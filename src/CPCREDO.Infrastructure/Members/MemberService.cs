using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Members;
using CPCREDO.Application.Savings;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Loans;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Teller;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Members;

public sealed class MemberService : IMemberService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;
    private readonly IJournalService _journals;
    private readonly IKycOverrideStore _overrides;
    private readonly PasswordHasher<User> _hasher = new();
    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(new User { Username = "dummy" }, "CpcredoDummyPassword!1");

    public MemberService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogger audit,
        IJournalService journals,
        IKycOverrideStore overrides)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _journals = journals;
        _overrides = overrides;
    }

    public async Task<Result<MemberListDto>> SearchAsync(
        string? query,
        MemberStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<MemberListDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        take = take is < 1 or > 100 ? 50 : take;
        skip = Math.Max(0, skip);

        var tenantId = _currentUser.TenantId!.Value;
        var members = _db.Members.AsNoTracking().Where(m => m.TenantId == tenantId);

        if (status is not null)
            members = members.Where(m => m.Status == status);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLower();
            members = members.Where(m =>
                m.MemberNo.ToLower().Contains(q)
                || m.FirstName.ToLower().Contains(q)
                || m.LastName.ToLower().Contains(q)
                || (m.Cin != null && m.Cin.ToLower().Contains(q))
                || (m.Nif != null && m.Nif.ToLower().Contains(q))
                || m.Phone.ToLower().Contains(q)
                || (m.AlternatePhone != null && m.AlternatePhone.ToLower().Contains(q)));
        }

        var total = await members.CountAsync(cancellationToken);
        var rows = await members
            .Include(m => m.ShareAccounts)
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var items = rows.Select(m => new MemberSummaryDto(
            m.Id,
            m.MemberNo,
            m.FirstName,
            m.LastName,
            m.FullName,
            m.Phone,
            m.City,
            m.Status.ToString(),
            m.KycStatus.ToString(),
            m.LegalStatus.ToString(),
            m.IsFounder,
            MembershipRules.HasVotingRights(m))).ToList();

        return Result<MemberListDto>.Ok(new MemberListDto(items, total));
    }

    public async Task<Result<Member360Dto>> Get360Async(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<Member360Dto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var member = await LoadAsync(id, _currentUser.TenantId!.Value, cancellationToken);
        if (member is null)
            return Result<Member360Dto>.Fail("member.not_found", "Membre introuvable.");

        return Result<Member360Dto>.Ok(await Build360Async(member, cancellationToken));
    }

    public async Task<Result<Member360Dto>> CreateAsync(MemberWriteRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireCreator();
        if (!gate.IsSuccess)
            return Result<Member360Dto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var tenantId = _currentUser.TenantId!.Value;
        var validation = Validate(request, isCreate: true);
        if (validation is not null)
            return validation;

        var duplicate = await FindDuplicateAsync(tenantId, request, excludeId: null, cancellationToken);
        if (duplicate is not null)
            return duplicate;

        var branchId = request.BranchId ?? _currentUser.BranchId!.Value;
        var branchExists = await _db.Branches.AnyAsync(
            b => b.Id == branchId && b.TenantId == tenantId && b.IsActive,
            cancellationToken);
        if (!branchExists)
            return Result<Member360Dto>.Fail("member.branch", "Agence introuvable.");

        var par = await LoadParValueAsync(tenantId, cancellationToken);
        var legal = request.LegalStatus ?? LegalStatus.Usager;
        var founder = request.IsFounder ?? false;
        var founderGroup = founder ? request.FounderGroup : null;
        var shares = Math.Max(0, request.QualificationShareCount ?? 0);
        var status = request.Status ?? MemberStatus.Pending;
        var classError = ValidateClass(legal, founder, founderGroup, shares, request.VotingRights, status);
        if (classError is not null)
            return classError;

        var memberNo = await NextNumberAsync(tenantId, "MemberNo", MembershipRules.MemberNoPrefix, cancellationToken);
        var shareAccountNo = await NextNumberAsync(tenantId, "ShareAccountNo", MembershipRules.ShareAccountNoPrefix, cancellationToken);

        var now = _clock.UtcNow;
        var memberId = Guid.NewGuid();
        var member = new Member
        {
            Id = memberId,
            TenantId = tenantId,
            BranchId = branchId,
            MemberNo = memberNo,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Cin = NormalizeOptional(request.Cin),
            Nif = NormalizeOptional(request.Nif),
            Phone = request.Phone.Trim(),
            AlternatePhone = NormalizeOptional(request.AlternatePhone),
            AddressLine = request.AddressLine.Trim(),
            City = string.IsNullOrWhiteSpace(request.City) ? Letterhead.City : request.City.Trim(),
            Commune = NormalizeOptional(request.Commune),
            DateOfBirth = request.DateOfBirth,
            PlaceOfBirth = NormalizeOptional(request.PlaceOfBirth),
            Occupation = NormalizeOptional(request.Occupation),
            Status = status,
            KycStatus = request.KycStatus ?? KycStatus.Incomplete,
            LegalStatus = legal,
            IsFounder = founder,
            FounderGroup = founderGroup,
            ProbationDays = MembershipRules.ClampProbationDays(request.ProbationDays),
            UsagerSinceUtc = legal == LegalStatus.Usager ? now : null,
            CreatedAtUtc = now
        };

        _db.Members.Add(member);
        _db.ShareAccounts.Add(new ShareAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MemberId = memberId,
            BranchId = branchId,
            AccountNo = shareAccountNo,
            ShareType = ShareType.Qualification,
            ShareCount = shares,
            ParValue = par,
            CurrencyCode = Currencies.Htg,
            IsActive = true,
            OpenedAtUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Member.Created",
            nameof(Member),
            member.Id,
            new { member.MemberNo, member.FullName },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return await Get360Async(member.Id, cancellationToken);
    }

    public async Task<Result<Member360Dto>> UpdateAsync(
        Guid id,
        MemberWriteRequest request,
        Guid? overrideGrantId = null,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<Member360Dto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var validation = Validate(request, isCreate: false);
        if (validation is not null)
            return validation;

        var tenantId = _currentUser.TenantId!.Value;
        var member = await LoadAsync(id, tenantId, cancellationToken);
        if (member is null)
            return Result<Member360Dto>.Fail("member.not_found", "Membre introuvable.");

        var identityGate = AuthorizeIdentityEdit(member, overrideGrantId);
        if (!identityGate.IsSuccess)
            return Result<Member360Dto>.Fail(identityGate.ErrorCode!, identityGate.ErrorMessage!);

        var before = SnapshotIdentity(member);

        var duplicate = await FindDuplicateAsync(tenantId, request, excludeId: id, cancellationToken);
        if (duplicate is not null)
            return duplicate;

        if (request.BranchId is { } branchId && branchId != member.BranchId)
        {
            var branchOk = await _db.Branches.AnyAsync(
                b => b.Id == branchId && b.TenantId == tenantId && b.IsActive,
                cancellationToken);
            if (!branchOk)
                return Result<Member360Dto>.Fail("member.branch", "Agence introuvable.");
            member.BranchId = branchId;
            foreach (var account in member.ShareAccounts)
                account.BranchId = branchId;
        }

        member.FirstName = request.FirstName.Trim();
        member.LastName = request.LastName.Trim();
        member.Cin = NormalizeOptional(request.Cin);
        member.Nif = NormalizeOptional(request.Nif);
        member.Phone = request.Phone.Trim();
        member.AlternatePhone = NormalizeOptional(request.AlternatePhone);

        member.AddressLine = request.AddressLine.Trim();
        member.City = string.IsNullOrWhiteSpace(request.City) ? member.City : request.City.Trim();
        member.Commune = NormalizeOptional(request.Commune);
        member.DateOfBirth = request.DateOfBirth;
        member.PlaceOfBirth = NormalizeOptional(request.PlaceOfBirth);
        member.Occupation = NormalizeOptional(request.Occupation);
        if (request.Status is not null)
            member.Status = request.Status.Value;
        if (request.KycStatus is not null)
            member.KycStatus = request.KycStatus.Value;
        if (request.LegalStatus is not null)
        {
            if (request.LegalStatus == LegalStatus.Usager && member.LegalStatus != LegalStatus.Usager)
                member.UsagerSinceUtc ??= _clock.UtcNow;
            member.LegalStatus = request.LegalStatus.Value;
        }
        if (request.IsFounder is not null)
            member.IsFounder = request.IsFounder.Value;
        member.FounderGroup = member.IsFounder ? (request.FounderGroup ?? member.FounderGroup) : null;
        if (request.ProbationDays is not null)
            member.ProbationDays = MembershipRules.ClampProbationDays(request.ProbationDays);
        member.UpdatedAtUtc = _clock.UtcNow;

        if (request.QualificationShareCount is not null)
        {
            if (request.QualificationShareCount.Value < 0)
                return Result<Member360Dto>.Fail("member.shares", "Le nombre de parts ne peut pas être négatif.");
            var qual = await EnsureShareAccountAsync(member, ShareType.Qualification, cancellationToken);
            qual.ShareCount = request.QualificationShareCount.Value;
        }

        var classError = ValidateClass(
            member.LegalStatus,
            member.IsFounder,
            member.FounderGroup,
            member.QualificationShareCount,
            request.VotingRights,
            member.Status);
        if (classError is not null)
            return classError;

        await _db.SaveChangesAsync(cancellationToken);
        var after = SnapshotIdentity(member);
        await _audit.LogAsync(
            "Member.Updated",
            nameof(Member),
            member.Id,
            new
            {
                member.MemberNo,
                actor = _currentUser.Username,
                authorizedBy = identityGate.Value?.AuthorizedByUsername,
                authorizedByUserId = identityGate.Value?.AuthorizedByUserId,
                before,
                after
            },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return await Get360Async(member.Id, cancellationToken);
    }

    public async Task<Result<Member360Dto>> ConvertToSocietaireAsync(
        Guid id,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<Member360Dto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var tenantId = _currentUser.TenantId!.Value;
        var member = await LoadAsync(id, tenantId, cancellationToken);
        if (member is null)
            return Result<Member360Dto>.Fail("member.not_found", "Membre introuvable.");
        if (member.LegalStatus == LegalStatus.Societaire)
            return Result<Member360Dto>.Fail("member.already_societaire", "Ce membre est déjà sociétaire.");
        if (!MembershipRules.IsKycActive(member.KycStatus))
            return Result<Member360Dto>.Fail("member.kyc_inactive", "La conversion en sociétaire exige un KYC actif (vérifié).");

        var par = await LoadParValueAsync(tenantId, cancellationToken);
        var posted = await PostShareJournalAsync(
            member,
            MembershipRules.QualificationCapitalGl,
            par,
            "Part de qualification",
            idempotencyKey,
            cancellationToken);
        if (!posted.IsSuccess)
            return Result<Member360Dto>.Fail(posted.ErrorCode!, posted.ErrorMessage!);

        var qual = await EnsureShareAccountAsync(member, ShareType.Qualification, cancellationToken);
        qual.ShareCount += 1;
        qual.ParValue = par;
        member.LegalStatus = LegalStatus.Societaire;
        member.Status = MemberStatus.Active;
        member.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Member.ConvertedToSocietaire",
            nameof(Member),
            member.Id,
            new { member.MemberNo, journalId = posted.Value!.Id },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return await Get360Async(member.Id, cancellationToken);
    }

    public async Task<Result<Member360Dto>> SubscribePermanentSharesAsync(
        Guid id,
        SubscribePermanentSharesRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<Member360Dto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        if (request.Quantity < 1)
            return Result<Member360Dto>.Fail("member.shares", "La souscription doit porter sur au moins une part permanente.");

        var tenantId = _currentUser.TenantId!.Value;
        var member = await LoadAsync(id, tenantId, cancellationToken);
        if (member is null)
            return Result<Member360Dto>.Fail("member.not_found", "Membre introuvable.");
        if (member.LegalStatus != LegalStatus.Societaire)
            return Result<Member360Dto>.Fail("member.not_societaire", "Seuls les sociétaires peuvent souscrire des parts permanentes.");
        if (MembershipRules.ServicesBlocked(member, _clock.UtcNow))
            return Result<Member360Dto>.Fail("member.usager_expired", "Période d’usage échue : conversion en sociétaire requise.");

        var par = await LoadParValueAsync(tenantId, cancellationToken);
        var amount = MembershipRules.BookValue(request.Quantity, par);
        var posted = await PostShareJournalAsync(
            member,
            MembershipRules.PermanentCapitalGl,
            amount,
            $"Parts permanentes × {request.Quantity}",
            idempotencyKey,
            cancellationToken);
        if (!posted.IsSuccess)
            return Result<Member360Dto>.Fail(posted.ErrorCode!, posted.ErrorMessage!);

        var permanent = await EnsureShareAccountAsync(member, ShareType.Permanent, cancellationToken);
        permanent.ShareCount += request.Quantity;
        permanent.ParValue = par;
        member.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Member.PermanentSharesSubscribed",
            nameof(Member),
            member.Id,
            new { member.MemberNo, request.Quantity, journalId = posted.Value!.Id },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return await Get360Async(member.Id, cancellationToken);
    }

    public async Task<Result<PaySharesResultDto>> PaySharesAsync(
        Guid id,
        PaySharesRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<PaySharesResultDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        if (!Enum.TryParse<ShareType>(request.ShareType, true, out var shareType))
            return Result<PaySharesResultDto>.Fail("member.share_type", "Type de parts invalide (Qualification ou Permanent).");
        var source = (request.Source ?? "Till").Trim();
        if (!source.Equals("Till", StringComparison.OrdinalIgnoreCase)
            && !source.Equals("Vault", StringComparison.OrdinalIgnoreCase)
            && !source.Equals("Coffre", StringComparison.OrdinalIgnoreCase))
            return Result<PaySharesResultDto>.Fail("member.share_source", "Source invalide (Caisse ou Coffre).");
        var useVault = source.Equals("Vault", StringComparison.OrdinalIgnoreCase)
                       || source.Equals("Coffre", StringComparison.OrdinalIgnoreCase);

        var tenantId = _currentUser.TenantId!.Value;
        var member = await LoadAsync(id, tenantId, cancellationToken);
        if (member is null)
            return Result<PaySharesResultDto>.Fail("member.not_found", "Membre introuvable.");
        if (MembershipRules.ServicesBlocked(member, _clock.UtcNow))
            return Result<PaySharesResultDto>.Fail("member.usager_expired", "Période d’usage échue : conversion en sociétaire requise.");
        if (shareType == ShareType.Qualification && member.LegalStatus == LegalStatus.Usager)
            return Result<PaySharesResultDto>.Fail(
                "savings.usager_parts",
                "Un usager ne peut pas payer de parts de qualification hors conversion.");
        if (shareType == ShareType.Permanent && member.LegalStatus == LegalStatus.Usager)
            return Result<PaySharesResultDto>.Fail(
                "savings.usager_permanent",
                "Un usager ne peut pas payer de parts permanentes.");
        if (shareType == ShareType.Permanent
            && member.LegalStatus != LegalStatus.Societaire
            && member.LegalStatus != LegalStatus.Auxiliaire)
            return Result<PaySharesResultDto>.Fail(
                "member.not_societaire",
                "Seuls les sociétaires et les auxiliaires paient des parts permanentes.");
        if (shareType == ShareType.Qualification && member.LegalStatus == LegalStatus.Auxiliaire)
            return Result<PaySharesResultDto>.Fail(
                "savings.auxiliaire_parts",
                "Un auxiliaire n’ouvre pas de parts de qualification.");

        var share = member.ShareAccounts.FirstOrDefault(s => s.ShareType == shareType && s.IsActive);
        if (share is null)
            return Result<PaySharesResultDto>.Fail(
                "savings.account.required",
                "Ouvrez d’abord le compte de parts correspondant.");

        var par = MembershipRules.NormalizeParValue(share.ParValue);
        if (par > MembershipRules.MaxShareParValue)
            par = MembershipRules.MaxShareParValue;

        int units;
        decimal amount;
        if (request.Units is > 0)
        {
            units = request.Units.Value;
            amount = MembershipRules.BookValue(units, par);
        }
        else if (request.Amount is > 0m)
        {
            amount = MoneyAmount.Normalize(request.Amount.Value);
            if (amount % par != 0m)
                return Result<PaySharesResultDto>.Fail(
                    "teller.share_par",
                    $"Le montant des parts doit être un multiple de la valeur nominale ({MoneyDisplay.Format(par, Currencies.Htg)}).");
            units = (int)(amount / par);
        }
        else
        {
            return Result<PaySharesResultDto>.Fail("teller.share_units", "Indiquez un nombre de parts ou un montant.");
        }

        if (units < 1)
            return Result<PaySharesResultDto>.Fail("teller.share_units", "Payez au moins une part.");

        Guid debitGl;
        if (useVault)
        {
            var vault = await _db.GlAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Code == "1030", cancellationToken);
            if (vault is null)
                return Result<PaySharesResultDto>.Fail("member.gl", "Compte coffre introuvable.");
            debitGl = vault.Id;
        }
        else
        {
            var till = await _db.TillSessions.FirstOrDefaultAsync(
                t => t.TenantId == tenantId
                     && t.UserId == _currentUser.UserId
                     && t.BranchId == _currentUser.BranchId
                     && t.CurrencyCode == Currencies.Htg
                     && t.Status == TillSessionStatus.Open,
                cancellationToken);
            if (till is null)
                return Result<PaySharesResultDto>.Fail(
                    "till.not_open",
                    "Ouvrez la caisse pour payer des parts en espèces.");
            debitGl = MembershipRules.CashHtgGl == "1010"
                ? (await _db.GlAccounts.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Code == MembershipRules.CashHtgGl, cancellationToken))?.Id
                  ?? Guid.Empty
                : Guid.Empty;
            if (debitGl == Guid.Empty)
                return Result<PaySharesResultDto>.Fail("member.gl", "Compte de caisse introuvable.");

            till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash + amount);
        }

        var capitalCode = shareType == ShareType.Qualification
            ? MembershipRules.QualificationCapitalGl
            : MembershipRules.PermanentCapitalGl;
        var capital = await _db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Code == capitalCode, cancellationToken);
        if (capital is null)
            return Result<PaySharesResultDto>.Fail("member.gl", "Compte de capital introuvable.");

        var label = shareType == ShareType.Qualification ? "Parts de qualification" : "Parts permanentes";
        var posted = await _journals.PostAsync(new CreateJournalRequest
        {
            Description = $"Paiement {label} — {member.MemberNo} {member.FullName}",
            CurrencyCode = Currencies.Htg,
            BranchId = member.BranchId,
            Lines =
            [
                new CreateJournalLineRequest
                {
                    GlAccountId = debitGl,
                    Debit = amount,
                    Credit = 0m,
                    Description = useVault ? "Coffre HTG" : "Caisse HTG"
                },
                new CreateJournalLineRequest
                {
                    GlAccountId = capital.Id,
                    Debit = 0m,
                    Credit = amount,
                    Description = label
                }
            ]
        }, idempotencyKey, cancellationToken);
        if (!posted.IsSuccess)
            return Result<PaySharesResultDto>.Fail(posted.ErrorCode!, posted.ErrorMessage!);

        share.ShareCount += units;
        share.ParValue = par;
        member.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Member.SharesPaid",
            nameof(Member),
            member.Id,
            new { shareType = shareType.ToString(), units, amount, source, journalId = posted.Value!.Id },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        var votes = MembershipRules.HasVotingRights(member);
        return Result<PaySharesResultDto>.Ok(new PaySharesResultDto(
            member.Id,
            shareType.ToString(),
            units,
            amount,
            useVault ? "Vault" : "Till",
            posted.Value.Id,
            member.QualificationShareCount,
            member.PermanentShareCount,
            votes));
    }

    public async Task<Result<AgExportDto>> GetAgExportAsync(CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<AgExportDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var members = await _db.Members
            .AsNoTracking()
            .Include(m => m.ShareAccounts)
            .Where(m => m.TenantId == _currentUser.TenantId)
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .ToListAsync(cancellationToken);

        var voters = members
            .Where(MembershipRules.HasVotingRights)
            .Select(m => new AgVoterDto(
                m.Id,
                m.MemberNo,
                m.FullName,
                m.LegalStatus.ToString(),
                m.FounderGroup?.ToString(),
                m.QualificationShareCount,
                m.PermanentShareCount,
                1))
            .ToList();

        return Result<AgExportDto>.Ok(new AgExportDto(
            _clock.TodayInPortAuPrince(),
            MembershipRules.VoteRule,
            voters.Count,
            voters.Count,
            voters));
    }

    public async Task<Result<bool>> AssertCapabilityAsync(
        Guid memberId,
        string capability,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<bool>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var member = await _db.Members.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == _currentUser.TenantId, cancellationToken);
        if (member is null)
            return Result<bool>.Fail("member.not_found", "Membre introuvable.");

        var key = (capability ?? string.Empty).Trim();
        if (key.Equals(MembershipRules.Micro90Product, StringComparison.OrdinalIgnoreCase)
            && !MembershipRules.AllowsMicro90(member.LegalStatus))
            return Result<bool>.Fail("member.usager_micro90", "CT90 est interdit aux usagers. Conversion en sociétaire requise.");
        if (key.Equals(MembershipRules.OfficerCapability, StringComparison.OrdinalIgnoreCase)
            && !MembershipRules.AllowsOfficerRole(member.LegalStatus))
            return Result<bool>.Fail("member.usager_officer", "Les fonctions d’officier sont interdites aux usagers.");

        return Result<bool>.Ok(true);
    }

    public async Task<Result<MemberTicketDto>> OpenTicketAsync(
        Guid memberId,
        OpenTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<MemberTicketDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        if (string.IsNullOrWhiteSpace(request.Subject))
            return Result<MemberTicketDto>.Fail("ticket.subject", "L’objet du ticket est obligatoire.");

        var tenantId = _currentUser.TenantId!.Value;
        var memberExists = await _db.Members.AnyAsync(m => m.Id == memberId && m.TenantId == tenantId, cancellationToken);
        if (!memberExists)
            return Result<MemberTicketDto>.Fail("member.not_found", "Membre introuvable.");

        Guid? assignee = request.AssignToUserId;
        if (assignee is { } userId)
        {
            var ok = await _db.Users.AnyAsync(u => u.Id == userId && u.TenantId == tenantId && u.IsActive, cancellationToken);
            if (!ok)
                return Result<MemberTicketDto>.Fail("ticket.assignee", "Collaborateur introuvable.");
        }

        var now = _clock.UtcNow;
        var ticket = new MemberTicket
        {
            TenantId = tenantId,
            MemberId = memberId,
            TicketNo = await NextNumberAsync(tenantId, "TicketNo", "TK-", cancellationToken),
            Subject = request.Subject.Trim(),
            Body = (request.Body ?? string.Empty).Trim(),
            Status = assignee is null ? TicketStatus.Open : TicketStatus.Assigned,
            CreatedByUserId = _currentUser.UserId!.Value,
            AssignedToUserId = assignee,
            CreatedAtUtc = now,
            AssignedAtUtc = assignee is null ? null : now
        };
        _db.MemberTickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Ticket.Opened",
            nameof(MemberTicket),
            ticket.Id,
            new { ticket.TicketNo, ticket.Subject },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<MemberTicketDto>.Ok(await MapTicketAsync(ticket, cancellationToken));
    }

    public async Task<Result<MemberTicketDto>> AssignTicketAsync(
        Guid memberId,
        Guid ticketId,
        AssignTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<MemberTicketDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var ticket = await LoadTicketAsync(memberId, ticketId, cancellationToken);
        if (ticket is null)
            return Result<MemberTicketDto>.Fail("ticket.not_found", "Ticket introuvable.");
        if (ticket.Status == TicketStatus.Closed)
            return Result<MemberTicketDto>.Fail("ticket.closed", "Ce ticket est déjà clôturé.");

        var assigneeOk = await _db.Users.AnyAsync(
            u => u.Id == request.AssignedToUserId && u.TenantId == _currentUser.TenantId && u.IsActive,
            cancellationToken);
        if (!assigneeOk)
            return Result<MemberTicketDto>.Fail("ticket.assignee", "Collaborateur introuvable.");

        ticket.AssignedToUserId = request.AssignedToUserId;
        ticket.AssignedAtUtc = _clock.UtcNow;
        ticket.Status = TicketStatus.Assigned;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Ticket.Assigned",
            nameof(MemberTicket),
            ticket.Id,
            new { ticket.TicketNo, request.AssignedToUserId },
            ticket.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<MemberTicketDto>.Ok(await MapTicketAsync(ticket, cancellationToken));
    }

    public async Task<Result<MemberTicketDto>> CloseTicketAsync(
        Guid memberId,
        Guid ticketId,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<MemberTicketDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var ticket = await LoadTicketAsync(memberId, ticketId, cancellationToken);
        if (ticket is null)
            return Result<MemberTicketDto>.Fail("ticket.not_found", "Ticket introuvable.");
        if (ticket.Status == TicketStatus.Closed)
            return Result<MemberTicketDto>.Fail("ticket.closed", "Ce ticket est déjà clôturé.");

        ticket.Status = TicketStatus.Closed;
        ticket.ClosedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Ticket.Closed",
            nameof(MemberTicket),
            ticket.Id,
            new { ticket.TicketNo },
            ticket.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<MemberTicketDto>.Ok(await MapTicketAsync(ticket, cancellationToken));
    }

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null || _currentUser.BranchId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireWriter()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!_currentUser.Roles.Any(r => RoleNames.WriteRoles.Contains(r)))
            return Result<bool>.Fail("auth.forbidden", "Le commissaire a un accès en lecture seule.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireCreator()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!_currentUser.Roles.Any(r => RoleNames.MemberCreateRoles.Contains(r)))
            return Result<bool>.Fail("auth.forbidden", "Vous ne pouvez pas créer de membre.");
        return Result<bool>.Ok(true);
    }

    private bool IsIdentityEditor() =>
        _currentUser.Roles.Any(r => RoleNames.KycManageRoles.Contains(r));

    private Result<KycOverrideGrant?> AuthorizeIdentityEdit(Member member, Guid? overrideGrantId)
    {
        if (IsIdentityEditor())
            return Result<KycOverrideGrant?>.Ok(null);
        if (overrideGrantId is Guid grantId
            && _overrides.TryConsume(
                grantId,
                _currentUser.UserId!.Value,
                member.Id,
                "edit-fiche",
                _clock.UtcNow,
                out var grant))
            return Result<KycOverrideGrant?>.Ok(grant);
        return Result<KycOverrideGrant?>.Fail("member.locked", "Fiche verrouillée — demandez au gérant.");
    }

    public async Task<Result<KycOverrideAuthResponse>> AuthorizeFicheOverrideAsync(
        Guid memberId,
        KycOverrideAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<KycOverrideAuthResponse>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        if (IsIdentityEditor())
            return Result<KycOverrideAuthResponse>.Fail("member.override_unnecessary", "Le gérant ou l’administrateur peut modifier sans autorisation.");
        if (!_currentUser.Roles.Any(r => RoleNames.WriteRoles.Contains(r)))
            return Result<KycOverrideAuthResponse>.Fail("auth.forbidden", "Consultation uniquement.");

        var action = (request.Action ?? "edit-fiche").Trim().ToLowerInvariant();
        if (action is not "edit-fiche")
            return Result<KycOverrideAuthResponse>.Fail("member.override_action", "Action invalide. Utilisez edit-fiche.");

        var member = await LoadAsync(memberId, _currentUser.TenantId!.Value, cancellationToken);
        if (member is null)
            return Result<KycOverrideAuthResponse>.Fail("member.not_found", "Membre introuvable.");

        var username = request.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
        {
            _hasher.VerifyHashedPassword(new User { Username = username }, DummyHash, request.Password ?? string.Empty);
            return Result<KycOverrideAuthResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");
        }

        var manager = await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(
                u => u.TenantId == _currentUser.TenantId && u.Username.ToLower() == username.ToLower(),
                cancellationToken);
        if (manager is null || !manager.IsActive)
        {
            _hasher.VerifyHashedPassword(new User { Username = username }, DummyHash, request.Password);
            return Result<KycOverrideAuthResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");
        }

        var verify = _hasher.VerifyHashedPassword(manager, manager.PasswordHash, request.Password);
        var isManagerRole = manager.UserRoles.Any(ur =>
            ur.Role is not null && RoleNames.KycManageRoles.Contains(ur.Role.Name));
        if (verify == PasswordVerificationResult.Failed || !isManagerRole)
            return Result<KycOverrideAuthResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");

        var expires = _clock.UtcNow.AddMinutes(5);
        var grant = _overrides.Issue(new KycOverrideGrant(
            Guid.NewGuid(),
            _currentUser.UserId!.Value,
            member.Id,
            "edit-fiche",
            manager.Id,
            manager.Username,
            expires));

        await _audit.LogAsync(
            "Member.OverrideAuthorized",
            nameof(Member),
            member.Id,
            new
            {
                actor = _currentUser.Username,
                authorizedBy = manager.Username,
                authorizedByUserId = manager.Id,
                action = "edit-fiche",
                memberId = member.Id,
                memberNo = member.MemberNo
            },
            member.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<KycOverrideAuthResponse>.Ok(
            new KycOverrideAuthResponse(grant.GrantId, member.Id, "edit-fiche", expires));
    }

    private static object SnapshotIdentity(Member member) => new
    {
        member.FirstName,
        member.LastName,
        member.Cin,
        member.Nif,
        member.Phone,
        member.AlternatePhone,
        member.AddressLine,
        member.City,
        member.Commune,
        member.DateOfBirth,
        member.PlaceOfBirth,
        member.Occupation,
        status = member.Status.ToString(),
        kycStatus = member.KycStatus.ToString(),
        legalStatus = member.LegalStatus.ToString(),
        member.IsFounder,
        founderGroup = member.FounderGroup?.ToString()
    };

    private static Result<Member360Dto>? Validate(MemberWriteRequest request, bool isCreate)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return Result<Member360Dto>.Fail("member.name", "Le prénom et le nom sont obligatoires.");
        if (string.IsNullOrWhiteSpace(request.Cin) && string.IsNullOrWhiteSpace(request.Nif))
            return Result<Member360Dto>.Fail("member.identity", "Le CIN ou le NIF est obligatoire.");
        if (string.IsNullOrWhiteSpace(request.Phone))
            return Result<Member360Dto>.Fail("member.phone", "Un numéro de téléphone est obligatoire.");
        if (isCreate && string.IsNullOrWhiteSpace(request.AddressLine))
            return Result<Member360Dto>.Fail("member.address", "L’adresse est obligatoire.");
        if (request.QualificationShareCount is < 0)
            return Result<Member360Dto>.Fail("member.shares", "Le nombre de parts ne peut pas être négatif.");
        if (request.ProbationDays is > MembershipRules.MaxProbationDays)
            return Result<Member360Dto>.Fail("member.probation", "La période d’usage ne peut pas dépasser 180 jours.");
        return null;
    }

    private static Result<Member360Dto>? ValidateClass(
        LegalStatus legal,
        bool isFounder,
        FounderGroup? founderGroup,
        int qualificationShares,
        bool? votingRights,
        MemberStatus status)
    {
        if (isFounder && legal != LegalStatus.Societaire)
            return Result<Member360Dto>.Fail("member.founder", "Un fondateur doit être sociétaire.");
        if (isFounder && founderGroup is null)
            return Result<Member360Dto>.Fail("member.founder_group", "Le groupe de fondateurs est obligatoire.");
        if (!isFounder && founderGroup is not null)
            return Result<Member360Dto>.Fail("member.founder_group", "Le groupe de fondateurs n’est renseigné que pour un fondateur.");
        if (votingRights == true && !MembershipRules.HasVotingRights(legal, status, qualificationShares))
            return Result<Member360Dto>.Fail(
                "member.voting_requires_qualification",
                "Le droit de vote exige le statut sociétaire, un membre actif et au moins une part de qualification libérée.");
        return null;
    }

    private async Task<Result<Member360Dto>?> FindDuplicateAsync(
        Guid tenantId,
        MemberWriteRequest request,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var cin = NormalizeOptional(request.Cin);
        var nif = NormalizeOptional(request.Nif);

        if (cin is not null)
        {
            var exists = await _db.Members.AnyAsync(
                m => m.TenantId == tenantId && m.Cin == cin && (excludeId == null || m.Id != excludeId),
                cancellationToken);
            if (exists)
                return Result<Member360Dto>.Fail("member.duplicate_cin", "Un membre existe déjà avec ce CIN.");
        }

        if (nif is not null)
        {
            var exists = await _db.Members.AnyAsync(
                m => m.TenantId == tenantId && m.Nif == nif && (excludeId == null || m.Id != excludeId),
                cancellationToken);
            if (exists)
                return Result<Member360Dto>.Fail("member.duplicate_nif", "Un membre existe déjà avec ce NIF.");
        }

        return null;
    }

    private async Task<string> NextNumberAsync(Guid tenantId, string key, string prefix, CancellationToken cancellationToken)
    {
        var sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Key == key, cancellationToken);

        if (sequence is null)
        {
            sequence = new NumberSequence
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Key = key,
                LastValue = 0
            };
            _db.NumberSequences.Add(sequence);
        }

        sequence.LastValue += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return $"{prefix}{sequence.LastValue:000000}";
    }

    private Task<Member?> LoadAsync(Guid id, Guid tenantId, CancellationToken cancellationToken) =>
        _db.Members
            .Include(m => m.ShareAccounts)
            .Include(m => m.Branch)
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, cancellationToken);

    private async Task<decimal> LoadParValueAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var parRaw = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.ShareParValue)
            .FirstAsync(cancellationToken);
        return MembershipRules.NormalizeParValue(parRaw);
    }

    private async Task<ShareAccount> EnsureShareAccountAsync(
        Member member,
        ShareType type,
        CancellationToken cancellationToken)
    {
        var existing = member.ShareAccounts.FirstOrDefault(s => s.ShareType == type);
        if (existing is not null)
            return existing;

        var prefix = type == ShareType.Permanent
            ? MembershipRules.PermanentShareAccountNoPrefix
            : MembershipRules.ShareAccountNoPrefix;
        var key = type == ShareType.Permanent ? "PermanentShareAccountNo" : "ShareAccountNo";
        var par = await LoadParValueAsync(member.TenantId, cancellationToken);
        var account = new ShareAccount
        {
            Id = Guid.NewGuid(),
            TenantId = member.TenantId,
            MemberId = member.Id,
            BranchId = member.BranchId,
            AccountNo = await NextNumberAsync(member.TenantId, key, prefix, cancellationToken),
            ShareType = type,
            ShareCount = 0,
            ParValue = par,
            CurrencyCode = Currencies.Htg,
            IsActive = true,
            OpenedAtUtc = _clock.UtcNow
        };
        member.ShareAccounts.Add(account);
        _db.ShareAccounts.Add(account);
        return account;
    }

    private async Task<Result<JournalDto>> PostShareJournalAsync(
        Member member,
        string capitalCode,
        decimal amount,
        string description,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var cash = await _db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == member.TenantId && a.Code == MembershipRules.CashHtgGl, cancellationToken);
        var capital = await _db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == member.TenantId && a.Code == capitalCode, cancellationToken);
        if (cash is null || capital is null)
            return Result<JournalDto>.Fail("member.gl", "Comptes de capital ou de caisse introuvables.");

        return await _journals.PostAsync(new CreateJournalRequest
        {
            Description = $"{description} — {member.MemberNo} {member.FullName}",
            CurrencyCode = Currencies.Htg,
            BranchId = member.BranchId,
            Lines =
            [
                new CreateJournalLineRequest { GlAccountId = cash.Id, Debit = amount, Credit = 0m, Description = "Caisse HTG" },
                new CreateJournalLineRequest { GlAccountId = capital.Id, Debit = 0m, Credit = amount, Description = description }
            ]
        }, idempotencyKey, cancellationToken);
    }

    private Task<MemberTicket?> LoadTicketAsync(Guid memberId, Guid ticketId, CancellationToken cancellationToken) =>
        _db.MemberTickets.FirstOrDefaultAsync(
            t => t.Id == ticketId && t.MemberId == memberId && t.TenantId == _currentUser.TenantId,
            cancellationToken);

    private async Task<MemberTicketDto> MapTicketAsync(MemberTicket ticket, CancellationToken cancellationToken)
    {
        var ids = new List<Guid> { ticket.CreatedByUserId };
        if (ticket.AssignedToUserId is { } assigned)
            ids.Add(assigned);
        var names = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        return new MemberTicketDto(
            ticket.Id,
            ticket.TicketNo,
            ticket.Subject,
            ticket.Body,
            ticket.Status.ToString(),
            ticket.CreatedByUserId,
            names.GetValueOrDefault(ticket.CreatedByUserId, string.Empty),
            ticket.AssignedToUserId,
            ticket.AssignedToUserId is { } a ? names.GetValueOrDefault(a) : null,
            ticket.CreatedAtUtc,
            ticket.ClosedAtUtc);
    }

    private async Task<Member360Dto> Build360Async(Member member, CancellationToken cancellationToken)
    {
        var votes = MembershipRules.HasVotingRights(member);
        var qual = member.ShareAccounts.FirstOrDefault(s => s.ShareType == ShareType.Qualification);
        var perm = member.ShareAccounts.FirstOrDefault(s => s.ShareType == ShareType.Permanent);
        var par = qual?.ParValue ?? perm?.ParValue ?? MembershipRules.DefaultShareParValue;

        var accounts = await _db.SavingsAccounts.AsNoTracking()
            .Include(a => a.Product)
            .Where(a => a.TenantId == member.TenantId && a.MemberId == member.Id)
            .OrderBy(a => a.AccountNo)
            .ToListAsync(cancellationToken);

        var savings = new List<SavingsAccountDto>();
        foreach (var account in accounts)
        {
            var entries = await _db.SavingsLedgerEntries.AsNoTracking()
                .Where(x => x.SavingsAccountId == account.Id)
                .ToListAsync(cancellationToken);
            var ledger = MoneyAmount.Normalize(entries.Sum(x => x.SignedAmount));
            var liens = await _db.SavingsLiens.AsNoTracking()
                .Where(x => x.SavingsAccountId == account.Id && x.ReleasedAtUtc == null)
                .ToListAsync(cancellationToken);
            var available = MoneyAmount.Normalize(ledger - liens.Sum(x => x.Amount));
            var holds = liens
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new SavingsHoldDto(x.Id, x.Amount, x.Reason, x.CreatedAtUtc))
                .ToList();
            savings.Add(new SavingsAccountDto(
                account.Id,
                account.MemberId,
                account.ProductId,
                account.AccountNo,
                account.Product?.DisplayName ?? account.Product?.Name ?? string.Empty,
                account.CurrencyCode,
                ledger,
                available,
                account.IsActive,
                account.IsBlocked,
                account.BlockedReason,
                account.OpenedAtUtc,
                account.LastPassbookPrintAtUtc,
                account.MaturesOn,
                account.AllowWithdrawBeforeTerm,
                account.Product?.ProductKind.ToString() ?? "AVue",
                holds));
        }

        var accountIds = accounts.Select(a => a.Id).ToList();
        var recent = accountIds.Count == 0
            ? []
            : await (
                from e in _db.SavingsLedgerEntries.AsNoTracking()
                join a in _db.SavingsAccounts.AsNoTracking() on e.SavingsAccountId equals a.Id
                where accountIds.Contains(e.SavingsAccountId)
                orderby e.PostedAtUtc descending
                select new { e, a.AccountNo })
                .Take(50)
                .ToListAsync(cancellationToken);

        var transactions = recent.Select(x => new MemberTransactionDto(
            x.e.SavingsAccountId,
            x.AccountNo,
            x.e.ValueDateUtc,
            x.e.PostedAtUtc,
            _clock.ToPortAuPrince(x.e.PostedAtUtc),
            x.e.EntryType,
            MoneyAmount.Normalize(x.e.Amount),
            x.e.CurrencyCode,
            x.e.Description)).ToList();

        var ticketEntities = await _db.MemberTickets.AsNoTracking()
            .Where(t => t.TenantId == member.TenantId && t.MemberId == member.Id)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var tickets = new List<MemberTicketDto>();
        foreach (var ticket in ticketEntities)
            tickets.Add(await MapTicketAsync(ticket, cancellationToken));

        var memberLoans = await _db.Loans.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.TenantId == member.TenantId && l.MemberId == member.Id)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var previousIds = memberLoans
            .Where(l => l.RenewedFromLoanId is not null)
            .Select(l => l.RenewedFromLoanId!.Value)
            .Distinct()
            .ToList();
        var previousPrincipals = previousIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.Loans.AsNoTracking()
                .Where(l => previousIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Principal, cancellationToken);
        var loanSummaries = memberLoans.Select(l =>
        {
            decimal? prev = l.RenewedFromLoanId is { } pid && previousPrincipals.TryGetValue(pid, out var p) ? p : null;
            return new MemberLoanSummaryDto(
                l.Id,
                l.LoanNo,
                l.Status.ToString(),
                l.CycleNumber,
                l.Product?.MaxRenewals,
                LoanEvergreen.RemainingRenewals(l.CycleNumber, l.Product?.MaxRenewals),
                LoanEvergreen.Matches(l.CycleNumber, l.Principal, prev),
                l.Principal,
                l.CurrencyCode);
        }).ToList();

        var kycRows = await _db.KycDocuments.AsNoTracking()
            .Where(d => d.TenantId == member.TenantId && d.MemberId == member.Id)
            .OrderBy(d => d.Type)
            .ToListAsync(cancellationToken);
        var uploaderIds = kycRows.Select(d => d.UploadedBy).Distinct().ToList();
        var uploaderNames = uploaderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => uploaderIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var kycDocuments = kycRows.Select(d => new KycDocumentDto(
            d.Id,
            d.Type.ToString(),
            d.ContentType,
            Path.GetFileName(d.FilePath.Replace('\\', '/')),
            d.UploadedAtUtc,
            d.UploadedBy,
            uploaderNames.GetValueOrDefault(d.UploadedBy, string.Empty))).ToList();

        return new Member360Dto(
            member.Id,
            member.MemberNo,
            member.FirstName,
            member.LastName,
            member.FullName,
            member.Cin,
            member.Nif,
            member.Phone,
            member.AlternatePhone,
            member.AddressLine,
            member.City,
            member.Commune,
            member.DateOfBirth,
            member.PlaceOfBirth,
            member.Occupation,
            member.BranchId,
            member.Branch?.Name ?? string.Empty,
            member.Status.ToString(),
            member.KycStatus.ToString(),
            member.LegalStatus.ToString(),
            member.IsFounder,
            member.FounderGroup?.ToString(),
            member.UsagerSinceUtc,
            member.ProbationDays,
            member.LegalStatus == LegalStatus.Usager ? MembershipRules.UsagerDaysElapsed(member, _clock.UtcNow) : null,
            MembershipRules.UsagerDaysLeft(member, _clock.UtcNow),
            MembershipRules.ServicesBlocked(member, _clock.UtcNow),
            votes,
            MembershipRules.VoteRule,
            member.CreatedAtUtc,
            _clock.ToPortAuPrince(member.CreatedAtUtc),
            member.UpdatedAtUtc,
            new ShareHoldingsDto(
                qual?.AccountNo,
                perm?.AccountNo,
                member.QualificationShareCount,
                member.PermanentShareCount,
                MoneyAmount.Normalize(par),
                Currencies.Htg,
                MembershipRules.BookValue(member.QualificationShareCount, par),
                MembershipRules.BookValue(member.PermanentShareCount, par),
                votes,
                MembershipRules.VoteRule),
            savings,
            transactions,
            tickets,
            loanSummaries.Count == 0 ? "Crédits: aucun" : $"Crédits: {loanSummaries.Count}",
            loanSummaries,
            kycDocuments);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
