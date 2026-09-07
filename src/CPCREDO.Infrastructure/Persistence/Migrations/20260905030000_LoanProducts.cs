using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260905030000_LoanProducts")]
public partial class LoanProducts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "loan_products",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                legal_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                commercial_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                sms_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                term_days = table.Column<int>(type: "integer", nullable: false),
                installment_count = table.Column<int>(type: "integer", nullable: false),
                repayment_frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                interest_method = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                default_rate_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                max_renewals = table.Column<int>(type: "integer", nullable: true),
                compulsory_savings_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                min_principal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                max_principal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                officer_max_approval = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                early_payoff_charges_full_flat_interest = table.Column<bool>(type: "boolean", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_loan_products", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_loan_products_tenant_id_code",
            table: "loan_products",
            columns: ["tenant_id", "code"],
            unique: true);

        migrationBuilder.CreateTable(
            name: "loans",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                member_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                loan_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                principal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                agreed_rate_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                rate_applies_to_term_days = table.Column<int>(type: "integer", nullable: false),
                term_days = table.Column<int>(type: "integer", nullable: false),
                installment_count = table.Column<int>(type: "integer", nullable: false),
                repayment_frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                interest_method = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                total_interest = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                total_due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                cycle_number = table.Column<int>(type: "integer", nullable: false),
                renewed_from_loan_id = table.Column<Guid>(type: "uuid", nullable: true),
                renewed_to_loan_id = table.Column<Guid>(type: "uuid", nullable: true),
                origination_date = table.Column<DateOnly>(type: "date", nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_loans", x => x.id);
                table.ForeignKey(
                    name: "fk_loans_loan_products_product_id",
                    column: x => x.product_id,
                    principalTable: "loan_products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_loans_members_member_id",
                    column: x => x.member_id,
                    principalTable: "members",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_loans_tenant_id_loan_no",
            table: "loans",
            columns: ["tenant_id", "loan_no"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_loans_product_id",
            table: "loans",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_loans_member_id",
            table: "loans",
            column: "member_id");

        migrationBuilder.CreateTable(
            name: "loan_installments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false),
                due_date = table.Column<DateOnly>(type: "date", nullable: false),
                principal_due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                interest_due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                total_due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                principal_paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                interest_paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_loan_installments", x => x.id);
                table.ForeignKey(
                    name: "fk_loan_installments_loans_loan_id",
                    column: x => x.loan_id,
                    principalTable: "loans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_loan_installments_loan_id_line_no",
            table: "loan_installments",
            columns: ["loan_id", "line_no"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "loan_installments");
        migrationBuilder.DropTable(name: "loans");
        migrationBuilder.DropTable(name: "loan_products");
    }
}
