using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260905040000_LoanDisbursement")]
public partial class LoanDisbursement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "compulsory_savings_percent",
            table: "loans",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "compulsory_savings_amount",
            table: "loans",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "cash_disbursed_amount",
            table: "loans",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "savings_disbursed_amount",
            table: "loans",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<Guid>(name: "savings_account_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "lien_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "posted_journal_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "till_session_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "submitted_by_user_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "submitted_at_utc", table: "loans", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "approver1_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "approved1_at_utc", table: "loans", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "approver2_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "approved2_at_utc", table: "loans", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "rejected_by_user_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "rejected_at_utc", table: "loans", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "reject_reason", table: "loans", type: "character varying(512)", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "disbursed_by_user_id", table: "loans", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "disbursed_at_utc", table: "loans", type: "timestamp with time zone", nullable: true);

        migrationBuilder.CreateIndex(name: "ix_loans_status", table: "loans", column: "status");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_loans_status", table: "loans");
        migrationBuilder.DropColumn(name: "compulsory_savings_percent", table: "loans");
        migrationBuilder.DropColumn(name: "compulsory_savings_amount", table: "loans");
        migrationBuilder.DropColumn(name: "cash_disbursed_amount", table: "loans");
        migrationBuilder.DropColumn(name: "savings_disbursed_amount", table: "loans");
        migrationBuilder.DropColumn(name: "savings_account_id", table: "loans");
        migrationBuilder.DropColumn(name: "lien_id", table: "loans");
        migrationBuilder.DropColumn(name: "posted_journal_id", table: "loans");
        migrationBuilder.DropColumn(name: "till_session_id", table: "loans");
        migrationBuilder.DropColumn(name: "submitted_by_user_id", table: "loans");
        migrationBuilder.DropColumn(name: "submitted_at_utc", table: "loans");
        migrationBuilder.DropColumn(name: "approver1_id", table: "loans");
        migrationBuilder.DropColumn(name: "approved1_at_utc", table: "loans");
        migrationBuilder.DropColumn(name: "approver2_id", table: "loans");
        migrationBuilder.DropColumn(name: "approved2_at_utc", table: "loans");
        migrationBuilder.DropColumn(name: "rejected_by_user_id", table: "loans");
        migrationBuilder.DropColumn(name: "rejected_at_utc", table: "loans");
        migrationBuilder.DropColumn(name: "reject_reason", table: "loans");
        migrationBuilder.DropColumn(name: "disbursed_by_user_id", table: "loans");
        migrationBuilder.DropColumn(name: "disbursed_at_utc", table: "loans");
    }
}
