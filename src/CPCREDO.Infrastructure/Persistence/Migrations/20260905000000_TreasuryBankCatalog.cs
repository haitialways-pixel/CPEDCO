using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260905000000_TreasuryBankCatalog")]
public partial class TreasuryBankCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "custom_bank_name",
            table: "bank_accounts",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "notes",
            table: "bank_accounts",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "cancel_reason",
            table: "treasury_transfers",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "custom_bank_name", table: "bank_accounts");
        migrationBuilder.DropColumn(name: "notes", table: "bank_accounts");
        migrationBuilder.DropColumn(name: "cancel_reason", table: "treasury_transfers");
    }
}
