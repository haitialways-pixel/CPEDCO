using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260904190000_MembershipClasses")]
public partial class MembershipClasses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "legal_status",
            table: "members",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Usager");

        migrationBuilder.AddColumn<bool>(
            name: "is_founder",
            table: "members",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "founder_group",
            table: "members",
            type: "character varying(24)",
            maxLength: 24,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "usager_since_utc",
            table: "members",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "probation_days",
            table: "members",
            type: "integer",
            nullable: false,
            defaultValue: 90);

        migrationBuilder.RenameColumn(
            name: "qualification_share_count",
            table: "share_accounts",
            newName: "share_count");

        migrationBuilder.AddColumn<string>(
            name: "share_type",
            table: "share_accounts",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Qualification");

        migrationBuilder.DropIndex(
            name: "ix_share_accounts_member_id",
            table: "share_accounts");

        migrationBuilder.CreateIndex(
            name: "ix_share_accounts_member_id_share_type",
            table: "share_accounts",
            columns: ["member_id", "share_type"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_share_accounts_member_id_share_type",
            table: "share_accounts");

        migrationBuilder.DropColumn(name: "legal_status", table: "members");
        migrationBuilder.DropColumn(name: "is_founder", table: "members");
        migrationBuilder.DropColumn(name: "founder_group", table: "members");
        migrationBuilder.DropColumn(name: "usager_since_utc", table: "members");
        migrationBuilder.DropColumn(name: "probation_days", table: "members");
        migrationBuilder.DropColumn(name: "share_type", table: "share_accounts");

        migrationBuilder.RenameColumn(
            name: "share_count",
            table: "share_accounts",
            newName: "qualification_share_count");

        migrationBuilder.CreateIndex(
            name: "ix_share_accounts_member_id",
            table: "share_accounts",
            column: "member_id",
            unique: true);
    }
}
