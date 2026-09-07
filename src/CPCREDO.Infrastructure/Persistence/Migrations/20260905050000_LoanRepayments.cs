using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260905050000_LoanRepayments")]
public partial class LoanRepayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "late_penalty_percent_per_day",
            table: "loan_products",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "days_past_due",
            table: "loans",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateOnly>(
            name: "last_accrued_on",
            table: "loans",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "penalty_due",
            table: "loan_installments",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "penalty_paid",
            table: "loan_installments",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<DateOnly>(
            name: "last_penalty_accrued_on",
            table: "loan_installments",
            type: "date",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "loan_repayments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                receipt_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                penalty_allocated = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                interest_allocated = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                principal_allocated = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                posted_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                till_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_loan_repayments", x => x.id);
                table.ForeignKey(
                    name: "fk_loan_repayments_loans_loan_id",
                    column: x => x.loan_id,
                    principalTable: "loans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_loan_repayments_tenant_id_receipt_no",
            table: "loan_repayments",
            columns: ["tenant_id", "receipt_no"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_loan_repayments_loan_id",
            table: "loan_repayments",
            column: "loan_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "loan_repayments");
        migrationBuilder.DropColumn(name: "late_penalty_percent_per_day", table: "loan_products");
        migrationBuilder.DropColumn(name: "days_past_due", table: "loans");
        migrationBuilder.DropColumn(name: "last_accrued_on", table: "loans");
        migrationBuilder.DropColumn(name: "penalty_due", table: "loan_installments");
        migrationBuilder.DropColumn(name: "penalty_paid", table: "loan_installments");
        migrationBuilder.DropColumn(name: "last_penalty_accrued_on", table: "loan_installments");
    }
}
