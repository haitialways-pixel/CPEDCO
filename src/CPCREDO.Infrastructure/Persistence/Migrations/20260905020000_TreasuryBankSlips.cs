using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260905020000_TreasuryBankSlips")]
public partial class TreasuryBankSlips : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "slip_type",
            table: "treasury_transfers",
            type: "character varying(24)",
            maxLength: 24,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "slip_file_name",
            table: "treasury_transfers",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "slip_content_type",
            table: "treasury_transfers",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<byte[]>(
            name: "slip_content",
            table: "treasury_transfers",
            type: "bytea",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "slip_uploaded_at_utc",
            table: "treasury_transfers",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "slip_type", table: "treasury_transfers");
        migrationBuilder.DropColumn(name: "slip_file_name", table: "treasury_transfers");
        migrationBuilder.DropColumn(name: "slip_content_type", table: "treasury_transfers");
        migrationBuilder.DropColumn(name: "slip_content", table: "treasury_transfers");
        migrationBuilder.DropColumn(name: "slip_uploaded_at_utc", table: "treasury_transfers");
    }
}
