using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260908010000_InternalCashMovements")]
public partial class InternalCashMovements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "internal_cash_movements",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                movement_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                source_till_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                destination_till_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                accepted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_internal_cash_movements", x => x.id);
                table.ForeignKey(
                    name: "fk_internal_cash_movements_till_sessions_source_till_session_id",
                    column: x => x.source_till_session_id,
                    principalTable: "till_sessions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_internal_cash_movements_till_sessions_destination_till_session_id",
                    column: x => x.destination_till_session_id,
                    principalTable: "till_sessions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_internal_cash_movements_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_internal_cash_movements_tenant_id_movement_no",
            table: "internal_cash_movements",
            columns: new[] { "tenant_id", "movement_no" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_internal_cash_movements_tenant_id_idempotency_key",
            table: "internal_cash_movements",
            columns: new[] { "tenant_id", "idempotency_key" },
            unique: true,
            filter: "idempotency_key IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_internal_cash_movements_source_till_session_id",
            table: "internal_cash_movements",
            column: "source_till_session_id");

        migrationBuilder.CreateIndex(
            name: "ix_internal_cash_movements_destination_till_session_id",
            table: "internal_cash_movements",
            column: "destination_till_session_id");

        migrationBuilder.CreateIndex(
            name: "ix_internal_cash_movements_created_by_user_id",
            table: "internal_cash_movements",
            column: "created_by_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "internal_cash_movements");
    }
}
