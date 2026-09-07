using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Reports;
using CPCREDO.Application.Treasury;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Teller;
using CPCREDO.Domain.Treasury;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Reports;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Treasury;

public sealed class TreasuryService : ITreasuryService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;
    private readonly IJournalService _journals;
    private readonly PasswordHasher<User> _hasher = new();
    private static readonly string[] AllowedSlipTypes = ["image/jpeg", "image/png", "application/pdf"];
    private const int MaxSlipBytes = 5 * 1024 * 1024;

    public TreasuryService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogger audit,
        IJournalService journals)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _journals = journals;
    }

    public Task<Result<IReadOnlyList<string>>> ListBankNamesAsync(CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Task.FromResult(Result<IReadOnlyList<string>>.Fail(auth.ErrorCode!, auth.ErrorMessage!));
        return Task.FromResult(Result<IReadOnlyList<string>>.Ok(CorrespondentBanks.Names));
    }

    public async Task<Result<IReadOnlyList<BankAccountDto>>> ListBanksAsync(CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<BankAccountDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var query = _db.BankAccounts.AsNoTracking().Where(b => b.TenantId == _currentUser.TenantId);
        if (!CanManageBanks())
            query = query.Where(b => b.IsActive);

        var banks = await query
            .OrderBy(b => b.Bank)
            .ThenBy(b => b.Name)
            .ThenBy(b => b.CurrencyCode)
            .ToListAsync(cancellationToken);

        var list = new List<BankAccountDto>();
        foreach (var bank in banks)
        {
            var balance = await GlBalanceAsync(bank.GlCode, bank.CurrencyCode, cancellationToken);
            list.Add(MapBank(bank, balance, revealNumber: CanManageBanks()));
        }

        return Result<IReadOnlyList<BankAccountDto>>.Ok(list);
    }

    public async Task<Result<BankAccountDto>> CreateBankAsync(
        SaveBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireBankManager();
        if (!gate.IsSuccess)
            return Result<BankAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var parsed = await ParseBankRequestAsync(request, excludeId: null, cancellationToken);
        if (!parsed.IsSuccess)
            return Result<BankAccountDto>.Fail(parsed.ErrorCode!, parsed.ErrorMessage!);

        var bank = parsed.Value!;
        bank.Id = Guid.NewGuid();
        bank.TenantId = _currentUser.TenantId!.Value;
        bank.CreatedAtUtc = _clock.UtcNow;
        _db.BankAccounts.Add(bank);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Treasury.BankCreated", nameof(BankAccount), bank.Id, new { bank.Bank, bank.Number, bank.CurrencyCode }, bank.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<BankAccountDto>.Ok(await MapBankAsync(bank, cancellationToken));
    }

    public async Task<Result<BankAccountDto>> UpdateBankAsync(
        Guid id,
        SaveBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireBankManager();
        if (!gate.IsSuccess)
            return Result<BankAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var bank = await _db.BankAccounts.FirstOrDefaultAsync(
            b => b.Id == id && b.TenantId == _currentUser.TenantId, cancellationToken);
        if (bank is null)
            return Result<BankAccountDto>.Fail("treasury.bank_not_found", "Compte correspondant introuvable.");

        var parsed = await ParseBankRequestAsync(request, excludeId: id, cancellationToken);
        if (!parsed.IsSuccess)
            return Result<BankAccountDto>.Fail(parsed.ErrorCode!, parsed.ErrorMessage!);

        var next = parsed.Value!;
        bank.Bank = next.Bank;
        bank.CustomBankName = next.CustomBankName;
        bank.Name = next.Name;
        bank.Number = next.Number;
        bank.CurrencyCode = next.CurrencyCode;
        bank.GlCode = next.GlCode;
        bank.Notes = next.Notes;
        bank.IsActive = next.IsActive;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Treasury.BankUpdated", nameof(BankAccount), bank.Id, new { bank.Bank, bank.Number, bank.IsActive }, bank.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<BankAccountDto>.Ok(await MapBankAsync(bank, cancellationToken));
    }

    public async Task<Result<BankAccountDto>> DeactivateBankAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var gate = RequireBankManager();
        if (!gate.IsSuccess)
            return Result<BankAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var bank = await _db.BankAccounts.FirstOrDefaultAsync(
            b => b.Id == id && b.TenantId == _currentUser.TenantId, cancellationToken);
        if (bank is null)
            return Result<BankAccountDto>.Fail("treasury.bank_not_found", "Compte correspondant introuvable.");

        bank.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Treasury.BankDeactivated", nameof(BankAccount), bank.Id, new { bank.Number }, bank.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<BankAccountDto>.Ok(await MapBankAsync(bank, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<VaultBalanceDto>>> ListVaultsAsync(CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<VaultBalanceDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var codes = new[] { TreasuryGl.VaultHtg, TreasuryGl.VaultUsd };
        var accounts = await _db.GlAccounts.AsNoTracking()
            .Where(a => a.TenantId == _currentUser.TenantId && codes.Contains(a.Code))
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        var list = new List<VaultBalanceDto>();
        foreach (var account in accounts)
        {
            var balance = await GlBalanceAsync(account.Code, account.CurrencyCode, cancellationToken);
            list.Add(new VaultBalanceDto(account.Code, account.NameFr, account.CurrencyCode, balance));
        }

        return Result<IReadOnlyList<VaultBalanceDto>>.Ok(list);
    }

    public async Task<Result<IReadOnlyList<TreasuryTransferDto>>> ListTransfersAsync(CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<TreasuryTransferDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var transfers = await _db.TreasuryTransfers
            .AsNoTracking()
            .Include(t => t.BankAccount)
            .Where(t => t.TenantId == _currentUser.TenantId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<TreasuryTransferDto>>.Ok(await MapManyAsync(transfers, cancellationToken));
    }

    public async Task<Result<TreasuryTransferDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TreasuryTransferDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var transfer = await LoadAsync(id, cancellationToken);
        if (transfer is null)
            return Result<TreasuryTransferDto>.Fail("treasury.not_found", "Mouvement de trésorerie introuvable.");
        return Result<TreasuryTransferDto>.Ok(await MapAsync(transfer, cancellationToken));
    }

    public async Task<Result<TreasurySlipFileDto>> GetSlipAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TreasurySlipFileDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var transfer = await LoadAsync(id, cancellationToken);
        if (transfer is null)
            return Result<TreasurySlipFileDto>.Fail("treasury.not_found", "Mouvement de trésorerie introuvable.");
        if (transfer.SlipContent is null || transfer.SlipContent.Length == 0)
            return Result<TreasurySlipFileDto>.Fail("treasury.slip_not_found", "Aucun bordereau joint.");
        return Result<TreasurySlipFileDto>.Ok(new TreasurySlipFileDto(
            transfer.SlipContent,
            transfer.SlipContentType ?? "application/octet-stream",
            transfer.SlipFileName ?? $"{transfer.TransferNo}-bordereau"));
    }

    public async Task<Result<TreasuryTransferDto>> CreateDraftAsync(
        CreateTreasuryTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireUser();
        if (!gate.IsSuccess)
            return Result<TreasuryTransferDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        if (!Enum.TryParse<TreasuryDirection>(request.Direction, true, out var direction))
            return Result<TreasuryTransferDto>.Fail("treasury.direction", "Direction invalide. Utilisez TillToVault, VaultToTill, VaultToBank ou BankToVault.");

        if (TreasuryDirections.InvolvesBank(direction))
        {
            if (!IsGerant() && !IsAdmin())
                return Result<TreasuryTransferDto>.Fail("auth.forbidden", "Seul un gérant ou un administrateur peut initier un mouvement coffre ↔ banque.");
        }
        else if (!CanCreateTillDraft())
        {
            return Result<TreasuryTransferDto>.Fail("auth.forbidden", "Seul un caissier ou un gérant peut créer un mouvement caisse ↔ coffre.");
        }

        var amount = MoneyAmount.Normalize(request.Amount);
        var fee = MoneyAmount.Normalize(request.FeeAmount);
        if (amount <= 0m)
            return Result<TreasuryTransferDto>.Fail("treasury.amount", "Le montant doit être saisi et supérieur à zéro.");
        if (fee < 0m)
            return Result<TreasuryTransferDto>.Fail("treasury.fee", "Les frais ne peuvent pas être négatifs.");

        BankAccount? bank = null;
        string currency;
        currency = (request.CurrencyCode ?? Currencies.Htg).Trim().ToUpperInvariant();
        if (!Currencies.IsSupported(currency))
            return Result<TreasuryTransferDto>.Fail("treasury.currency", "Devise non supportée. Utilisez HTG ou USD.");

        if (TreasuryDirections.InvolvesBank(direction))
        {
            if (request.BankAccountId is null || request.BankAccountId == Guid.Empty)
                return Result<TreasuryTransferDto>.Fail("treasury.bank_required", "Un compte correspondant est obligatoire pour un mouvement coffre ↔ banque.");
            bank = await _db.BankAccounts.FirstOrDefaultAsync(
                b => b.Id == request.BankAccountId && b.TenantId == _currentUser.TenantId && b.IsActive,
                cancellationToken);
            if (bank is null)
                return Result<TreasuryTransferDto>.Fail("treasury.bank_not_found", "Compte correspondant introuvable ou inactif.");
            if (!string.Equals(bank.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase))
                return Result<TreasuryTransferDto>.Fail("treasury.currency_mismatch", "Le compte correspondant n’est pas dans la devise du mouvement.");
        }

        var transfer = new TreasuryTransfer
        {
            TenantId = _currentUser.TenantId!.Value,
            BranchId = _currentUser.BranchId!.Value,
            TransferNo = await NextNumberAsync(cancellationToken),
            Direction = direction,
            BankAccountId = bank?.Id,
            Amount = amount,
            FeeAmount = TreasuryDirections.InvolvesBank(direction) ? fee : 0m,
            CurrencyCode = currency,
            Status = TreasuryTransferStatus.Draft,
            CreatedByUserId = _currentUser.UserId!.Value,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAtUtc = _clock.UtcNow,
            BankAccount = bank
        };
        _db.TreasuryTransfers.Add(transfer);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Treasury.DraftCreated", nameof(TreasuryTransfer), transfer.Id, new { transfer.TransferNo, transfer.Direction, transfer.Amount }, transfer.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<TreasuryTransferDto>.Ok(await MapAsync(transfer, cancellationToken));
    }

    public async Task<Result<TreasuryTransferDto>> AttachSlipAsync(
        Guid id,
        AttachTreasurySlipRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TreasuryTransferDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        if (!IsGerant() && !IsAdmin())
            return Result<TreasuryTransferDto>.Fail("auth.forbidden", "Seul un gérant ou un administrateur peut joindre un bordereau.");

        var transfer = await LoadAsync(id, cancellationToken);
        if (transfer is null)
            return Result<TreasuryTransferDto>.Fail("treasury.not_found", "Mouvement de trésorerie introuvable.");
        if (!TreasuryDirections.InvolvesBank(transfer.Direction))
            return Result<TreasuryTransferDto>.Fail("treasury.invalid_status", "Le bordereau s’applique aux mouvements coffre ↔ banque.");
        if (transfer.Status != TreasuryTransferStatus.Draft)
            return Result<TreasuryTransferDto>.Fail("treasury.invalid_status", "Le bordereau ne peut être joint que sur un brouillon.");

        var check = ValidateSlip(request.SlipContent, request.SlipContentType, request.SlipFileName, request.SlipRef, request.SlipType);
        if (!check.IsSuccess)
            return Result<TreasuryTransferDto>.Fail(check.ErrorCode!, check.ErrorMessage!);

        transfer.BankSlipRef = request.SlipRef.Trim();
        transfer.SlipType = Enum.Parse<TreasurySlipType>(string.IsNullOrWhiteSpace(request.SlipType) ? "DepositSlip" : request.SlipType, true);
        transfer.SlipFileName = request.SlipFileName!.Trim();
        transfer.SlipContentType = NormalizeSlipContentType(request.SlipContentType);
        transfer.SlipContent = request.SlipContent;
        transfer.SlipUploadedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Treasury.SlipAttached", nameof(TreasuryTransfer), transfer.Id, new { transfer.TransferNo, transfer.BankSlipRef }, transfer.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<TreasuryTransferDto>.Ok(await MapAsync(transfer, cancellationToken));
    }

    public async Task<Result<TreasuryTransferDto>> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TreasuryTransferDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var transfer = await LoadAsync(id, cancellationToken);
        if (transfer is null)
            return Result<TreasuryTransferDto>.Fail("treasury.not_found", "Mouvement de trésorerie introuvable.");
        if (TreasuryDirections.InvolvesBank(transfer.Direction))
            return Result<TreasuryTransferDto>.Fail("treasury.invalid_status", "Les mouvements coffre ↔ banque s’exécutent sans double validation.");

        var userId = _currentUser.UserId!.Value;
        if (transfer.Status == TreasuryTransferStatus.Draft)
        {
            if (!IsGerant())
                return Result<TreasuryTransferDto>.Fail("auth.forbidden", "Seul un gérant peut donner l’approbation 1.");
            transfer.Status = TreasuryTransferStatus.Approved1;
            transfer.Approver1Id = userId;
            transfer.Approved1AtUtc = _clock.UtcNow;
        }
        else if (transfer.Status == TreasuryTransferStatus.Approved1)
        {
            if (!IsGerant() && !IsAdmin())
                return Result<TreasuryTransferDto>.Fail("auth.forbidden", "L’approbation 2 est réservée à un administrateur ou à un second gérant.");
            if (transfer.Approver1Id == userId)
                return Result<TreasuryTransferDto>.Fail("treasury.same_approver", "Le même utilisateur ne peut pas être les deux approbateurs.");
            transfer.Status = TreasuryTransferStatus.Approved2;
            transfer.Approver2Id = userId;
            transfer.Approved2AtUtc = _clock.UtcNow;
        }
        else
        {
            return Result<TreasuryTransferDto>.Fail("treasury.invalid_status", "Ce mouvement ne peut pas être approuvé dans son état actuel.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Treasury.Approved", nameof(TreasuryTransfer), transfer.Id, new { transfer.TransferNo, transfer.Status }, transfer.TenantId, userId, cancellationToken: cancellationToken);
        return Result<TreasuryTransferDto>.Ok(await MapAsync(transfer, cancellationToken));
    }

    public async Task<Result<TreasuryTransferDto>> ExecuteAsync(
        Guid id,
        ExecuteTreasuryTransferRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TreasuryTransferDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        if (!IsGerant() && !IsAdmin())
            return Result<TreasuryTransferDto>.Fail("auth.forbidden", "Seul un gérant ou un administrateur peut exécuter un mouvement de trésorerie.");

        var transfer = await LoadAsync(id, cancellationToken);
        if (transfer is null)
            return Result<TreasuryTransferDto>.Fail("treasury.not_found", "Mouvement de trésorerie introuvable.");
        if (transfer.Status == TreasuryTransferStatus.Executed)
            return Result<TreasuryTransferDto>.Ok(await MapAsync(transfer, cancellationToken));

        var involvesBank = TreasuryDirections.InvolvesBank(transfer.Direction);
        if (involvesBank)
        {
            if (transfer.Status != TreasuryTransferStatus.Draft)
                return Result<TreasuryTransferDto>.Fail("treasury.invalid_status", "Seul un brouillon coffre ↔ banque peut être exécuté.");
            if (transfer.CreatedByUserId == _currentUser.UserId)
                return Result<TreasuryTransferDto>.Fail("treasury.self_execute", "L’initiateur ne peut pas exécuter son propre mouvement.");
            var verified = await VerifyExecutorPasswordAsync(request.Password, cancellationToken);
            if (!verified.IsSuccess)
                return Result<TreasuryTransferDto>.Fail(verified.ErrorCode!, verified.ErrorMessage!);
            if (transfer.SlipContent is null || transfer.SlipContent.Length == 0 || string.IsNullOrWhiteSpace(transfer.BankSlipRef))
                return Result<TreasuryTransferDto>.Fail("treasury.slip_required", "Joignez le bordereau avant d’exécuter.");
        }
        else
        {
            if (transfer.Status is TreasuryTransferStatus.Draft or TreasuryTransferStatus.Approved1)
                return Result<TreasuryTransferDto>.Fail("treasury.dual_approval_required", "Les deux approbations sont obligatoires avant l’exécution.");
            if (transfer.Status != TreasuryTransferStatus.Approved2)
                return Result<TreasuryTransferDto>.Fail("treasury.invalid_status", "Seul un mouvement en Approuvé 2 peut être exécuté.");
            if (transfer.Approver1Id is null || transfer.Approver2Id is null || transfer.Approver1Id == transfer.Approver2Id)
                return Result<TreasuryTransferDto>.Fail("treasury.dual_approval_required", "Les deux approbations distinctes sont obligatoires.");
        }

        var posted = await PostTransferJournalAsync(transfer, transfer.BankSlipRef, idempotencyKey, cancellationToken);
        if (!posted.IsSuccess)
            return Result<TreasuryTransferDto>.Fail(posted.ErrorCode!, posted.ErrorMessage!);

        transfer.Status = TreasuryTransferStatus.Executed;
        transfer.ExecutedById = _currentUser.UserId;
        transfer.ExecutedAtUtc = _clock.UtcNow;
        transfer.PostedJournalId = posted.Value!.Id;
        if (!string.IsNullOrWhiteSpace(request.Notes))
            transfer.Notes = request.Notes.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Treasury.Executed", nameof(TreasuryTransfer), transfer.Id, new { transfer.TransferNo, posted.Value.JournalNo }, transfer.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<TreasuryTransferDto>.Ok(await MapAsync(transfer, cancellationToken));
    }

    public async Task<Result<TreasuryTransferDto>> CancelAsync(
        Guid id,
        CancelTreasuryTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TreasuryTransferDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var transfer = await LoadAsync(id, cancellationToken);
        if (transfer is null)
            return Result<TreasuryTransferDto>.Fail("treasury.not_found", "Mouvement de trésorerie introuvable.");
        if (transfer.Status is TreasuryTransferStatus.Executed or TreasuryTransferStatus.Cancelled)
            return Result<TreasuryTransferDto>.Fail("treasury.invalid_status", "Ce mouvement ne peut pas être annulé.");

        var isInitiator = transfer.CreatedByUserId == _currentUser.UserId && transfer.Status == TreasuryTransferStatus.Draft;
        if (!isInitiator && !IsAdmin())
            return Result<TreasuryTransferDto>.Fail("auth.forbidden", "Annulation réservée à l’initiateur ou à l’administrateur.");

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return Result<TreasuryTransferDto>.Fail("treasury.cancel_reason", "Le motif d’annulation est obligatoire.");

        transfer.Status = TreasuryTransferStatus.Cancelled;
        transfer.CancelReason = reason;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Treasury.Cancelled", nameof(TreasuryTransfer), transfer.Id, new { transfer.TransferNo, reason }, transfer.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<TreasuryTransferDto>.Ok(await MapAsync(transfer, cancellationToken));
    }

    public async Task<Result<ReportFileDto>> ExportJournalAsync(
        DateOnly? from,
        DateOnly? to,
        string format,
        CancellationToken cancellationToken = default)
    {
        var list = await ListTransfersAsync(cancellationToken);
        if (!list.IsSuccess)
            return Result<ReportFileDto>.Fail(list.ErrorCode!, list.ErrorMessage!);

        var end = to ?? _clock.TodayInPortAuPrince();
        var start = from ?? end.AddMonths(-1);
        var executed = list.Value!
            .Where(t => t.Status == nameof(TreasuryTransferStatus.Executed) && t.ExecutedAtUtc is { } at
                && DateOnly.FromDateTime(_clock.ToPortAuPrince(at)) >= start
                && DateOnly.FromDateTime(_clock.ToPortAuPrince(at)) <= end)
            .ToList();

        var headers = new[] { "Date", "N°", "Direction", "Banque", "Montant", "Frais", "Bordereau", "Approbateur 1", "Approbateur 2" };
        var rows = executed.Select(t => (IReadOnlyList<string>)
        [
            DateOnly.FromDateTime(_clock.ToPortAuPrince(t.ExecutedAtUtc!.Value)).ToString("yyyy-MM-dd"),
            t.TransferNo,
            Enum.TryParse<TreasuryDirection>(t.Direction, out var dir) ? TreasuryDirections.LabelFr(dir) : t.Direction,
            t.BankName,
            MoneyDisplay.Format(t.Amount, t.CurrencyCode),
            MoneyDisplay.Format(t.FeeAmount, t.CurrencyCode),
            t.BankSlipRef ?? "",
            t.Approver1Name ?? "",
            t.Approver2Name ?? ""
        ]).ToList();

        var kind = (format ?? "pdf").Trim().ToLowerInvariant();
        if (kind is "csv" or "text/csv")
        {
            return Result<ReportFileDto>.Ok(new ReportFileDto(
                ReportCsv.Render(headers, rows),
                "text/csv; charset=utf-8",
                $"journal-tresorerie-{start:yyyy-MM-dd}-{end:yyyy-MM-dd}.csv"));
        }

        return Result<ReportFileDto>.Ok(new ReportFileDto(
            ReportPdf.Render(
                "Journal de trésorerie",
                $"Du {start:yyyy-MM-dd} au {end:yyyy-MM-dd}",
                headers,
                rows),
            "application/pdf",
            $"journal-tresorerie-{start:yyyy-MM-dd}-{end:yyyy-MM-dd}.pdf"));
    }

    private async Task<Result<BankAccount>> ParseBankRequestAsync(
        SaveBankAccountRequest request,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        if (!CorrespondentBanks.IsCatalogName(request.BankName))
            return Result<BankAccount>.Fail("treasury.bank_name", "Choisissez une banque du catalogue, ou Autre.");

        var bankName = CorrespondentBanks.CanonicalName(request.BankName);
        string? custom = string.IsNullOrWhiteSpace(request.CustomBankName) ? null : request.CustomBankName.Trim();
        if (CorrespondentBanks.IsAutre(bankName))
        {
            if (string.IsNullOrWhiteSpace(custom))
                return Result<BankAccount>.Fail("treasury.custom_bank", "Le nom de la banque est obligatoire lorsque vous choisissez Autre.");
        }
        else
        {
            custom = null;
        }

        var number = request.AccountNumber?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(number))
            return Result<BankAccount>.Fail("treasury.account_number", "Le numéro de compte est obligatoire.");

        var currency = (request.CurrencyCode ?? Currencies.Htg).Trim().ToUpperInvariant();
        if (!Currencies.IsSupported(currency))
            return Result<BankAccount>.Fail("treasury.currency", "Devise non supportée. Utilisez HTG ou USD.");

        var duplicate = await _db.BankAccounts.AnyAsync(
            b => b.TenantId == _currentUser.TenantId
                 && b.Number == number
                 && (excludeId == null || b.Id != excludeId),
            cancellationToken);
        if (duplicate)
            return Result<BankAccount>.Fail("treasury.account_number_taken", "Ce numéro de compte existe déjà.");

        var glCode = string.IsNullOrWhiteSpace(request.GlCode)
            ? (currency == Currencies.Usd ? TreasuryGl.DefaultBankUsd : TreasuryGl.DefaultBankHtg)
            : request.GlCode.Trim();
        var gl = await RequireGlAsync(glCode, currency, cancellationToken);
        if (gl is null)
            return Result<BankAccount>.Fail("treasury.gl", "Compte GL introuvable pour cette devise.");

        var label = string.IsNullOrWhiteSpace(request.Label) ? "" : request.Label.Trim();
        return Result<BankAccount>.Ok(new BankAccount
        {
            Bank = bankName,
            CustomBankName = custom,
            Name = label,
            Number = number,
            CurrencyCode = currency,
            GlCode = glCode,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            IsActive = request.IsActive
        });
    }

    private async Task<Result<JournalDto>> PostTransferJournalAsync(
        TreasuryTransfer transfer,
        string? slip,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var vaultCode = TreasuryGl.Vault(transfer.CurrencyCode);
        var vault = await RequireGlAsync(vaultCode, transfer.CurrencyCode, cancellationToken);
        if (vault is null)
            return Result<JournalDto>.Fail("treasury.gl", "Compte de coffre introuvable.");

        var vaultBal = await GlBalanceAsync(vaultCode, transfer.CurrencyCode, cancellationToken);
        var lines = new List<CreateJournalLineRequest>();
        string counterpart;

        if (TreasuryDirections.InvolvesTill(transfer.Direction))
        {
            var tillCode = TreasuryGl.Till(transfer.CurrencyCode);
            var till = await RequireGlAsync(tillCode, transfer.CurrencyCode, cancellationToken);
            if (till is null)
                return Result<JournalDto>.Fail("treasury.gl", "Compte de caisse introuvable.");
            var tillBal = await GlBalanceAsync(tillCode, transfer.CurrencyCode, cancellationToken);
            if (transfer.Direction == TreasuryDirection.TillToVault && tillBal < transfer.Amount)
                return Result<JournalDto>.Fail("treasury.insufficient_till", "Solde de caisse insuffisant.");
            if (transfer.Direction == TreasuryDirection.VaultToTill && vaultBal < transfer.Amount)
                return Result<JournalDto>.Fail("treasury.insufficient_vault", "Solde du coffre insuffisant.");

            if (transfer.Direction == TreasuryDirection.TillToVault)
            {
                lines.Add(new CreateJournalLineRequest { GlAccountId = vault.Id, Debit = transfer.Amount, Credit = 0m, Description = "Coffre" });
                lines.Add(new CreateJournalLineRequest { GlAccountId = till.Id, Debit = 0m, Credit = transfer.Amount, Description = "Caisse" });
            }
            else
            {
                lines.Add(new CreateJournalLineRequest { GlAccountId = till.Id, Debit = transfer.Amount, Credit = 0m, Description = "Caisse" });
                lines.Add(new CreateJournalLineRequest { GlAccountId = vault.Id, Debit = 0m, Credit = transfer.Amount, Description = "Coffre" });
            }

            counterpart = "caisse";
        }
        else
        {
            var bank = transfer.BankAccount
                ?? await _db.BankAccounts.FirstOrDefaultAsync(b => b.Id == transfer.BankAccountId, cancellationToken);
            if (bank is null)
                return Result<JournalDto>.Fail("treasury.bank_not_found", "Compte correspondant introuvable.");
            var bankGl = await RequireGlAsync(bank.GlCode, transfer.CurrencyCode, cancellationToken);
            if (bankGl is null)
                return Result<JournalDto>.Fail("treasury.gl", "Compte de banque introuvable.");
            var bankBal = await GlBalanceAsync(bank.GlCode, transfer.CurrencyCode, cancellationToken);
            if (transfer.Direction == TreasuryDirection.VaultToBank && vaultBal < transfer.Amount)
                return Result<JournalDto>.Fail("treasury.insufficient_vault", "Solde du coffre insuffisant.");
            if (transfer.Direction == TreasuryDirection.BankToVault && bankBal < transfer.Amount + transfer.FeeAmount)
                return Result<JournalDto>.Fail("treasury.insufficient_bank", "Solde bancaire insuffisant.");

            if (transfer.Direction == TreasuryDirection.VaultToBank)
            {
                lines.Add(new CreateJournalLineRequest { GlAccountId = bankGl.Id, Debit = transfer.Amount, Credit = 0m, Description = "Banque" });
                lines.Add(new CreateJournalLineRequest { GlAccountId = vault.Id, Debit = 0m, Credit = transfer.Amount, Description = "Coffre" });
            }
            else
            {
                lines.Add(new CreateJournalLineRequest { GlAccountId = vault.Id, Debit = transfer.Amount, Credit = 0m, Description = "Coffre" });
                lines.Add(new CreateJournalLineRequest { GlAccountId = bankGl.Id, Debit = 0m, Credit = transfer.Amount, Description = "Banque" });
            }

            if (transfer.FeeAmount > 0m)
            {
                var feeGl = await RequireGlAsync(TreasuryGl.BankFee(transfer.CurrencyCode), transfer.CurrencyCode, cancellationToken);
                if (feeGl is null)
                    return Result<JournalDto>.Fail("treasury.gl", "Compte de frais bancaires introuvable.");
                lines.Add(new CreateJournalLineRequest { GlAccountId = feeGl.Id, Debit = transfer.FeeAmount, Credit = 0m, Description = "Frais bancaires" });
                lines.Add(new CreateJournalLineRequest { GlAccountId = bankGl.Id, Debit = 0m, Credit = transfer.FeeAmount, Description = "Frais bancaires" });
            }

            counterpart = bank.DisplayBankName;
        }

        return await _journals.PostAsync(new CreateJournalRequest
        {
            Description = $"Trésorerie {transfer.TransferNo} {TreasuryDirections.LabelFr(transfer.Direction)} {counterpart}",
            CurrencyCode = transfer.CurrencyCode,
            BranchId = transfer.BranchId,
            Lines = lines
        }, idempotencyKey, cancellationToken);
    }

    private Task<TreasuryTransfer?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        _db.TreasuryTransfers
            .Include(t => t.BankAccount)
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == _currentUser.TenantId, cancellationToken);

    private async Task<decimal> GlBalanceAsync(string code, string currency, CancellationToken cancellationToken)
    {
        var nets = await (
            from line in _db.JournalLines
            join journal in _db.JournalEntries on line.JournalEntryId equals journal.Id
            join account in _db.GlAccounts on line.GlAccountId equals account.Id
            where journal.TenantId == _currentUser.TenantId
                  && journal.Status == JournalStatus.Posted
                  && account.Code == code
                  && account.CurrencyCode == currency
            select line.Debit - line.Credit).ToListAsync(cancellationToken);
        return MoneyAmount.Normalize(nets.Sum());
    }

    private Task<GlAccount?> RequireGlAsync(string code, string currency, CancellationToken cancellationToken) =>
        _db.GlAccounts.AsNoTracking().FirstOrDefaultAsync(
            a => a.TenantId == _currentUser.TenantId && a.Code == code && a.CurrencyCode == currency && a.IsPostable,
            cancellationToken);

    private async Task<string> NextNumberAsync(CancellationToken cancellationToken)
    {
        var sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == _currentUser.TenantId && s.Key == "TreasuryTransferNo", cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence
            {
                Id = Guid.NewGuid(),
                TenantId = _currentUser.TenantId!.Value,
                Key = "TreasuryTransferNo",
                LastValue = 0
            };
            _db.NumberSequences.Add(sequence);
        }

        sequence.LastValue += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return $"TR-{sequence.LastValue:000000}";
    }

    private async Task<IReadOnlyList<TreasuryTransferDto>> MapManyAsync(
        IReadOnlyList<TreasuryTransfer> transfers,
        CancellationToken cancellationToken)
    {
        var ids = transfers
            .SelectMany(t => new[] { t.CreatedByUserId, t.Approver1Id, t.Approver2Id, t.ExecutedById })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var roles = await _db.UserRoles.AsNoTracking()
            .Include(ur => ur.Role)
            .Where(ur => ids.Contains(ur.UserId))
            .ToListAsync(cancellationToken);
        var roleByUser = roles
            .GroupBy(ur => ur.UserId)
            .ToDictionary(g => g.Key, g => g.First().Role?.Name);
        return transfers.Select(t => Map(t, names, roleByUser)).ToList();
    }

    private async Task<TreasuryTransferDto> MapAsync(TreasuryTransfer transfer, CancellationToken cancellationToken)
    {
        var mapped = await MapManyAsync([transfer], cancellationToken);
        return mapped[0];
    }

    private static TreasuryTransferDto Map(
        TreasuryTransfer t,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, string?> roles)
    {
        string? RoleOf(Guid? id) => id is { } key && roles.TryGetValue(key, out var role) ? role : null;
        var bank = t.BankAccount;
        return new(
            t.Id,
            t.TransferNo,
            t.Direction.ToString(),
            t.BankAccountId,
            bank?.DisplayBankName ?? string.Empty,
            bank?.Bank ?? string.Empty,
            bank?.PickerLabel ?? string.Empty,
            bank?.MaskedNumber ?? string.Empty,
            t.Amount,
            t.FeeAmount,
            t.CurrencyCode,
            t.Status.ToString(),
            t.CreatedByUserId,
            names.GetValueOrDefault(t.CreatedByUserId, string.Empty),
            RoleOf(t.CreatedByUserId),
            t.CreatedByUserId,
            names.GetValueOrDefault(t.CreatedByUserId, string.Empty),
            RoleOf(t.CreatedByUserId),
            t.Approver1Id,
            t.Approver1Id is { } a1 ? names.GetValueOrDefault(a1) : null,
            RoleOf(t.Approver1Id),
            t.Approved1AtUtc,
            t.Approver2Id,
            t.Approver2Id is { } a2 ? names.GetValueOrDefault(a2) : null,
            RoleOf(t.Approver2Id),
            t.Approved2AtUtc,
            t.ExecutedById,
            t.ExecutedById is { } ex ? names.GetValueOrDefault(ex) : null,
            RoleOf(t.ExecutedById),
            t.BankSlipRef,
            t.SlipType?.ToString(),
            t.SlipFileName,
            t.SlipContentType,
            t.SlipContent is { Length: > 0 },
            t.Notes,
            t.CancelReason,
            t.PostedJournalId,
            t.CreatedAtUtc,
            t.ExecutedAtUtc);
    }

    private async Task<BankAccountDto> MapBankAsync(BankAccount bank, CancellationToken cancellationToken)
    {
        var balance = await GlBalanceAsync(bank.GlCode, bank.CurrencyCode, cancellationToken);
        return MapBank(bank, balance, revealNumber: true);
    }

    private static BankAccountDto MapBank(BankAccount bank, decimal balance, bool revealNumber) =>
        new(
            bank.Id,
            bank.Bank,
            bank.CustomBankName,
            bank.DisplayBankName,
            string.IsNullOrWhiteSpace(bank.Name) ? null : bank.Name,
            revealNumber ? bank.Number : "",
            bank.MaskedNumber,
            bank.PickerLabel,
            bank.CurrencyCode,
            bank.GlCode,
            balance,
            bank.IsActive,
            bank.Notes);

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null || _currentUser.BranchId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireBankManager()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!CanManageBanks())
            return Result<bool>.Fail("auth.forbidden", "Seuls l’administrateur et le gérant gèrent les comptes correspondants.");
        return Result<bool>.Ok(true);
    }

    private bool IsAdmin() => _currentUser.Roles.Contains(RoleNames.Admin);
    private bool IsGerant() => _currentUser.Roles.Contains(RoleNames.Gerant);
    private bool IsCaissierOnly() =>
        _currentUser.Roles.Contains(RoleNames.Caissier) && !IsGerant() && !IsAdmin();
    private bool CanManageBanks() => IsAdmin() || IsGerant();
    private bool CanCreateTillDraft() =>
        IsGerant() || (_currentUser.Roles.Contains(RoleNames.Caissier) && !IsAdmin());

    private async Task<Result<bool>> VerifyExecutorPasswordAsync(string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(password))
            return Result<bool>.Fail("treasury.password_invalid", "Le mot de passe du vérificateur est obligatoire.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, cancellationToken);
        if (user is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed)
            return Result<bool>.Fail("treasury.password_invalid", "Mot de passe du vérificateur incorrect.");
        return Result<bool>.Ok(true);
    }

    private static Result<bool> ValidateSlip(
        byte[]? content,
        string? contentType,
        string? fileName,
        string? slipRef,
        string? slipType)
    {
        if (content is null || content.Length == 0)
            return Result<bool>.Fail("treasury.slip_required", "Le bordereau scanné (jpg, png ou pdf) est obligatoire.");
        if (content.Length > MaxSlipBytes)
            return Result<bool>.Fail("treasury.slip_too_large", "Le fichier du bordereau dépasse 5 Mo.");
        var type = string.IsNullOrWhiteSpace(slipType) ? nameof(TreasurySlipType.DepositSlip) : slipType;
        if (!Enum.TryParse<TreasurySlipType>(type, true, out _))
            return Result<bool>.Fail("treasury.slip_type", "Type de bordereau invalide. Utilisez DepositSlip ou WithdrawalSlip.");
        if (string.IsNullOrWhiteSpace(slipRef))
            return Result<bool>.Fail("treasury.slip_required", "La référence du bordereau est obligatoire.");
        var normalized = NormalizeSlipContentType(contentType);
        if (!AllowedSlipTypes.Contains(normalized))
            return Result<bool>.Fail("treasury.slip_type", "Le bordereau doit être un fichier jpg, png ou pdf.");
        if (string.IsNullOrWhiteSpace(fileName))
            return Result<bool>.Fail("treasury.slip_required", "Le nom du fichier bordereau est obligatoire.");
        return Result<bool>.Ok(true);
    }

    private static string NormalizeSlipContentType(string? contentType)
    {
        var value = (contentType ?? "").Trim().ToLowerInvariant();
        if (value is "image/jpg")
            return "image/jpeg";
        return value;
    }
}
