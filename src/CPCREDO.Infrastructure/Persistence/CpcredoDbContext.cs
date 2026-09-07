using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Audit;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Teller;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Domain.Loans;
using CPCREDO.Domain.Treasury;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Persistence;

public class CpcredoDbContext : DbContext
{
    public CpcredoDbContext(DbContextOptions<CpcredoDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<ShareAccount> ShareAccounts => Set<ShareAccount>();
    public DbSet<MemberTicket> MemberTickets => Set<MemberTicket>();
    public DbSet<SavingsProduct> SavingsProducts => Set<SavingsProduct>();
    public DbSet<SavingsAccount> SavingsAccounts => Set<SavingsAccount>();
    public DbSet<SavingsLien> SavingsLiens => Set<SavingsLien>();
    public DbSet<SavingsLedgerEntry> SavingsLedgerEntries => Set<SavingsLedgerEntry>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<GlAccount> GlAccounts => Set<GlAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<TillSession> TillSessions => Set<TillSession>();
    public DbSet<TillCountLine> TillCountLines => Set<TillCountLine>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<TreasuryTransfer> TreasuryTransfers => Set<TreasuryTransfer>();
    public DbSet<LoanProduct> LoanProducts => Set<LoanProduct>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanInstallment> LoanInstallments => Set<LoanInstallment>();
    public DbSet<LoanRepayment> LoanRepayments => Set<LoanRepayment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public override int SaveChanges()
    {
        GuardPostedJournals();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardPostedJournals();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardPostedJournals();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardPostedJournals();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardPostedJournals()
    {
        foreach (var entry in ChangeTracker.Entries<JournalEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new PostedJournalImmutableException();
        }

        foreach (var entry in ChangeTracker.Entries<JournalLine>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new PostedJournalImmutableException();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var npgsql = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        ConfigureTenancy(modelBuilder);
        ConfigureIdentity(modelBuilder);
        ConfigureMembers(modelBuilder, npgsql);
        ConfigureTeller(modelBuilder, npgsql);
        ConfigureTreasury(modelBuilder);
        ConfigureLoans(modelBuilder);
        ConfigureAccounting(modelBuilder, npgsql);
        ConfigureAudit(modelBuilder, npgsql);
    }

    private static void ConfigureTenancy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Sigle).HasMaxLength(32).IsRequired();
            b.Property(x => x.LegalName).HasMaxLength(256).IsRequired();
            b.Property(x => x.City).HasMaxLength(128).IsRequired();
            b.Property(x => x.Country).HasMaxLength(64).IsRequired();
            b.Property(x => x.PrimaryCurrency).HasMaxLength(3).IsRequired();
            b.Property(x => x.SecondaryCurrency).HasMaxLength(3).IsRequired();
            b.Property(x => x.DisplayTimeZone).HasMaxLength(64).IsRequired();
            b.Property(x => x.ShareParValue)
                .HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale)
                .HasDefaultValue(MembershipRules.DefaultShareParValue);
            b.HasIndex(x => x.Sigle).IsUnique();
            b.HasOne(x => x.DefaultBranch)
                .WithMany()
                .HasForeignKey(x => x.DefaultBranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("branches");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Code).HasMaxLength(16).IsRequired();
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.City).HasMaxLength(128).IsRequired();
            b.Property(x => x.Country).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasOne(x => x.Tenant)
                .WithMany(t => t.Branches)
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(64).IsRequired();
            b.Property(x => x.DisplayNameFr).HasMaxLength(128).IsRequired();
            b.Property(x => x.DisplayNameHt).HasMaxLength(128).IsRequired();
            b.Property(x => x.DisplayNameEn).HasMaxLength(128).IsRequired();
            b.Property(x => x.DescriptionFr).HasMaxLength(256).IsRequired();
            b.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Username).HasMaxLength(64).IsRequired();
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(128).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            b.Property(x => x.MustChangePassword).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Username }).IsUnique();
            b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserRole>(b =>
        {
            b.ToTable("user_roles");
            b.HasKey(x => new { x.UserId, x.RoleId });
            b.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureMembers(ModelBuilder modelBuilder, bool npgsql)
    {
        modelBuilder.Entity<Member>(b =>
        {
            b.ToTable("members");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.MemberNo).HasMaxLength(32).IsRequired();
            b.Property(x => x.FirstName).HasMaxLength(128).IsRequired();
            b.Property(x => x.LastName).HasMaxLength(128).IsRequired();
            b.Property(x => x.Cin).HasMaxLength(32);
            b.Property(x => x.Nif).HasMaxLength(32);
            b.Property(x => x.Phone).HasMaxLength(32).IsRequired();
            b.Property(x => x.AlternatePhone).HasMaxLength(32);
            b.Property(x => x.AddressLine).HasMaxLength(256).IsRequired();
            b.Property(x => x.City).HasMaxLength(128).IsRequired();
            b.Property(x => x.Commune).HasMaxLength(128);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.KycStatus).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.LegalStatus).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.FounderGroup).HasConversion<string>().HasMaxLength(24);
            b.Property(x => x.ProbationDays).HasDefaultValue(MembershipRules.DefaultProbationDays);
            b.Ignore(x => x.FullName);
            b.Ignore(x => x.QualificationShareCount);
            b.Ignore(x => x.PermanentShareCount);
            b.Ignore(x => x.HasVotingRights);
            b.HasIndex(x => new { x.TenantId, x.MemberNo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.LastName, x.FirstName });
            if (npgsql)
            {
                b.HasIndex(x => new { x.TenantId, x.Cin })
                    .IsUnique()
                    .HasFilter("cin IS NOT NULL AND cin <> ''");
                b.HasIndex(x => new { x.TenantId, x.Nif })
                    .IsUnique()
                    .HasFilter("nif IS NOT NULL AND nif <> ''");
            }

            b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.ShareAccounts)
                .WithOne(s => s.Member)
                .HasForeignKey(s => s.MemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MemberTicket>(b =>
        {
            b.ToTable("member_tickets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.TicketNo).HasMaxLength(32).IsRequired();
            b.Property(x => x.Subject).HasMaxLength(160).IsRequired();
            b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(x => new { x.TenantId, x.TicketNo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.MemberId, x.CreatedAtUtc });
            b.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShareAccount>(b =>
        {
            b.ToTable("share_accounts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.AccountNo).HasMaxLength(32).IsRequired();
            b.Property(x => x.ShareType).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.ShareCount).IsRequired();
            b.Property(x => x.ParValue).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Ignore(x => x.BookValue);
            b.HasIndex(x => new { x.TenantId, x.AccountNo }).IsUnique();
            b.HasIndex(x => new { x.MemberId, x.ShareType }).IsUnique();
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SavingsProduct>(b =>
        {
            b.ToTable("savings_products");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.MinimumBalance).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<GlAccount>().WithMany().HasForeignKey(x => x.LiabilityGlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<GlAccount>().WithMany().HasForeignKey(x => x.CashGlAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SavingsAccount>(b =>
        {
            b.ToTable("savings_accounts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.AccountNo).HasMaxLength(32).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.MinimumBalance).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.BlockedReason).HasMaxLength(256);
            b.Ignore(x => x.LedgerBalance);
            b.Ignore(x => x.AvailableBalance);
            b.HasIndex(x => new { x.TenantId, x.AccountNo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.MemberId, x.ProductId }).IsUnique();
            b.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SavingsLien>(b =>
        {
            b.ToTable("savings_liens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.Amount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.Reason).HasMaxLength(256).IsRequired();
            b.HasOne(x => x.SavingsAccount).WithMany(x => x.Liens).HasForeignKey(x => x.SavingsAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavingsLedgerEntry>(b =>
        {
            b.ToTable("savings_ledger_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.EntryType).HasMaxLength(16).IsRequired();
            b.Property(x => x.Amount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.Description).HasMaxLength(256).IsRequired();
            b.Property(x => x.IdempotencyKey).HasMaxLength(128);
            b.Ignore(x => x.SignedAmount);
            b.HasOne(x => x.SavingsAccount).WithMany(x => x.LedgerEntries).HasForeignKey(x => x.SavingsAccountId).OnDelete(DeleteBehavior.Cascade);
            if (npgsql)
            {
                b.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
                    .IsUnique()
                    .HasFilter("idempotency_key IS NOT NULL");
            }
        });

        modelBuilder.Entity<NumberSequence>(b =>
        {
            b.ToTable("number_sequences");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Key).HasMaxLength(32).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });
    }

    private static void ConfigureTeller(ModelBuilder modelBuilder, bool npgsql)
    {
        modelBuilder.Entity<TillSession>(b =>
        {
            b.ToTable("till_sessions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.OpeningFloat).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.ExpectedCash).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CountedCash).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.OverShortAmount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            if (npgsql)
            {
                b.HasIndex(x => new { x.TenantId, x.UserId, x.BranchId, x.CurrencyCode })
                    .IsUnique()
                    .HasFilter("status = 'Open'");
            }
        });

        modelBuilder.Entity<TillCountLine>(b =>
        {
            b.ToTable("till_count_lines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.FaceValue).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Ignore(x => x.Subtotal);
            b.HasOne(x => x.TillSession).WithMany(t => t.CountLines).HasForeignKey(x => x.TillSessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTreasury(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BankAccount>(b =>
        {
            b.ToTable("bank_accounts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.Bank).HasMaxLength(128).IsRequired();
            b.Property(x => x.CustomBankName).HasMaxLength(128);
            b.Property(x => x.Number).HasMaxLength(64).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.GlCode).HasMaxLength(16).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(512);
            b.Ignore(x => x.DisplayBankName);
            b.Ignore(x => x.MaskedNumber);
            b.Ignore(x => x.PickerLabel);
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        });

        modelBuilder.Entity<TreasuryTransfer>(b =>
        {
            b.ToTable("treasury_transfers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.TransferNo).HasMaxLength(32).IsRequired();
            b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(24);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Amount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.FeeAmount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.Approver1Id).HasColumnName("approver1_id");
            b.Property(x => x.Approver2Id).HasColumnName("approver2_id");
            b.Property(x => x.Approved1AtUtc).HasColumnName("approved1_at_utc");
            b.Property(x => x.Approved2AtUtc).HasColumnName("approved2_at_utc");
            b.Property(x => x.BankSlipRef).HasMaxLength(64);
            b.Property(x => x.SlipType).HasConversion<string>().HasMaxLength(24);
            b.Property(x => x.SlipFileName).HasMaxLength(256);
            b.Property(x => x.SlipContentType).HasMaxLength(128);
            b.Property(x => x.Notes).HasMaxLength(512);
            b.Property(x => x.CancelReason).HasMaxLength(512);
            b.HasIndex(x => new { x.TenantId, x.TransferNo }).IsUnique();
            b.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureLoans(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoanProduct>(b =>
        {
            b.ToTable("loan_products");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Code).HasMaxLength(32).IsRequired();
            b.Property(x => x.LegalName).HasMaxLength(128).IsRequired();
            b.Property(x => x.CommercialName).HasMaxLength(128);
            b.Property(x => x.SmsName).HasMaxLength(20);
            b.Property(x => x.RepaymentFrequency).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.InterestMethod).HasConversion<string>().HasMaxLength(48);
            b.Property(x => x.DefaultRatePercent).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.LatePenaltyPercentPerDay).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CompulsorySavingsPercent).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.MinPrincipal).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.MaxPrincipal).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.OfficerMaxApproval).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Ignore(x => x.DisplayName);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<Loan>(b =>
        {
            b.ToTable("loans");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.LoanNo).HasMaxLength(32).IsRequired();
            b.Property(x => x.Principal).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.AgreedRatePercent).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.TotalInterest).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.TotalDue).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CompulsorySavingsPercent).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CompulsorySavingsAmount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CashDisbursedAmount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.SavingsDisbursedAmount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            b.Property(x => x.RepaymentFrequency).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.InterestMethod).HasConversion<string>().HasMaxLength(48);
            b.Property(x => x.RejectReason).HasMaxLength(512);
            b.Property(x => x.Approver1Id).HasColumnName("approver1_id");
            b.Property(x => x.Approver2Id).HasColumnName("approver2_id");
            b.Property(x => x.Approved1AtUtc).HasColumnName("approved1_at_utc");
            b.Property(x => x.Approved2AtUtc).HasColumnName("approved2_at_utc");
            b.HasIndex(x => new { x.TenantId, x.LoanNo }).IsUnique();
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.ProductId);
            b.HasIndex(x => x.MemberId);
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Member>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LoanInstallment>(b =>
        {
            b.ToTable("loan_installments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.PrincipalDue).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.InterestDue).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.PenaltyDue).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.TotalDue).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.PrincipalPaid).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.InterestPaid).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.PenaltyPaid).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.HasIndex(x => new { x.LoanId, x.LineNo }).IsUnique();
            b.HasOne(x => x.Loan).WithMany(l => l.Installments).HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoanRepayment>(b =>
        {
            b.ToTable("loan_repayments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ReceiptNo).HasMaxLength(32).IsRequired();
            b.Property(x => x.Amount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.PenaltyAllocated).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.InterestAllocated).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.PrincipalAllocated).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.IdempotencyKey).HasMaxLength(128);
            b.HasIndex(x => new { x.TenantId, x.ReceiptNo }).IsUnique();
            b.HasOne(x => x.Loan).WithMany(l => l.Repayments).HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAccounting(ModelBuilder modelBuilder, bool npgsql)
    {
        modelBuilder.Entity<GlAccount>(b =>
        {
            b.ToTable("gl_accounts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Code).HasMaxLength(16).IsRequired();
            b.Property(x => x.NameFr).HasMaxLength(128).IsRequired();
            b.Property(x => x.NameHt).HasMaxLength(128).IsRequired();
            b.Property(x => x.NameEn).HasMaxLength(128).IsRequired();
            b.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.NormalBalance).HasConversion<string>().HasMaxLength(8);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalEntry>(b =>
        {
            b.ToTable("journal_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.JournalNo).HasMaxLength(32).IsRequired();
            b.Property(x => x.Description).HasMaxLength(512).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.IdempotencyKey).HasMaxLength(128);
            b.Ignore(x => x.TotalDebit);
            b.Ignore(x => x.TotalCredit);
            b.HasIndex(x => new { x.TenantId, x.JournalNo }).IsUnique();
            if (npgsql)
            {
                b.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
                    .IsUnique()
                    .HasFilter("idempotency_key IS NOT NULL");
            }
            b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.PostedByUser).WithMany().HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ReversalOf)
                .WithMany()
                .HasForeignKey(x => x.ReversalOfJournalId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Lines)
                .WithOne(l => l.JournalEntry)
                .HasForeignKey(l => l.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalLine>(b =>
        {
            if (npgsql)
            {
                b.ToTable("journal_lines", t =>
                {
                    t.HasCheckConstraint(
                        "ck_journal_line_one_side",
                        "(debit = 0 AND credit > 0) OR (credit = 0 AND debit > 0)");
                });
            }
            else
            {
                b.ToTable("journal_lines");
            }
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Debit).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.Credit).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale);
            b.Property(x => x.Description).HasMaxLength(256).IsRequired();
            b.HasIndex(x => new { x.JournalEntryId, x.LineNo }).IsUnique();
            b.HasOne(x => x.GlAccount).WithMany().HasForeignKey(x => x.GlAccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAudit(ModelBuilder modelBuilder, bool npgsql)
    {
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("audit_logs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Action).HasMaxLength(64).IsRequired();
            b.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            if (npgsql)
                b.Property(x => x.DetailsJson).HasColumnType("jsonb");
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.HasIndex(x => x.OccurredAtUtc);
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_records");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Key).HasMaxLength(128).IsRequired();
            b.Property(x => x.HttpMethod).HasMaxLength(16).IsRequired();
            b.Property(x => x.Path).HasMaxLength(256).IsRequired();
            b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.ResponseBody).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            b.HasIndex(x => x.ExpiresAtUtc);
        });
    }
}
