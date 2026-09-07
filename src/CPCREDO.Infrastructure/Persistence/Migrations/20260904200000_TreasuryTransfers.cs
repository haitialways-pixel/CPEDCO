using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260904200000_TreasuryTransfers")]
public partial class TreasuryTransfers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "bank_accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                bank = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                gl_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_bank_accounts", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_bank_accounts_tenant_id_number",
            table: "bank_accounts",
            columns: ["tenant_id", "number"],
            unique: true);

        migrationBuilder.CreateTable(
            name: "treasury_transfers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                transfer_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                fee_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                approver1_id = table.Column<Guid>(type: "uuid", nullable: true),
                approver2_id = table.Column<Guid>(type: "uuid", nullable: true),
                executed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                approved1_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                approved2_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                executed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                bank_slip_ref = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                posted_journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_treasury_transfers", x => x.id);
                table.ForeignKey(
                    name: "fk_treasury_transfers_bank_accounts_bank_account_id",
                    column: x => x.bank_account_id,
                    principalTable: "bank_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_treasury_transfers_bank_account_id",
            table: "treasury_transfers",
            column: "bank_account_id");

        migrationBuilder.CreateIndex(
            name: "ix_treasury_transfers_tenant_id_transfer_no",
            table: "treasury_transfers",
            columns: ["tenant_id", "transfer_no"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "treasury_transfers");
        migrationBuilder.DropTable(name: "bank_accounts");
    }
}
