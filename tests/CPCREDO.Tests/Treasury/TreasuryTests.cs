using System.Text;
using CPCREDO.Application.Accounting;
using CPCREDO.Application.Treasury;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Teller;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Domain.Treasury;
using CPCREDO.Infrastructure.Accounting;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Treasury;
using CPCREDO.Tests.Accounting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Treasury;

public sealed class TreasuryTests
{
    [Fact]
    public async Task Caissier_cannot_create_bank_draft()
    {
        using var h = new TreasuryHarness();
        h.AsCaissier();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(10_000m));
        Assert.False(created.IsSuccess);
        Assert.Equal("auth.forbidden", created.ErrorCode);
    }

    [Fact]
    public async Task Caissier_creates_till_draft_and_cannot_approve()
    {
        using var h = new TreasuryHarness();
        h.AsCaissier();

        var created = await h.Treasury.CreateDraftAsync(h.Draft(10_000m, direction: "TillToVault"));
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal(nameof(TreasuryTransferStatus.Draft), created.Value!.Status);
        Assert.Null(created.Value.PostedJournalId);

        var approved = await h.Treasury.ApproveAsync(created.Value.Id);
        Assert.False(approved.IsSuccess);
        Assert.Equal("auth.forbidden", approved.ErrorCode);
        Assert.Equal(1, await h.Db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Same_gerant_cannot_be_both_approvers()
    {
        using var h = new TreasuryHarness();
        h.AsCaissier();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(5_000m, direction: "TillToVault"));
        Assert.True(created.IsSuccess, created.ErrorMessage);

        h.AsGerant();
        var first = await h.Treasury.ApproveAsync(created.Value!.Id);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal(nameof(TreasuryTransferStatus.Approved1), first.Value!.Status);

        var second = await h.Treasury.ApproveAsync(created.Value.Id);
        Assert.False(second.IsSuccess);
        Assert.Equal("treasury.same_approver", second.ErrorCode);
    }

    [Fact]
    public async Task Execute_without_dual_approval_is_rejected()
    {
        using var h = new TreasuryHarness();
        h.AsCaissier();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(5_000m, direction: "TillToVault"));
        Assert.True(created.IsSuccess, created.ErrorMessage);

        h.AsGerant();
        var skipped = await h.Treasury.ExecuteAsync(created.Value!.Id, new ExecuteTreasuryTransferRequest(), "skip-1");
        Assert.False(skipped.IsSuccess);
        Assert.Equal("treasury.dual_approval_required", skipped.ErrorCode);

        var first = await h.Treasury.ApproveAsync(created.Value.Id);
        Assert.True(first.IsSuccess, first.ErrorMessage);

        var still = await h.Treasury.ExecuteAsync(created.Value.Id, new ExecuteTreasuryTransferRequest(), "skip-2");
        Assert.False(still.IsSuccess);
        Assert.Equal("treasury.dual_approval_required", still.ErrorCode);
        Assert.Equal(1, await h.Db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task BankToVault_posts_dr_vault_cr_bank_and_fee()
    {
        using var h = new TreasuryHarness();
        var executed = await h.ExecuteAsync("BankToVault", 10_000m, 50m, "BRH-SLIP-1");

        Assert.Equal(nameof(TreasuryTransferStatus.Executed), executed.Status);
        Assert.NotNull(executed.PostedJournalId);
        Assert.Equal("BRH-SLIP-1", executed.BankSlipRef);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == executed.PostedJournalId);
        Assert.Equal(4, journal.Lines.Count);
        Assert.Equal(10_050m, journal.Lines.Sum(l => l.Debit));
        Assert.Equal(10_050m, journal.Lines.Sum(l => l.Credit));
        Assert.Equal(10_000m, Debit(journal, TreasuryGl.VaultHtg));
        Assert.Equal(10_050m, Credit(journal, TreasuryGl.DefaultBankHtg));
        Assert.Equal(50m, Debit(journal, TreasuryGl.BankFeeHtg));

        var vault = await h.Treasury.ListVaultsAsync();
        var banks = await h.Treasury.ListBanksAsync();
        Assert.Equal(10_000m, vault.Value!.Single(v => v.GlCode == TreasuryGl.VaultHtg).Balance);
        Assert.Equal(89_950m, banks.Value!.Single(b => b.Id == SeedGuids.BankBrhHtg).Balance);
    }

    [Fact]
    public async Task VaultToBank_posts_dr_bank_cr_vault()
    {
        using var h = new TreasuryHarness();
        await h.ExecuteAsync("BankToVault", 10_000m, 0m, "IN-1");
        var executed = await h.ExecuteAsync("VaultToBank", 2_000m, 0m, "OUT-1");

        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == executed.PostedJournalId);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(2_000m, Debit(journal, TreasuryGl.DefaultBankHtg));
        Assert.Equal(2_000m, Credit(journal, TreasuryGl.VaultHtg));
    }

    [Fact]
    public async Task Journal_pdf_uses_letterhead_and_two_decimals()
    {
        using var h = new TreasuryHarness();
        await h.ExecuteAsync("BankToVault", 10_000m, 0m, "BRH-SLIP-PDF");

        var pdf = await h.Treasury.ExportJournalAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 4), "pdf");

        Assert.True(pdf.IsSuccess, pdf.ErrorMessage);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf.Value!.Content.AsSpan(0, 4)));
        Assert.StartsWith("journal-tresorerie-", pdf.Value.FileName);
        Assert.EndsWith(".pdf", pdf.Value.FileName);
        Assert.Contains("CPCREDO", Encoding.Latin1.GetString(pdf.Value.Content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commissaire_cannot_create_draft()
    {
        using var h = new TreasuryHarness();
        h.AsCommissaire();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(1_000m));
        Assert.False(created.IsSuccess);
        Assert.Equal("auth.forbidden", created.ErrorCode);
    }

    [Fact]
    public async Task Admin_can_create_bank_draft()
    {
        using var h = new TreasuryHarness();
        h.AsAdmin();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(1_000m));
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal(nameof(TreasuryTransferStatus.Draft), created.Value!.Status);
        Assert.Null(created.Value.PostedJournalId);
        Assert.Equal(1000m, created.Value.Amount);
    }

    [Fact]
    public async Task Bank_execute_requires_slip_and_rejects_initiator()
    {
        using var h = new TreasuryHarness();
        h.AsGerant();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(1_000m));
        Assert.True(created.IsSuccess, created.ErrorMessage);

        var self = await h.Treasury.ExecuteAsync(created.Value!.Id, new ExecuteTreasuryTransferRequest { Password = TreasuryHarness.VerifierPassword }, "self-exec");
        Assert.Equal("treasury.self_execute", self.ErrorCode);

        h.AsAdmin();
        var noSlip = await h.Treasury.ExecuteAsync(created.Value.Id, new ExecuteTreasuryTransferRequest { Password = TreasuryHarness.VerifierPassword }, "no-slip");
        Assert.Equal("treasury.slip_required", noSlip.ErrorCode);

        var attached = await h.Treasury.AttachSlipAsync(created.Value.Id, h.BankSlip("X"));
        Assert.True(attached.IsSuccess, attached.ErrorMessage);

        var badPassword = await h.Treasury.ExecuteAsync(created.Value.Id, new ExecuteTreasuryTransferRequest { Password = "wrong" }, "bad-pw");
        Assert.Equal("treasury.password_invalid", badPassword.ErrorCode);
        Assert.Equal(1, await h.Db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Cancel_requires_reason_and_does_not_post()
    {
        using var h = new TreasuryHarness();
        h.AsCaissier();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(1_000m, direction: "TillToVault"));

        var missing = await h.Treasury.CancelAsync(created.Value!.Id, new CancelTreasuryTransferRequest());
        Assert.Equal("treasury.cancel_reason", missing.ErrorCode);

        var cancelled = await h.Treasury.CancelAsync(created.Value.Id, new CancelTreasuryTransferRequest { Reason = "Erreur de saisie" });
        Assert.True(cancelled.IsSuccess, cancelled.ErrorMessage);
        Assert.Equal(nameof(TreasuryTransferStatus.Cancelled), cancelled.Value!.Status);
        Assert.Equal("Erreur de saisie", cancelled.Value.CancelReason);
        Assert.Equal(1, await h.Db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Inactive_bank_cannot_be_selected()
    {
        using var h = new TreasuryHarness();
        h.AsGerant();
        var deactivated = await h.Treasury.DeactivateBankAsync(SeedGuids.BankBrhHtg);
        Assert.True(deactivated.IsSuccess, deactivated.ErrorMessage);
        Assert.False(deactivated.Value!.IsActive);

        h.AsGerant();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(1_000m));
        Assert.Equal("treasury.bank_not_found", created.ErrorCode);
    }

    [Fact]
    public async Task Autre_bank_requires_custom_name_and_account_number()
    {
        using var h = new TreasuryHarness();
        h.AsGerant();
        var missingCustom = await h.Treasury.CreateBankAsync(new SaveBankAccountRequest
        {
            BankName = "Autre",
            AccountNumber = "99-111",
            CurrencyCode = "HTG"
        });
        Assert.Equal("treasury.custom_bank", missingCustom.ErrorCode);

        var created = await h.Treasury.CreateBankAsync(new SaveBankAccountRequest
        {
            BankName = "Autre",
            CustomBankName = "Banque de la République d’Haïti",
            AccountNumber = "BRH-441122",
            CurrencyCode = "HTG",
            Label = "Compte opération"
        });
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal("Banque de la République d’Haïti", created.Value!.DisplayBankName);
        Assert.Equal("****1122", created.Value.AccountNumberMasked);
        Assert.Contains("Compte opération", created.Value.PickerLabel);
    }

    [Fact]
    public async Task Caissier_cannot_manage_banks()
    {
        using var h = new TreasuryHarness();
        h.AsCaissier();
        var created = await h.Treasury.CreateBankAsync(new SaveBankAccountRequest
        {
            BankName = "Sogebank",
            AccountNumber = "SG-1",
            CurrencyCode = "HTG"
        });
        Assert.Equal("auth.forbidden", created.ErrorCode);
    }

    [Fact]
    public async Task Bank_catalog_includes_federations_and_currency_must_match()
    {
        using var h = new TreasuryHarness();
        h.AsGerant();
        var names = await h.Treasury.ListBankNamesAsync();
        Assert.Contains("Citibank", names.Value!);
        Assert.Contains("Fédération / Le Levier", names.Value!);
        Assert.Contains("Fédération / Le Sociétaire", names.Value!);

        var mismatch = await h.Treasury.CreateDraftAsync(new CreateTreasuryTransferRequest
        {
            Direction = "BankToVault",
            BankAccountId = SeedGuids.BankBrhHtg,
            Amount = 1_000m,
            CurrencyCode = Currencies.Usd
        });
        Assert.Equal("treasury.currency_mismatch", mismatch.ErrorCode);
    }

    [Fact]
    public async Task TillToVault_posts_dr_vault_cr_till_without_bank()
    {
        using var h = new TreasuryHarness();
        var executed = await h.ExecuteAsync("TillToVault", 10_000m, 0m, slip: null);

        Assert.Equal(nameof(TreasuryTransferStatus.Executed), executed.Status);
        Assert.Null(executed.BankAccountId);
        Assert.Equal(RoleNames.Gerant, executed.Approver1Role);
        Assert.Equal(RoleNames.Admin, executed.Approver2Role);
        Assert.NotNull(executed.Approved1AtUtc);
        Assert.NotNull(executed.Approved2AtUtc);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == executed.PostedJournalId);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(10_000m, Debit(journal, TreasuryGl.VaultHtg));
        Assert.Equal(10_000m, Credit(journal, TreasuryGl.TillHtg));
    }

    [Fact]
    public async Task VaultToTill_posts_dr_till_cr_vault()
    {
        using var h = new TreasuryHarness();
        await h.ExecuteAsync("TillToVault", 10_000m, 0m, slip: null);
        var executed = await h.ExecuteAsync("VaultToTill", 2_000m, 0m, slip: null);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == executed.PostedJournalId);
        Assert.Equal(2_000m, Debit(journal, TreasuryGl.TillHtg));
        Assert.Equal(2_000m, Credit(journal, TreasuryGl.VaultHtg));
    }

    [Fact]
    public async Task Bank_direction_requires_bank_account()
    {
        using var h = new TreasuryHarness();
        h.AsGerant();
        var created = await h.Treasury.CreateDraftAsync(new CreateTreasuryTransferRequest
        {
            Direction = "VaultToBank",
            Amount = 1_000m,
            CurrencyCode = Currencies.Htg
        });
        Assert.Equal("treasury.bank_required", created.ErrorCode);
    }

    [Fact]
    public async Task Till_execute_does_not_require_slip()
    {
        using var h = new TreasuryHarness();
        h.AsCaissier();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(1_000m, direction: "TillToVault"));
        h.AsGerant();
        Assert.True((await h.Treasury.ApproveAsync(created.Value!.Id)).IsSuccess);
        h.AsAdmin();
        Assert.True((await h.Treasury.ApproveAsync(created.Value.Id)).IsSuccess);
        var executed = await h.Treasury.ExecuteAsync(created.Value.Id, new ExecuteTreasuryTransferRequest(), "till-no-slip");
        Assert.True(executed.IsSuccess, executed.ErrorMessage);
        Assert.Null(executed.Value!.BankSlipRef);
    }

    [Fact]
    public async Task Caissier_with_open_till_can_create_till_to_vault()
    {
        using var h = new TreasuryHarness();
        h.OpenTillForCaissier();
        h.AsCaissier();
        var created = await h.Treasury.CreateDraftAsync(h.Draft(500m, direction: "TillToVault"));
        Assert.True(created.IsSuccess, created.ErrorMessage);
    }

    private static decimal Debit(JournalEntry journal, string code) =>
        journal.Lines.Where(l => l.GlAccountId == SeedGuids.Gl(code)).Sum(l => l.Debit);

    private static decimal Credit(JournalEntry journal, string code) =>
        journal.Lines.Where(l => l.GlAccountId == SeedGuids.Gl(code)).Sum(l => l.Credit);
}

internal sealed class TreasuryHarness : IDisposable
{
    public const string VerifierPassword = "Verifier@123";
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly byte[] TinyPdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer\n%%EOF"u8.ToArray();

    public CpcredoDbContext Db { get; }
    public TreasuryService Treasury { get; }
    public TestCurrentUser User { get; }

    public TreasuryHarness()
    {
        var options = new DbContextOptionsBuilder<CpcredoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new CpcredoDbContext(options);
        Db.Database.EnsureCreated();

        var now = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc);
        Db.Tenants.Add(new Tenant
        {
            Id = SeedGuids.TenantId,
            Sigle = Letterhead.Sigle,
            LegalName = Letterhead.LegalName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            CreatedAtUtc = now
        });
        Db.Branches.Add(new Branch
        {
            Id = SeedGuids.BranchId,
            TenantId = SeedGuids.TenantId,
            Code = Letterhead.DefaultBranchCode,
            Name = Letterhead.DefaultBranchName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            IsHeadquarters = true,
            CreatedAtUtc = now
        });
        Db.Users.AddRange(
            UserRow(SeedGuids.AdminUserId, "admin", "Administrateur CPCREDO"),
            UserRow(SeedGuids.GerantUserId, "gerant", "Gérant CPCREDO"),
            UserRow(Guid.Parse("0c0ec0de-0001-4000-a000-000000000013"), "gerant2", "Second gérant"),
            UserRow(SeedGuids.CaissierUserId, "caissier", "Caissier CPCREDO"));
        Db.Roles.AddRange(
            RoleRow(SeedGuids.RoleAdmin, RoleNames.Admin, "Administrateur"),
            RoleRow(SeedGuids.RoleGerant, RoleNames.Gerant, "Gérant"),
            RoleRow(SeedGuids.RoleCaissier, RoleNames.Caissier, "Caissier"));
        Db.UserRoles.AddRange(
            new UserRole { UserId = SeedGuids.AdminUserId, RoleId = SeedGuids.RoleAdmin },
            new UserRole { UserId = SeedGuids.GerantUserId, RoleId = SeedGuids.RoleGerant },
            new UserRole { UserId = SeedGuids.CaissierUserId, RoleId = SeedGuids.RoleCaissier });
        Db.GlAccounts.AddRange(
            Gl(TreasuryGl.TillHtg, "Caisse HTG", GlAccountType.Asset, NormalBalance.Debit),
            Gl(TreasuryGl.VaultHtg, "Coffre HTG", GlAccountType.Asset, NormalBalance.Debit),
            Gl(TreasuryGl.DefaultBankHtg, "Banque HTG", GlAccountType.Asset, NormalBalance.Debit),
            Gl(TreasuryGl.BankFeeHtg, "Frais bancaires HTG", GlAccountType.Expense, NormalBalance.Debit),
            Gl("3010", "Parts", GlAccountType.Equity, NormalBalance.Credit));
        Db.BankAccounts.Add(new BankAccount
        {
            Id = SeedGuids.BankBrhHtg,
            TenantId = SeedGuids.TenantId,
            Name = "Compte opération",
            Bank = "Unibank",
            Number = "001-998877-21",
            CurrencyCode = Currencies.Htg,
            GlCode = TreasuryGl.DefaultBankHtg,
            IsActive = true,
            CreatedAtUtc = now
        });
        Db.SaveChanges();

        User = new TestCurrentUser();
        var clock = new FixedClock { UtcNow = new DateTime(2026, 9, 4, 16, 0, 0, DateTimeKind.Utc) };
        var audit = new AuditLogger(Db, clock, User);
        var journals = new JournalService(Db, User, clock, audit);
        Treasury = new TreasuryService(Db, User, clock, audit, journals);

        var opening = journals.PostAsync(new CreateJournalRequest
        {
            Description = "Ouverture banque test",
            CurrencyCode = Currencies.Htg,
            Lines =
            [
                new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl(TreasuryGl.DefaultBankHtg), Debit = 100_000m, Credit = 0m, Description = "Banque" },
                new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl(TreasuryGl.TillHtg), Debit = 50_000m, Credit = 0m, Description = "Caisse" },
                new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("3010"), Debit = 0m, Credit = 150_000m, Description = "Capital" }
            ]
        }, "open-bank").GetAwaiter().GetResult();
        if (!opening.IsSuccess)
            throw new InvalidOperationException(opening.ErrorMessage);
    }

    public CreateTreasuryTransferRequest Draft(decimal amount, decimal fee = 0m, string direction = "BankToVault") =>
        new()
        {
            Direction = direction,
            BankAccountId = TreasuryDirections.InvolvesBank(Enum.Parse<TreasuryDirection>(direction, true))
                ? SeedGuids.BankBrhHtg
                : null,
            Amount = amount,
            FeeAmount = fee,
            CurrencyCode = Currencies.Htg
        };

    public AttachTreasurySlipRequest BankSlip(string slipRef) =>
        new()
        {
            SlipRef = slipRef,
            SlipType = nameof(TreasurySlipType.DepositSlip),
            SlipContent = TinyPdf,
            SlipFileName = "bordereau.pdf",
            SlipContentType = "application/pdf"
        };

    public async Task<TreasuryTransferDto> ExecuteAsync(string direction, decimal amount, decimal fee, string? slip)
    {
        var involvesBank = TreasuryDirections.InvolvesBank(Enum.Parse<TreasuryDirection>(direction, true));
        if (involvesBank)
            AsGerant();
        else
            AsCaissier();

        var created = await Treasury.CreateDraftAsync(Draft(amount, fee, direction));
        Assert.True(created.IsSuccess, created.ErrorMessage);

        if (involvesBank)
        {
            AsAdmin();
            var attached = await Treasury.AttachSlipAsync(created.Value!.Id, BankSlip(slip ?? "SLIP"));
            Assert.True(attached.IsSuccess, attached.ErrorMessage);
            var executedBank = await Treasury.ExecuteAsync(
                created.Value.Id,
                new ExecuteTreasuryTransferRequest { Password = VerifierPassword },
                $"exec-{direction}-{amount}");
            Assert.True(executedBank.IsSuccess, executedBank.ErrorMessage);
            return executedBank.Value!;
        }

        AsGerant();
        var first = await Treasury.ApproveAsync(created.Value!.Id);
        Assert.True(first.IsSuccess, first.ErrorMessage);

        AsAdmin();
        var second = await Treasury.ApproveAsync(created.Value.Id);
        Assert.True(second.IsSuccess, second.ErrorMessage);

        var executed = await Treasury.ExecuteAsync(
            created.Value.Id,
            new ExecuteTreasuryTransferRequest(),
            $"exec-{direction}-{amount}");
        Assert.True(executed.IsSuccess, executed.ErrorMessage);
        return executed.Value!;
    }

    public void OpenTillForCaissier()
    {
        Db.TillSessions.Add(new TillSession
        {
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            UserId = SeedGuids.CaissierUserId,
            CurrencyCode = Currencies.Htg,
            Status = TillSessionStatus.Open,
            OpeningFloat = 0m,
            ExpectedCash = 0m,
            OpenedAtUtc = DateTime.UtcNow
        });
        Db.SaveChanges();
    }

    public void AsAdmin()
    {
        User.UserId = SeedGuids.AdminUserId;
        User.Username = "admin";
        User.Roles = [RoleNames.Admin];
    }

    public void AsGerant()
    {
        User.UserId = SeedGuids.GerantUserId;
        User.Username = "gerant";
        User.Roles = [RoleNames.Gerant];
    }

    public void AsCaissier()
    {
        User.UserId = SeedGuids.CaissierUserId;
        User.Username = "caissier";
        User.Roles = [RoleNames.Caissier];
    }

    public void AsCommissaire()
    {
        User.UserId = SeedGuids.AdminUserId;
        User.Username = "commissaire";
        User.Roles = [RoleNames.Commissaire];
    }

    private static Role RoleRow(Guid id, string name, string fr) =>
        new()
        {
            Id = id,
            Name = name,
            DisplayNameFr = fr,
            DisplayNameHt = fr,
            DisplayNameEn = fr,
            CreatedAtUtc = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc)
        };

    private static User UserRow(Guid id, string username, string name) =>
        new()
        {
            Id = id,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            Username = username,
            Email = $"{username}@cpcredo.ht",
            FullName = name,
            PasswordHash = Hasher.HashPassword(new User { Username = username }, VerifierPassword),
            CreatedAtUtc = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc)
        };

    private static GlAccount Gl(string code, string name, GlAccountType type, NormalBalance nb) =>
        new()
        {
            Id = SeedGuids.Gl(code),
            TenantId = SeedGuids.TenantId,
            Code = code,
            NameFr = name,
            NameHt = name,
            NameEn = name,
            AccountType = type,
            NormalBalance = nb,
            CurrencyCode = Currencies.Htg,
            IsPostable = true,
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc)
        };

    public void Dispose() => Db.Dispose();
}
