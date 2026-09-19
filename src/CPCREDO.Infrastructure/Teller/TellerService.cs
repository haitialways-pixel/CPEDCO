using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Teller;
using CPCREDO.Domain.Treasury;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Teller;

public sealed class TellerService : ITellerService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IJournalService _journals;
    private readonly IAuditLogger _audit;

    public TellerService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IJournalService journals,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _journals = journals;
        _audit = audit;
    }

    public async Task<Result<TillSessionDto>> OpenAsync(OpenTillRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<TillSessionDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var currency = NormalizeCurrency(request.CurrencyCode);
        if (currency is null)
            return Result<TillSessionDto>.Fail("till.currency", "Devise non supportée.");

        if (request.OpeningFloat is null)
            return Result<TillSessionDto>.Fail("till.float", "Les espèces en main sont obligatoires.");
        var floatAmount = MoneyAmount.Normalize(request.OpeningFloat.Value);
        if (floatAmount < 0m)
            return Result<TillSessionDto>.Fail("till.float", "Le fond de caisse ne peut pas être négatif.");

        var tenantId = _currentUser.TenantId!.Value;
        var userId = _currentUser.UserId!.Value;
        var branchId = _currentUser.BranchId!.Value;

        var openExists = await _db.TillSessions.AnyAsync(
            t => t.TenantId == tenantId && t.UserId == userId && t.BranchId == branchId
                 && t.CurrencyCode == currency && t.Status == TillSessionStatus.Open,
            cancellationToken);
        if (openExists)
            return Result<TillSessionDto>.Fail("till.already_open", "Une caisse est déjà ouverte pour cet utilisateur, cette agence et cette devise.");

        var till = new TillSession
        {
            TenantId = tenantId,
            BranchId = branchId,
            UserId = userId,
            CurrencyCode = currency,
            Status = TillSessionStatus.Open,
            OpeningFloat = floatAmount,
            ExpectedCash = floatAmount,
            OpenedAtUtc = _clock.UtcNow
        };
        _db.TillSessions.Add(till);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Till.Opened",
            nameof(TillSession),
            till.Id,
            new { till.CurrencyCode, till.OpeningFloat },
            tenantId,
            userId,
            cancellationToken: cancellationToken);

        return Result<TillSessionDto>.Ok(MapTill(till));
    }

    public async Task<Result<TillSessionDto>> GetCurrentAsync(string? currencyCode, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TillSessionDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var currency = NormalizeCurrency(currencyCode) ?? Currencies.Htg;
        var till = await FindOpenTillAsync(currency, cancellationToken);
        if (till is null)
            return Result<TillSessionDto>.Fail("till.not_open", "Aucune caisse ouverte pour cet utilisateur et cette agence.");

        return Result<TillSessionDto>.Ok(MapTill(till));
    }

    public async Task<Result<TillSessionDto>> CloseAsync(
        Guid tillId,
        CloseTillRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<TillSessionDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var till = await _db.TillSessions
            .Include(t => t.CountLines)
            .FirstOrDefaultAsync(
                t => t.Id == tillId && t.TenantId == _currentUser.TenantId && t.UserId == _currentUser.UserId,
                cancellationToken);
        if (till is null)
            return Result<TillSessionDto>.Fail("till.not_found", "Caisse introuvable.");
        if (till.Status != TillSessionStatus.Open)
            return Result<TillSessionDto>.Fail("till.already_closed", "Cette caisse est déjà fermée.");

        var pendingInternal = await _db.InternalCashMovements.AnyAsync(
            m => m.TenantId == till.TenantId
                 && m.Status == InternalCashStatus.Pending
                 && (m.SourceTillSessionId == till.Id || m.DestinationTillSessionId == till.Id),
            cancellationToken);
        if (pendingInternal)
            return Result<TillSessionDto>.Fail(
                "till.pending_internal",
                "Un mouvement interne est en attente. Acceptez-le avant de fermer la caisse.");

        if (request.CountedBalance is null)
            return Result<TillSessionDto>.Fail("till.counted", "Le solde compté est obligatoire.");
        if (request.CountedBalance.Value < 0m)
            return Result<TillSessionDto>.Fail("till.counted", "Le solde compté ne peut pas être négatif.");

        var counted = MoneyAmount.Normalize(request.CountedBalance.Value);
        var denomTotal = 0m;
        till.CountLines.Clear();
        foreach (var line in request.Denominations ?? [])
        {
            if (line.Quantity < 0 || line.FaceValue <= 0m)
                return Result<TillSessionDto>.Fail("till.count", "Dénominations invalides.");
            if (line.Quantity == 0)
                continue;
            var qty = line.Quantity;
            var face = MoneyAmount.Normalize(line.FaceValue);
            denomTotal += face * qty;
            till.CountLines.Add(new TillCountLine
            {
                TillSessionId = till.Id,
                FaceValue = face,
                Quantity = qty
            });
        }

        denomTotal = MoneyAmount.Normalize(denomTotal);
        if (till.CountLines.Count > 0 && denomTotal != counted)
            return Result<TillSessionDto>.Fail(
                "till.count_mismatch",
                "Le détail des coupures doit totaliser le solde compté.");

        var expected = MoneyAmount.Normalize(till.ExpectedCash);
        var overShort = MoneyAmount.Normalize(counted - expected);

        if (overShort != 0m && string.IsNullOrWhiteSpace(request.Notes))
            return Result<TillSessionDto>.Fail(
                "till.notes",
                "Une note est obligatoire lorsque l’écart n’est pas nul.");
        if (request.Notes is { Length: > 512 })
            return Result<TillSessionDto>.Fail("till.notes", "La note ne peut pas dépasser 512 caractères.");

        till.CountedCash = counted;
        till.OverShortAmount = overShort;
        till.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        till.Status = TillSessionStatus.Closed;
        till.ClosedAtUtc = _clock.UtcNow;

        if (overShort != 0m)
        {
            var cashGl = CashGl(till.CurrencyCode);
            var pnlGl = overShort > 0m
                ? SeedGuids.Gl("4040")
                : SeedGuids.Gl("5050");

            var abs = overShort > 0m ? overShort : -overShort;
            var journal = await _journals.PostAsync(new CreateJournalRequest
            {
                Description = overShort > 0m
                    ? $"Excédent de caisse {till.CurrencyCode}"
                    : $"Manquant de caisse {till.CurrencyCode}",
                CurrencyCode = till.CurrencyCode,
                BranchId = till.BranchId,
                Lines = overShort > 0m
                    ?
                    [
                        new CreateJournalLineRequest { GlAccountId = cashGl, Debit = abs, Credit = 0m, Description = "Caisse" },
                        new CreateJournalLineRequest { GlAccountId = pnlGl, Debit = 0m, Credit = abs, Description = "Écart de caisse" }
                    ]
                    :
                    [
                        new CreateJournalLineRequest { GlAccountId = pnlGl, Debit = abs, Credit = 0m, Description = "Écart de caisse" },
                        new CreateJournalLineRequest { GlAccountId = cashGl, Debit = 0m, Credit = abs, Description = "Caisse" }
                    ]
            }, idempotencyKey, cancellationToken);

            if (!journal.IsSuccess)
                return Result<TillSessionDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

            till.OverShortJournalId = journal.Value!.Id;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Till.Closed",
            nameof(TillSession),
            till.Id,
            new { till.ExpectedCash, till.CountedCash, till.OverShortAmount, till.Notes },
            till.TenantId,
            till.UserId,
            cancellationToken: cancellationToken);

        return Result<TillSessionDto>.Ok(MapTill(till));
    }

    public async Task<Result<IReadOnlyList<OpenTillPeerDto>>> ListOpenTillsAsync(
        string? currencyCode,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<OpenTillPeerDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var currency = NormalizeCurrency(currencyCode) ?? Currencies.Htg;
        var tills = await _db.TillSessions.AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.TenantId == _currentUser.TenantId
                        && t.BranchId == _currentUser.BranchId
                        && t.CurrencyCode == currency
                        && t.Status == TillSessionStatus.Open)
            .OrderBy(t => t.User!.FullName)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<OpenTillPeerDto>>.Ok(
            tills.Select(t => new OpenTillPeerDto(
                t.Id,
                t.UserId,
                t.User?.FullName ?? string.Empty,
                t.CurrencyCode,
                t.ExpectedCash)).ToList());
    }

    public async Task<Result<IReadOnlyList<InternalCashMovementDto>>> ListInternalMovementsAsync(
        string? currencyCode,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<InternalCashMovementDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var currency = NormalizeCurrency(currencyCode) ?? Currencies.Htg;
        var day = _clock.TodayInPortAuPrince();
        var start = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddHours(-6);
        var end = start.AddDays(2);

        var items = await QueryMovements()
            .Where(m => m.TenantId == _currentUser.TenantId
                        && m.BranchId == _currentUser.BranchId
                        && m.CurrencyCode == currency
                        && m.CreatedAtUtc >= start && m.CreatedAtUtc < end)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<InternalCashMovementDto>>.Ok(
            items.Where(m => DateOnly.FromDateTime(_clock.ToPortAuPrince(m.CreatedAtUtc)) == day)
                .Select(MapMovement)
                .ToList());
    }

    public async Task<Result<InternalCashMovementDto>> CreateInternalMovementAsync(
        CreateInternalCashRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<InternalCashMovementDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await QueryMovements().FirstOrDefaultAsync(
                m => m.TenantId == _currentUser.TenantId && m.IdempotencyKey == idempotencyKey.Trim(),
                cancellationToken);
            if (replay is not null)
                return Result<InternalCashMovementDto>.Ok(MapMovement(replay));
        }

        if (!Enum.TryParse<InternalCashDirection>(request.Direction, ignoreCase: true, out var direction)
            || !Enum.IsDefined(direction))
            return Result<InternalCashMovementDto>.Fail(
                "internal.direction",
                "Direction invalide. Utilisez VaultToTill, TillToVault ou TillToTill.");

        if (request.Amount is null)
            return Result<InternalCashMovementDto>.Fail("internal.amount", "Le montant est obligatoire.");
        var amount = MoneyAmount.Normalize(request.Amount.Value);
        if (amount <= 0m)
            return Result<InternalCashMovementDto>.Fail("internal.amount", "Le montant doit être supérieur à zéro.");

        var currency = NormalizeCurrency(request.CurrencyCode);
        if (currency is null)
            return Result<InternalCashMovementDto>.Fail("till.currency", "Devise non supportée.");

        if (request.Note is { Length: > 512 })
            return Result<InternalCashMovementDto>.Fail("internal.note", "La note ne peut pas dépasser 512 caractères.");

        var tenantId = _currentUser.TenantId!.Value;
        var userId = _currentUser.UserId!.Value;
        var branchId = _currentUser.BranchId!.Value;

        TillSession? source = null;
        TillSession? dest = null;

        if (direction is InternalCashDirection.TillToVault or InternalCashDirection.TillToTill)
        {
            if (request.SourceTillSessionId is null)
                return Result<InternalCashMovementDto>.Fail("internal.source", "La caisse source est obligatoire.");
            source = await _db.TillSessions.Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Id == request.SourceTillSessionId && t.TenantId == tenantId, cancellationToken);
            if (source is null)
                return Result<InternalCashMovementDto>.Fail("till.not_found", "Caisse source introuvable.");
            if (source.Status != TillSessionStatus.Open)
                return Result<InternalCashMovementDto>.Fail("till.not_open", "La caisse source doit être ouverte.");
            if (source.CurrencyCode != currency)
                return Result<InternalCashMovementDto>.Fail("internal.currency", "La devise de la caisse source ne correspond pas.");
            if (source.ExpectedCash < amount)
                return Result<InternalCashMovementDto>.Fail("internal.insufficient", "Solde de caisse source insuffisant.");
            if (!IsManager() && source.UserId != userId)
                return Result<InternalCashMovementDto>.Fail("auth.forbidden", "Vous ne pouvez envoyer que depuis votre propre caisse.");
        }
        else if (request.SourceTillSessionId is not null)
        {
            return Result<InternalCashMovementDto>.Fail("internal.source", "Coffre → Caisse n’a pas de caisse source.");
        }

        if (direction is InternalCashDirection.VaultToTill or InternalCashDirection.TillToTill)
        {
            if (request.DestinationTillSessionId is null)
                return Result<InternalCashMovementDto>.Fail("internal.dest", "La caisse destination est obligatoire.");
            dest = await _db.TillSessions.Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Id == request.DestinationTillSessionId && t.TenantId == tenantId, cancellationToken);
            if (dest is null)
                return Result<InternalCashMovementDto>.Fail("till.not_found", "Caisse destination introuvable.");
            if (dest.Status != TillSessionStatus.Open)
                return Result<InternalCashMovementDto>.Fail("till.not_open", "La caisse destination doit être ouverte.");
            if (dest.CurrencyCode != currency)
                return Result<InternalCashMovementDto>.Fail("internal.currency", "La devise de la caisse destination ne correspond pas.");
        }
        else if (request.DestinationTillSessionId is not null)
        {
            return Result<InternalCashMovementDto>.Fail("internal.dest", "Caisse → Coffre n’a pas de caisse destination.");
        }

        if (direction == InternalCashDirection.TillToTill)
        {
            if (source!.Id == dest!.Id || source.UserId == dest.UserId)
                return Result<InternalCashMovementDto>.Fail(
                    "internal.same_till",
                    "Un caissier ne peut pas envoyer vers sa propre caisse.");
        }

        var movement = new InternalCashMovement
        {
            TenantId = tenantId,
            BranchId = branchId,
            MovementNo = await NextInternalNoAsync(cancellationToken),
            Direction = direction,
            Status = InternalCashStatus.Pending,
            CurrencyCode = currency,
            Amount = amount,
            SourceTillSessionId = source?.Id,
            DestinationTillSessionId = dest?.Id,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedByUserId = userId,
            CreatedAtUtc = _clock.UtcNow,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        };
        _db.InternalCashMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Till.InternalCreated",
            nameof(InternalCashMovement),
            movement.Id,
            new { movement.MovementNo, movement.Direction, movement.Amount, movement.CurrencyCode },
            tenantId,
            userId,
            cancellationToken: cancellationToken);

        movement.SourceTillSession = source;
        movement.DestinationTillSession = dest;
        return Result<InternalCashMovementDto>.Ok(MapMovement(movement));
    }

    public async Task<Result<InternalCashMovementDto>> AcceptInternalMovementAsync(
        Guid movementId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<InternalCashMovementDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var movement = await QueryMovements()
            .FirstOrDefaultAsync(m => m.Id == movementId && m.TenantId == _currentUser.TenantId, cancellationToken);
        if (movement is null)
            return Result<InternalCashMovementDto>.Fail("internal.not_found", "Mouvement interne introuvable.");
        if (movement.Status != InternalCashStatus.Pending)
            return Result<InternalCashMovementDto>.Fail("internal.already_accepted", "Ce mouvement est déjà accepté.");

        if (!CanAccept(movement))
            return Result<InternalCashMovementDto>.Fail(
                "auth.forbidden",
                "Seul le gérant ou le caissier destinataire peut accepter la réception.");

        if (movement.Direction is InternalCashDirection.TillToVault or InternalCashDirection.TillToTill)
        {
            if (movement.SourceTillSession is null || movement.SourceTillSession.Status != TillSessionStatus.Open)
                return Result<InternalCashMovementDto>.Fail("till.not_open", "La caisse source n’est plus ouverte.");
            if (movement.SourceTillSession.ExpectedCash < movement.Amount)
                return Result<InternalCashMovementDto>.Fail("internal.insufficient", "Solde de caisse source insuffisant.");
        }

        if (movement.Direction is InternalCashDirection.VaultToTill or InternalCashDirection.TillToTill)
        {
            if (movement.DestinationTillSession is null || movement.DestinationTillSession.Status != TillSessionStatus.Open)
                return Result<InternalCashMovementDto>.Fail("till.not_open", "La caisse destination n’est plus ouverte.");
        }

        var tillGl = await RequireGlAsync(TreasuryGl.Till(movement.CurrencyCode), movement.CurrencyCode, cancellationToken);
        var vaultGl = await RequireGlAsync(TreasuryGl.Vault(movement.CurrencyCode), movement.CurrencyCode, cancellationToken);
        if (tillGl is null)
            return Result<InternalCashMovementDto>.Fail("internal.gl", "Compte de caisse introuvable.");
        if (vaultGl is null && movement.Direction != InternalCashDirection.TillToTill)
            return Result<InternalCashMovementDto>.Fail("internal.gl", "Compte de coffre introuvable.");

        List<CreateJournalLineRequest> lines;
        string description;
        if (movement.Direction == InternalCashDirection.VaultToTill)
        {
            var vaultBal = await GlBalanceAsync(vaultGl!.Id, cancellationToken);
            if (vaultBal < movement.Amount)
                return Result<InternalCashMovementDto>.Fail("internal.insufficient_vault", "Solde du coffre insuffisant.");
            description = $"Coffre → Caisse {movement.MovementNo}";
            lines =
            [
                new CreateJournalLineRequest { GlAccountId = tillGl.Id, Debit = movement.Amount, Credit = 0m, Description = "Caisse" },
                new CreateJournalLineRequest { GlAccountId = vaultGl.Id, Debit = 0m, Credit = movement.Amount, Description = "Coffre" }
            ];
        }
        else if (movement.Direction == InternalCashDirection.TillToVault)
        {
            description = $"Caisse → Coffre {movement.MovementNo}";
            lines =
            [
                new CreateJournalLineRequest { GlAccountId = vaultGl!.Id, Debit = movement.Amount, Credit = 0m, Description = "Coffre" },
                new CreateJournalLineRequest { GlAccountId = tillGl.Id, Debit = 0m, Credit = movement.Amount, Description = "Caisse" }
            ];
        }
        else
        {
            description = $"Caisse → Caisse {movement.MovementNo}";
            lines =
            [
                new CreateJournalLineRequest { GlAccountId = tillGl.Id, Debit = movement.Amount, Credit = 0m, Description = "Caisse B" },
                new CreateJournalLineRequest { GlAccountId = tillGl.Id, Debit = 0m, Credit = movement.Amount, Description = "Caisse A" }
            ];
        }

        var journal = await _journals.PostAsync(new CreateJournalRequest
        {
            Description = description,
            CurrencyCode = movement.CurrencyCode,
            BranchId = movement.BranchId,
            Lines = lines
        }, idempotencyKey, cancellationToken);
        if (!journal.IsSuccess)
            return Result<InternalCashMovementDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

        if (movement.SourceTillSession is not null)
            movement.SourceTillSession.ExpectedCash = MoneyAmount.Normalize(movement.SourceTillSession.ExpectedCash - movement.Amount);
        if (movement.DestinationTillSession is not null)
            movement.DestinationTillSession.ExpectedCash = MoneyAmount.Normalize(movement.DestinationTillSession.ExpectedCash + movement.Amount);

        movement.Status = InternalCashStatus.Accepted;
        movement.AcceptedByUserId = _currentUser.UserId;
        movement.AcceptedAtUtc = _clock.UtcNow;
        movement.JournalEntryId = journal.Value!.Id;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Till.InternalAccepted",
            nameof(InternalCashMovement),
            movement.Id,
            new { movement.MovementNo, movement.Amount, journal.Value.JournalNo },
            movement.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<InternalCashMovementDto>.Ok(MapMovement(movement));
    }

    public Task<Result<CashPostResultDto>> DepositAsync(
        CashPostRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        PostCashAsync(request, isDeposit: true, idempotencyKey, cancellationToken);

    public Task<Result<CashPostResultDto>> WithdrawAsync(
        CashPostRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        PostCashAsync(request, isDeposit: false, idempotencyKey, cancellationToken);

    private async Task<Result<CashPostResultDto>> PostCashAsync(
        CashPostRequest request,
        bool isDeposit,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<CashPostResultDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var amount = MoneyAmount.Normalize(request.Amount);
        if (amount <= 0m)
            return Result<CashPostResultDto>.Fail("teller.amount", "Le montant doit être supérieur à zéro.");

        var tenantId = _currentUser.TenantId!.Value;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await ReplayAsync(tenantId, idempotencyKey, cancellationToken);
            if (replay is not null)
                return Result<CashPostResultDto>.Ok(replay);
        }

        var account = await _db.SavingsAccounts
            .Include(a => a.Product)
            .Include(a => a.Member)
            .FirstOrDefaultAsync(
                a => a.Id == request.SavingsAccountId && a.TenantId == tenantId && a.IsActive,
                cancellationToken);
        if (account is null)
            return Result<CashPostResultDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");
        if (account.Product is null)
            return Result<CashPostResultDto>.Fail("savings.product.not_found", "Produit d’épargne introuvable.");
        if (account.IsBlocked)
            return Result<CashPostResultDto>.Fail(
                "savings.blocked",
                $"Compte bloqué : {account.BlockedReason ?? "gel administratif"}.");
        if (account.Member is not null && MembershipRules.ServicesBlocked(account.Member, _clock.UtcNow))
            return Result<CashPostResultDto>.Fail(
                "member.usager_expired",
                "Période d’usage échue : conversion en sociétaire requise avant tout mouvement de caisse.");

        var till = await FindOpenTillAsync(account.CurrencyCode, cancellationToken);
        if (till is null)
            return Result<CashPostResultDto>.Fail(
                "till.not_open",
                "Impossible de poster en caisse : aucune caisse ouverte pour cet utilisateur et cette agence.");

        var (ledger, available) = await ComputeBalancesAsync(account.Id, cancellationToken);
        if (!isDeposit && amount > available)
            return Result<CashPostResultDto>.Fail(
                "teller.insufficient",
                $"Solde disponible insuffisant ({MoneyDisplay.Format(available, account.CurrencyCode)}).");
        if (!isDeposit)
        {
            var locked = IsTermLocked(account);
            if (locked)
            {
                var note = request.GerantOverrideNote?.Trim();
                if (!IsManager() || string.IsNullOrWhiteSpace(note))
                    return Result<CashPostResultDto>.Fail(
                        "teller.terme_locked",
                        "Retrait avant échéance refusé. Le gérant peut outrepasser avec une note.");
                await _audit.LogAsync(
                    "Till.TermeOverride",
                    nameof(SavingsAccount),
                    account.Id,
                    new { account.AccountNo, amount, note },
                    tenantId,
                    _currentUser.UserId,
                    cancellationToken: cancellationToken);
            }
        }

        var cashGl = account.Product.CashGlAccountId;
        var liabilityGl = account.Product.LiabilityGlAccountId;
        var type = isDeposit ? "deposit" : "withdrawal";
        var title = isDeposit ? "Dépôt en espèces" : "Retrait en espèces";

        var journal = await _journals.PostAsync(new CreateJournalRequest
        {
            Description = $"{title} {account.AccountNo}",
            CurrencyCode = account.CurrencyCode,
            BranchId = till.BranchId,
            Lines = isDeposit
                ?
                [
                    new CreateJournalLineRequest { GlAccountId = cashGl, Debit = amount, Credit = 0m, Description = "Caisse" },
                    new CreateJournalLineRequest { GlAccountId = liabilityGl, Debit = 0m, Credit = amount, Description = "Épargne membre" }
                ]
                :
                [
                    new CreateJournalLineRequest { GlAccountId = liabilityGl, Debit = amount, Credit = 0m, Description = "Épargne membre" },
                    new CreateJournalLineRequest { GlAccountId = cashGl, Debit = 0m, Credit = amount, Description = "Caisse" }
                ]
        }, idempotencyKey, cancellationToken);

        if (!journal.IsSuccess)
            return Result<CashPostResultDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

        _db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
        {
            TenantId = tenantId,
            SavingsAccountId = account.Id,
            ValueDateUtc = DateTime.SpecifyKind(
                _clock.TodayInPortAuPrince().ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            PostedAtUtc = _clock.UtcNow,
            EntryType = isDeposit ? "Credit" : "Debit",
            Amount = amount,
            CurrencyCode = account.CurrencyCode,
            Description = title,
            JournalEntryId = journal.Value!.Id,
            TillSessionId = till.Id,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        });

        till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash + (isDeposit ? amount : -amount));
        await _db.SaveChangesAsync(cancellationToken);

        var (newLedger, newAvailable) = await ComputeBalancesAsync(account.Id, cancellationToken);
        var receipt = BuildReceipt(
            type,
            title,
            journal.Value,
            account,
            amount,
            newLedger,
            newAvailable);

        return Result<CashPostResultDto>.Ok(new CashPostResultDto(
            receipt,
            journal.Value.Id,
            journal.Value.Lines.Count,
            newLedger,
            newAvailable));
    }

    public async Task<Result<CashPostResultDto>> CollectMixedAsync(
        MixedCollectRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<CashPostResultDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var cash = MoneyAmount.Normalize(request.CashReceived);
        if (cash <= 0m)
            return Result<CashPostResultDto>.Fail("teller.amount", "Le montant reçu doit être supérieur à zéro.");
        if (request.Lines is null || request.Lines.Count == 0)
            return Result<CashPostResultDto>.Fail("teller.collect_lines", "Ajoutez au moins une ligne (épargne ou parts).");

        var tenantId = _currentUser.TenantId!.Value;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await ReplayAsync(tenantId, idempotencyKey, cancellationToken);
            if (replay is not null)
                return Result<CashPostResultDto>.Ok(replay);
        }

        var member = await _db.Members
            .Include(m => m.ShareAccounts)
            .FirstOrDefaultAsync(m => m.Id == request.MemberId && m.TenantId == tenantId, cancellationToken);
        if (member is null)
            return Result<CashPostResultDto>.Fail("member.not_found", "Membre introuvable.");
        if (MembershipRules.ServicesBlocked(member, _clock.UtcNow))
            return Result<CashPostResultDto>.Fail(
                "member.usager_expired",
                "Période d’usage échue : conversion en sociétaire requise avant tout mouvement de caisse.");

        var currency = NormalizeCurrency(request.CurrencyCode) ?? Currencies.Htg;
        var till = await FindOpenTillAsync(currency, cancellationToken);
        if (till is null)
            return Result<CashPostResultDto>.Fail(
                "till.not_open",
                "Impossible de poster en caisse : aucune caisse ouverte pour cet utilisateur et cette agence.");

        var lineSum = MoneyAmount.Normalize(request.Lines.Sum(l => MoneyAmount.Normalize(l.Amount)));
        if (lineSum != cash)
            return Result<CashPostResultDto>.Fail(
                "teller.collect_unbalanced",
                $"Répartissez le montant reçu (espèces) entre épargne et parts. Total des lignes {MoneyDisplay.Format(lineSum, currency)} ≠ {MoneyDisplay.Format(cash, currency)}.");

        var journalLines = new List<CreateJournalLineRequest>
        {
            new() { GlAccountId = CashGl(currency), Debit = cash, Credit = 0m, Description = "Caisse" }
        };
        var allocations = new List<ReceiptAllocationDto>();
        SavingsAccount? savingsForReceipt = null;
        decimal savingsPosted = 0m;
        var shareIncrements = new List<(ShareAccount Account, int Units, decimal Amount, string Label)>();

        foreach (var raw in request.Lines)
        {
            var amount = MoneyAmount.Normalize(raw.Amount);
            if (amount <= 0m)
                return Result<CashPostResultDto>.Fail("teller.amount", "Chaque ligne doit avoir un montant supérieur à zéro.");
            var kind = (raw.Kind ?? "").Trim();

            if (kind.Equals("Epargne", StringComparison.OrdinalIgnoreCase))
            {
                if (raw.SavingsAccountId is null || raw.SavingsAccountId == Guid.Empty)
                    return Result<CashPostResultDto>.Fail(
                        "savings.account.required",
                        "Ouvrez d’abord le compte d’épargne, ou enlevez la ligne.");
                var account = await _db.SavingsAccounts
                    .Include(a => a.Product)
                    .Include(a => a.Member)
                    .FirstOrDefaultAsync(
                        a => a.Id == raw.SavingsAccountId && a.TenantId == tenantId && a.MemberId == member.Id && a.IsActive,
                        cancellationToken);
                if (account is null)
                    return Result<CashPostResultDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");
                if (account.IsBlocked)
                    return Result<CashPostResultDto>.Fail("savings.blocked", $"Compte bloqué : {account.BlockedReason ?? "gel administratif"}.");
                if (account.Product is null)
                    return Result<CashPostResultDto>.Fail("savings.product.not_found", "Produit d’épargne introuvable.");
                journalLines.Add(new CreateJournalLineRequest
                {
                    GlAccountId = account.Product.LiabilityGlAccountId,
                    Debit = 0m,
                    Credit = amount,
                    Description = "Épargne membre"
                });
                allocations.Add(new ReceiptAllocationDto(
                    "Epargne",
                    account.AccountNo,
                    account.Product.DisplayName,
                    amount));
                savingsForReceipt = account;
                savingsPosted += amount;
                _db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
                {
                    TenantId = tenantId,
                    SavingsAccountId = account.Id,
                    ValueDateUtc = DateTime.SpecifyKind(
                        _clock.TodayInPortAuPrince().ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                    PostedAtUtc = _clock.UtcNow,
                    EntryType = "Credit",
                    Amount = amount,
                    CurrencyCode = account.CurrencyCode,
                    Description = "Encaissement mixte — épargne",
                    TillSessionId = till.Id,
                    IdempotencyKey = allocations.Count == 1 && !string.IsNullOrWhiteSpace(idempotencyKey)
                        ? idempotencyKey.Trim()
                        : null
                });
            }
            else if (kind.Equals("Qualification", StringComparison.OrdinalIgnoreCase)
                     || kind.Equals("Permanent", StringComparison.OrdinalIgnoreCase))
            {
                var shareType = kind.Equals("Permanent", StringComparison.OrdinalIgnoreCase)
                    ? ShareType.Permanent
                    : ShareType.Qualification;
                if (shareType == ShareType.Qualification && member.LegalStatus == LegalStatus.Usager)
                    return Result<CashPostResultDto>.Fail(
                        "savings.usager_parts",
                        "Un usager ne peut pas payer de parts de qualification hors conversion.");
                if (shareType == ShareType.Permanent && member.LegalStatus == LegalStatus.Usager)
                    return Result<CashPostResultDto>.Fail(
                        "savings.usager_permanent",
                        "Un usager ne peut pas payer de parts permanentes.");
                var share = member.ShareAccounts.FirstOrDefault(s => s.ShareType == shareType && s.IsActive);
                if (share is null)
                    return Result<CashPostResultDto>.Fail(
                        "savings.account.required",
                        shareType == ShareType.Qualification
                            ? "Ouvrez d’abord le compte de parts de qualification."
                            : "Ouvrez d’abord le compte de parts permanentes.");
                var par = MembershipRules.NormalizeParValue(share.ParValue);
                if (par <= 0m || amount % par != 0m)
                    return Result<CashPostResultDto>.Fail(
                        "teller.share_par",
                        $"Le montant des parts doit être un multiple de la valeur nominale ({MoneyDisplay.Format(par, Currencies.Htg)}).");
                var units = (int)(amount / par);
                if (units < 1)
                    return Result<CashPostResultDto>.Fail("teller.share_units", "Payez au moins une part.");
                var capitalCode = shareType == ShareType.Qualification
                    ? MembershipRules.QualificationCapitalGl
                    : MembershipRules.PermanentCapitalGl;
                var capital = await _db.GlAccounts.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Code == capitalCode, cancellationToken);
                if (capital is null)
                    return Result<CashPostResultDto>.Fail("member.gl", "Compte de capital introuvable.");
                journalLines.Add(new CreateJournalLineRequest
                {
                    GlAccountId = capital.Id,
                    Debit = 0m,
                    Credit = amount,
                    Description = shareType == ShareType.Qualification ? "Parts de qualification" : "Parts permanentes"
                });
                allocations.Add(new ReceiptAllocationDto(
                    shareType.ToString(),
                    share.AccountNo,
                    shareType == ShareType.Qualification ? "Parts de qualification" : "Parts permanentes",
                    amount));
                shareIncrements.Add((share, units, amount, allocations[^1].Label));
            }
            else
            {
                return Result<CashPostResultDto>.Fail("teller.collect_kind", "Ligne invalide (Épargne, Parts qualification ou Parts permanentes).");
            }
        }

        var journal = await _journals.PostAsync(new CreateJournalRequest
        {
            Description = $"Encaissement mixte {member.MemberNo}",
            CurrencyCode = currency,
            BranchId = till.BranchId,
            Lines = journalLines
        }, idempotencyKey, cancellationToken);
        if (!journal.IsSuccess)
            return Result<CashPostResultDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

        foreach (var entry in _db.ChangeTracker.Entries<SavingsLedgerEntry>().Where(e => e.State == EntityState.Added))
            entry.Entity.JournalEntryId = journal.Value!.Id;
        foreach (var (share, units, _, _) in shareIncrements)
            share.ShareCount += units;

        till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash + cash);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Till.MixedCollect",
            nameof(TillSession),
            till.Id,
            new { member.MemberNo, cash, lines = allocations.Count, journal.Value!.JournalNo },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        decimal ledger = 0m, available = 0m;
        if (savingsForReceipt is not null)
            (ledger, available) = await ComputeBalancesAsync(savingsForReceipt.Id, cancellationToken);

        var receiptAccount = savingsForReceipt;
        var receipt = new CashReceiptDto(
            new ReceiptLetterheadDto(Letterhead.Sigle, Letterhead.Line2, Letterhead.Line3, Letterhead.Line4),
            "collect",
            "Encaissement mixte",
            journal.Value!.JournalNo,
            journal.Value.JournalNo,
            member.MemberNo,
            member.FullName,
            receiptAccount?.AccountNo ?? allocations.FirstOrDefault()?.AccountNo ?? "",
            "Répartition espèces",
            cash,
            currency,
            ledger,
            available,
            _currentUser.Username ?? string.Empty,
            Letterhead.DefaultBranchName,
            journal.Value.PostedAtUtc,
            journal.Value.PostedAtPortAuPrince,
            allocations);

        return Result<CashPostResultDto>.Ok(new CashPostResultDto(
            receipt,
            journal.Value.Id,
            journal.Value.Lines.Count,
            ledger,
            available));
    }

    private bool IsTermLocked(SavingsAccount account)
    {
        if (account.AllowWithdrawBeforeTerm)
            return false;
        if (account.MaturesOn is { } matures)
            return _clock.TodayInPortAuPrince() < matures;
        return account.Product is { IsTermLike: true } && account.Product.TermDays is > 0
               && _clock.TodayInPortAuPrince() < DateOnly.FromDateTime(_clock.ToPortAuPrince(account.OpenedAtUtc))
                   .AddDays(account.Product.TermDays.Value);
    }

    private async Task<CashPostResultDto?> ReplayAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var entry = await _db.SavingsLedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.IdempotencyKey == key, cancellationToken);
        if (entry is null)
            return null;

        var account = await _db.SavingsAccounts.AsNoTracking()
            .Include(a => a.Product)
            .Include(a => a.Member)
            .FirstAsync(a => a.Id == entry.SavingsAccountId, cancellationToken);
        var journal = await _journals.GetAsync(entry.JournalEntryId!.Value, cancellationToken);
        if (!journal.IsSuccess)
            return null;

        var (ledger, available) = await ComputeBalancesAsync(account.Id, cancellationToken);
        var isDeposit = entry.EntryType == "Credit";
        var receipt = BuildReceipt(
            isDeposit ? "deposit" : "withdrawal",
            isDeposit ? "Dépôt en espèces" : "Retrait en espèces",
            journal.Value!,
            account,
            entry.Amount,
            ledger,
            available);
        return new CashPostResultDto(receipt, journal.Value!.Id, journal.Value.Lines.Count, ledger, available);
    }

    private async Task<(decimal Ledger, decimal Available)> ComputeBalancesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var entries = await _db.SavingsLedgerEntries.AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId)
            .ToListAsync(cancellationToken);
        var ledger = MoneyAmount.Normalize(entries.Sum(x => x.SignedAmount));
        var liens = await _db.SavingsLiens.AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId && x.ReleasedAtUtc == null)
            .Select(x => x.Amount)
            .ToListAsync(cancellationToken);
        return (ledger, MoneyAmount.Normalize(ledger - liens.Sum()));
    }

    private Task<TillSession?> FindOpenTillAsync(string currency, CancellationToken cancellationToken) =>
        _db.TillSessions.FirstOrDefaultAsync(
            t => t.TenantId == _currentUser.TenantId
                 && t.UserId == _currentUser.UserId
                 && t.BranchId == _currentUser.BranchId
                 && t.CurrencyCode == currency
                 && t.Status == TillSessionStatus.Open,
            cancellationToken);

    private CashReceiptDto BuildReceipt(
        string type,
        string title,
        JournalDto journal,
        SavingsAccount account,
        decimal amount,
        decimal ledger,
        decimal available) =>
        new(
            new ReceiptLetterheadDto(Letterhead.Sigle, Letterhead.Line2, Letterhead.Line3, Letterhead.Line4),
            type,
            title,
            journal.JournalNo,
            journal.JournalNo,
            account.Member?.MemberNo ?? string.Empty,
            account.Member?.FullName ?? string.Empty,
            account.AccountNo,
            account.Product?.DisplayName ?? account.Product?.Name ?? string.Empty,
            amount,
            account.CurrencyCode,
            ledger,
            available,
            _currentUser.Username ?? string.Empty,
            account.Member?.Branch?.Name ?? Letterhead.DefaultBranchName,
            journal.PostedAtUtc,
            journal.PostedAtPortAuPrince);

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

    private static string? NormalizeCurrency(string? code) =>
        Currencies.IsSupported(code ?? string.Empty) ? code!.Trim().ToUpperInvariant() : null;

    private static Guid CashGl(string currency) =>
        string.Equals(currency, Currencies.Usd, StringComparison.OrdinalIgnoreCase)
            ? SeedGuids.Gl("1020")
            : SeedGuids.Gl("1010");

    private IQueryable<InternalCashMovement> QueryMovements() =>
        _db.InternalCashMovements
            .Include(m => m.SourceTillSession)!.ThenInclude(t => t!.User)
            .Include(m => m.DestinationTillSession)!.ThenInclude(t => t!.User);

    private InternalCashMovementDto MapMovement(InternalCashMovement m) =>
        new(
            m.Id,
            m.MovementNo,
            m.Direction.ToString(),
            m.Status.ToString(),
            m.CurrencyCode,
            m.Amount,
            m.SourceTillSessionId,
            m.SourceTillSession?.User?.FullName,
            m.DestinationTillSessionId,
            m.DestinationTillSession?.User?.FullName,
            m.Note,
            m.CreatedAtUtc,
            m.AcceptedAtUtc,
            m.JournalEntryId,
            CanAccept(m));

    private bool CanAccept(InternalCashMovement movement)
    {
        if (movement.Status != InternalCashStatus.Pending)
            return false;
        if (IsManager())
            return true;
        return movement.DestinationTillSessionId is not null
               && movement.DestinationTillSession?.UserId == _currentUser.UserId;
    }

    private bool IsManager() =>
        _currentUser.Roles.Any(r => r is RoleNames.Admin or RoleNames.Gerant);

    private async Task<string> NextInternalNoAsync(CancellationToken cancellationToken)
    {
        var sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == _currentUser.TenantId && s.Key == "InternalCashMovementNo", cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence
            {
                Id = Guid.NewGuid(),
                TenantId = _currentUser.TenantId!.Value,
                Key = "InternalCashMovementNo",
                LastValue = 0
            };
            _db.NumberSequences.Add(sequence);
        }

        sequence.LastValue += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return $"MI-{sequence.LastValue:000000}";
    }

    private Task<GlAccount?> RequireGlAsync(string code, string currency, CancellationToken cancellationToken) =>
        _db.GlAccounts.FirstOrDefaultAsync(
            a => a.TenantId == _currentUser.TenantId && a.Code == code && a.CurrencyCode == currency && a.IsPostable,
            cancellationToken);

    private async Task<decimal> GlBalanceAsync(Guid glAccountId, CancellationToken cancellationToken)
    {
        var nets = await _db.JournalLines.AsNoTracking()
            .Where(l => l.GlAccountId == glAccountId)
            .Select(l => l.Debit - l.Credit)
            .ToListAsync(cancellationToken);
        return MoneyAmount.Normalize(nets.Sum());
    }

    private static TillSessionDto MapTill(TillSession till) =>
        new(
            till.Id,
            till.BranchId,
            till.UserId,
            till.CurrencyCode,
            till.Status.ToString(),
            till.OpeningFloat,
            till.ExpectedCash,
            till.CountedCash,
            till.OverShortAmount,
            till.OverShortJournalId,
            till.OpenedAtUtc,
            till.ClosedAtUtc,
            till.Notes);
}
