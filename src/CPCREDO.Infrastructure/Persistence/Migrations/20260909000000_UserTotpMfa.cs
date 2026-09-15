using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260909000000_UserTotpMfa")]
public partial class UserTotpMfa : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "mfa_enabled",
            table: "users",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "totp_secret_protected",
            table: "users",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "last_totp_timestep",
            table: "users",
            type: "bigint",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "mfa_enabled", table: "users");
        migrationBuilder.DropColumn(name: "totp_secret_protected", table: "users");
        migrationBuilder.DropColumn(name: "last_totp_timestep", table: "users");
    }
}
