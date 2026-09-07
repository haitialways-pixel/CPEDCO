using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260905010000_TreasuryTillMovements")]
public partial class TreasuryTillMovements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "bank_account_id",
            table: "treasury_transfers",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<string>(
            name: "direction",
            table: "treasury_transfers",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(16)",
            oldMaxLength: 16);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "direction",
            table: "treasury_transfers",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(24)",
            oldMaxLength: 24);

        migrationBuilder.AlterColumn<Guid>(
            name: "bank_account_id",
            table: "treasury_transfers",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
