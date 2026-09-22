using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260922010000_LivretPrintedFlag")]
public partial class LivretPrintedFlag : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "printed_on_livret_at_utc",
            table: "savings_ledger_entries",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "printed_on_livret_at_utc", table: "savings_ledger_entries");
    }
}
