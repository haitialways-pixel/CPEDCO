using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260922000000_CashMovementOpening")]
public partial class CashMovementOpening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "opening_movement_id",
            table: "till_sessions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "business_date",
            table: "till_sessions",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "source_type",
            table: "internal_cash_movements",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "Vault");

        migrationBuilder.AddColumn<Guid>(
            name: "source_id",
            table: "internal_cash_movements",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "destination_type",
            table: "internal_cash_movements",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "Till");

        migrationBuilder.AddColumn<Guid>(
            name: "destination_id",
            table: "internal_cash_movements",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "teller_user_id",
            table: "internal_cash_movements",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "business_date",
            table: "internal_cash_movements",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "reason",
            table: "internal_cash_movements",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "Refill");

        migrationBuilder.AddColumn<string>(
            name: "bag_id",
            table: "internal_cash_movements",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "received_amount",
            table: "internal_cash_movements",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "rejected_by_user_id",
            table: "internal_cash_movements",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "rejected_at_utc",
            table: "internal_cash_movements",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "internal_cash_denominations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                face_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                quantity = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_internal_cash_denominations", x => x.id);
                table.ForeignKey(
                    name: "fk_internal_cash_denominations_internal_cash_movements_movement_id",
                    column: x => x.movement_id,
                    principalTable: "internal_cash_movements",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_internal_cash_denominations_movement_id",
            table: "internal_cash_denominations",
            column: "movement_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "internal_cash_denominations");
        migrationBuilder.DropColumn(name: "opening_movement_id", table: "till_sessions");
        migrationBuilder.DropColumn(name: "business_date", table: "till_sessions");
        migrationBuilder.DropColumn(name: "source_type", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "source_id", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "destination_type", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "destination_id", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "teller_user_id", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "business_date", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "reason", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "bag_id", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "received_amount", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "rejected_by_user_id", table: "internal_cash_movements");
        migrationBuilder.DropColumn(name: "rejected_at_utc", table: "internal_cash_movements");
    }
}
