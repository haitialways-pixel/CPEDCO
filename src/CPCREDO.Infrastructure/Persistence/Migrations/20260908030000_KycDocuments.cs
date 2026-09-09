using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPCREDO.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CpcredoDbContext))]
[Migration("20260908030000_KycDocuments")]
public partial class KycDocuments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "kyc_documents",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                member_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                file_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                uploaded_by = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_kyc_documents", x => x.id);
                table.ForeignKey(
                    name: "fk_kyc_documents_members_member_id",
                    column: x => x.member_id,
                    principalTable: "members",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_kyc_documents_users_uploaded_by",
                    column: x => x.uploaded_by,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_kyc_documents_member_id_type",
            table: "kyc_documents",
            columns: new[] { "member_id", "type" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_kyc_documents_tenant_id_member_id",
            table: "kyc_documents",
            columns: new[] { "tenant_id", "member_id" });

        migrationBuilder.CreateIndex(
            name: "ix_kyc_documents_uploaded_by",
            table: "kyc_documents",
            column: "uploaded_by");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "kyc_documents");
    }
}
