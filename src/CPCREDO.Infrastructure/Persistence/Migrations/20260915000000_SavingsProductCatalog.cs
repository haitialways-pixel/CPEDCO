using System;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260915000000_SavingsProductCatalog")]
public partial class SavingsProductCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "code",
            table: "savings_products",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "legal_name",
            table: "savings_products",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "commercial_name",
            table: "savings_products",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "product_kind",
            table: "savings_products",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "AVue");

        migrationBuilder.AddColumn<int>(
            name: "term_days",
            table: "savings_products",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "interest_rate_percent",
            table: "savings_products",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "interest_method",
            table: "savings_products",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "None");

        migrationBuilder.AddColumn<decimal>(
            name: "min_opening_amount",
            table: "savings_products",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<bool>(
            name: "allow_withdraw_before_term",
            table: "savings_products",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "updated_at_utc",
            table: "savings_products",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "matures_on",
            table: "savings_accounts",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "allow_withdraw_before_term",
            table: "savings_accounts",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql(
            """
            UPDATE savings_products
            SET legal_name = name,
                code = CASE
                    WHEN currency_code = 'USD' THEN 'EAV-USD'
                    ELSE 'EAV-HTG'
                END,
                product_kind = 'AVue',
                interest_method = 'None'
            WHERE legal_name = '' OR code = '';
            """);

        migrationBuilder.CreateIndex(
            name: "ix_savings_products_tenant_id_code",
            table: "savings_products",
            columns: new[] { "tenant_id", "code" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_savings_products_tenant_id_code",
            table: "savings_products");
        migrationBuilder.DropColumn(name: "code", table: "savings_products");
        migrationBuilder.DropColumn(name: "legal_name", table: "savings_products");
        migrationBuilder.DropColumn(name: "commercial_name", table: "savings_products");
        migrationBuilder.DropColumn(name: "product_kind", table: "savings_products");
        migrationBuilder.DropColumn(name: "term_days", table: "savings_products");
        migrationBuilder.DropColumn(name: "interest_rate_percent", table: "savings_products");
        migrationBuilder.DropColumn(name: "interest_method", table: "savings_products");
        migrationBuilder.DropColumn(name: "min_opening_amount", table: "savings_products");
        migrationBuilder.DropColumn(name: "allow_withdraw_before_term", table: "savings_products");
        migrationBuilder.DropColumn(name: "updated_at_utc", table: "savings_products");
        migrationBuilder.DropColumn(name: "matures_on", table: "savings_accounts");
        migrationBuilder.DropColumn(name: "allow_withdraw_before_term", table: "savings_accounts");
    }
}
