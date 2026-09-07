using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "blocked_at_utc",
                table: "savings_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "blocked_by_user_id",
                table: "savings_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "blocked_reason",
                table: "savings_accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_blocked",
                table: "savings_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "member_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    assigned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_member_tickets_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_member_tickets_users_assigned_to_user_id",
                        column: x => x.assigned_to_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_member_tickets_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_member_tickets_assigned_to_user_id",
                table: "member_tickets",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_tickets_created_by_user_id",
                table: "member_tickets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_tickets_member_id",
                table: "member_tickets",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_tickets_tenant_id_member_id_created_at_utc",
                table: "member_tickets",
                columns: new[] { "tenant_id", "member_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_member_tickets_tenant_id_ticket_no",
                table: "member_tickets",
                columns: new[] { "tenant_id", "ticket_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "member_tickets");

            migrationBuilder.DropColumn(
                name: "blocked_at_utc",
                table: "savings_accounts");

            migrationBuilder.DropColumn(
                name: "blocked_by_user_id",
                table: "savings_accounts");

            migrationBuilder.DropColumn(
                name: "blocked_reason",
                table: "savings_accounts");

            migrationBuilder.DropColumn(
                name: "is_blocked",
                table: "savings_accounts");
        }
    }
}
