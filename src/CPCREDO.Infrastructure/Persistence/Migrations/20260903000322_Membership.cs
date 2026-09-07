using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Membership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "share_par_value",
                table: "tenants",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 500.0000m);

            migrationBuilder.AddColumn<string>(
                name: "address_line",
                table: "members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "alternate_phone",
                table: "members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cin",
                table: "members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "commune",
                table: "members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kyc_status",
                table: "members",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "nif",
                table: "members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "number_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "share_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    qualification_share_count = table.Column<int>(type: "integer", nullable: false),
                    par_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_share_accounts_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_share_accounts_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_cin",
                table: "members",
                columns: new[] { "tenant_id", "cin" },
                unique: true,
                filter: "cin IS NOT NULL AND cin <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_last_name_first_name",
                table: "members",
                columns: new[] { "tenant_id", "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_nif",
                table: "members",
                columns: new[] { "tenant_id", "nif" },
                unique: true,
                filter: "nif IS NOT NULL AND nif <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_tenant_id_key",
                table: "number_sequences",
                columns: new[] { "tenant_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_share_accounts_branch_id",
                table: "share_accounts",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_share_accounts_member_id",
                table: "share_accounts",
                column: "member_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_share_accounts_tenant_id_account_no",
                table: "share_accounts",
                columns: new[] { "tenant_id", "account_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_sequences");

            migrationBuilder.DropTable(
                name: "share_accounts");

            migrationBuilder.DropIndex(
                name: "ix_members_tenant_id_cin",
                table: "members");

            migrationBuilder.DropIndex(
                name: "ix_members_tenant_id_last_name_first_name",
                table: "members");

            migrationBuilder.DropIndex(
                name: "ix_members_tenant_id_nif",
                table: "members");

            migrationBuilder.DropColumn(
                name: "share_par_value",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "address_line",
                table: "members");

            migrationBuilder.DropColumn(
                name: "alternate_phone",
                table: "members");

            migrationBuilder.DropColumn(
                name: "cin",
                table: "members");

            migrationBuilder.DropColumn(
                name: "city",
                table: "members");

            migrationBuilder.DropColumn(
                name: "commune",
                table: "members");

            migrationBuilder.DropColumn(
                name: "kyc_status",
                table: "members");

            migrationBuilder.DropColumn(
                name: "nif",
                table: "members");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "members");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "members");
        }
    }
}
