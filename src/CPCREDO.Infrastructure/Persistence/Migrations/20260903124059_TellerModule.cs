using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TellerModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "savings_ledger_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "journal_entry_id",
                table: "savings_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "till_session_id",
                table: "savings_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "till_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    opening_float = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    expected_cash = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    counted_cash = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    over_short_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    over_short_journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_till_sessions_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "till_count_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    till_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    face_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_count_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_till_count_lines_till_sessions_till_session_id",
                        column: x => x.till_session_id,
                        principalTable: "till_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_savings_ledger_entries_tenant_id_idempotency_key",
                table: "savings_ledger_entries",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_till_count_lines_till_session_id",
                table: "till_count_lines",
                column: "till_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_branch_id",
                table: "till_sessions",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_tenant_id_user_id_branch_id_currency_code",
                table: "till_sessions",
                columns: new[] { "tenant_id", "user_id", "branch_id", "currency_code" },
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_user_id",
                table: "till_sessions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "till_count_lines");

            migrationBuilder.DropTable(
                name: "till_sessions");

            migrationBuilder.DropIndex(
                name: "ix_savings_ledger_entries_tenant_id_idempotency_key",
                table: "savings_ledger_entries");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "savings_ledger_entries");

            migrationBuilder.DropColumn(
                name: "journal_entry_id",
                table: "savings_ledger_entries");

            migrationBuilder.DropColumn(
                name: "till_session_id",
                table: "savings_ledger_entries");
        }
    }
}
