using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260908020000_LoanRenewalOutstandingPercent")]
public partial class LoanRenewalOutstandingPercent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "renewal_max_outstanding_percent",
            table: "loan_products",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "renewal_max_outstanding_percent",
            table: "loan_products");
    }
}
