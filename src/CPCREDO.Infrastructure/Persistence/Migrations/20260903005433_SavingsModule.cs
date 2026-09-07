using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavingsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "savings_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    minimum_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    liability_gl_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_gl_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_savings_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_savings_products_gl_accounts_cash_gl_account_id",
                        column: x => x.cash_gl_account_id,
                        principalTable: "gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_savings_products_gl_accounts_liability_gl_account_id",
                        column: x => x.liability_gl_account_id,
                        principalTable: "gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_savings_products_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "savings_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    minimum_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_savings_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_savings_accounts_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_savings_accounts_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_savings_accounts_savings_products_product_id",
                        column: x => x.product_id,
                        principalTable: "savings_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "savings_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    savings_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_savings_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_savings_ledger_entries_savings_accounts_savings_account_id",
                        column: x => x.savings_account_id,
                        principalTable: "savings_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "savings_liens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    savings_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    released_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_savings_liens", x => x.id);
                    table.ForeignKey(
                        name: "fk_savings_liens_savings_accounts_savings_account_id",
                        column: x => x.savings_account_id,
                        principalTable: "savings_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_savings_accounts_branch_id",
                table: "savings_accounts",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_savings_accounts_member_id",
                table: "savings_accounts",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_savings_accounts_product_id",
                table: "savings_accounts",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_savings_accounts_tenant_id_account_no",
                table: "savings_accounts",
                columns: new[] { "tenant_id", "account_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_savings_accounts_tenant_id_member_id_product_id",
                table: "savings_accounts",
                columns: new[] { "tenant_id", "member_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_savings_ledger_entries_savings_account_id",
                table: "savings_ledger_entries",
                column: "savings_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_savings_liens_savings_account_id",
                table: "savings_liens",
                column: "savings_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_savings_products_cash_gl_account_id",
                table: "savings_products",
                column: "cash_gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_savings_products_liability_gl_account_id",
                table: "savings_products",
                column: "liability_gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_savings_products_tenant_id_name",
                table: "savings_products",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "savings_ledger_entries");

            migrationBuilder.DropTable(
                name: "savings_liens");

            migrationBuilder.DropTable(
                name: "savings_accounts");

            migrationBuilder.DropTable(
                name: "savings_products");
        }
    }
}
