using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260908040000_MemberIdentityAndLivret")]
public partial class MemberIdentityAndLivret : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "date_of_birth",
            table: "members",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "place_of_birth",
            table: "members",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "occupation",
            table: "members",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "last_passbook_print_at_utc",
            table: "savings_accounts",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "date_of_birth", table: "members");
        migrationBuilder.DropColumn(name: "place_of_birth", table: "members");
        migrationBuilder.DropColumn(name: "occupation", table: "members");
        migrationBuilder.DropColumn(name: "last_passbook_print_at_utc", table: "savings_accounts");
    }
}
