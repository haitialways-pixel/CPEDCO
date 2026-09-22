using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260922020000_CreditPool")]
public partial class CreditPool : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "credit_pools",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                funded_total = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                disbursed_total = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_credit_pools", x => x.id));

        migrationBuilder.CreateIndex(
            name: "ix_credit_pools_tenant_id_currency_code",
            table: "credit_pools",
            columns: new[] { "tenant_id", "currency_code" },
            unique: true);

        migrationBuilder.CreateTable(
            name: "credit_pool_movements",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                source_kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                bank_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                loan_id = table.Column<Guid>(type: "uuid", nullable: true),
                amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_credit_pool_movements", x => x.id);
                table.ForeignKey(
                    name: "fk_credit_pool_movements_credit_pools_pool_id",
                    column: x => x.pool_id,
                    principalTable: "credit_pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_credit_pool_movements_pool_id",
            table: "credit_pool_movements",
            column: "pool_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "credit_pool_movements");
        migrationBuilder.DropTable(name: "credit_pools");
    }
}
