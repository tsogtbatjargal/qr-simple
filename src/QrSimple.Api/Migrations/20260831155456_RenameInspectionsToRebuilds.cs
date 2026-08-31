using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrSimple.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameInspectionsToRebuilds : Migration
    {
        // Hand-written as in-place renames. `dotnet ef migrations add` scaffolds an entity
        // rename as DropTable + CreateTable, which would silently delete every existing record
        // along with its PDF — the scaffolded version is what the "may result in the loss of
        // data" warning was about. Rewriting it this way keeps the rows and produces exactly the
        // schema in the accompanying Designer snapshot.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Inspections_EquipmentId_InspectionDate",
                table: "Inspections");

            // Weekly/Monthly/Quarterly/Annual/Ad-hoc are inspection cadences; a rebuild happens
            // years apart and has no equivalent, so the column goes rather than being repurposed.
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Inspections");

            // The note carries the record now that the PDF is optional, so it becomes required.
            // Rows filed before this migration may have none — give those an empty string rather
            // than failing the NOT NULL, and let an operator fill them in from the admin UI.
            migrationBuilder.Sql("""UPDATE "Inspections" SET "Note" = '' WHERE "Note" IS NULL;""");

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "Inspections",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "Content",
                table: "Inspections",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                table: "Inspections",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "Inspections",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.RenameColumn(
                name: "InspectionDate",
                table: "Inspections",
                newName: "RebuildDate");

            // Postgres keeps a constraint's own name when its table is renamed, so the primary
            // key has to be renamed explicitly or it stays "PK_Inspections" and the next
            // migration that touches it fails against a name the snapshot no longer knows.
            migrationBuilder.DropPrimaryKey(
                name: "PK_Inspections",
                table: "Inspections");

            migrationBuilder.RenameTable(
                name: "Inspections",
                newName: "Rebuilds");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Rebuilds",
                table: "Rebuilds",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Rebuilds_EquipmentId_RebuildDate",
                table: "Rebuilds",
                columns: new[] { "EquipmentId", "RebuildDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rebuilds_EquipmentId_RebuildDate",
                table: "Rebuilds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Rebuilds",
                table: "Rebuilds");

            migrationBuilder.RenameTable(
                name: "Rebuilds",
                newName: "Inspections");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Inspections",
                table: "Inspections",
                column: "Id");

            migrationBuilder.RenameColumn(
                name: "RebuildDate",
                table: "Inspections",
                newName: "InspectionDate");

            // Rolling back can't recover a Kind that was never recorded, and can't invent PDF
            // bytes for a record filed without one. Both are filled with placeholders so the
            // NOT NULL constraints can go back on — a rollback is lossy by construction here.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Inspections",
                type: "text",
                nullable: false,
                defaultValue: "AdHoc");

            migrationBuilder.Sql("""
                UPDATE "Inspections"
                SET "Content" = COALESCE("Content", ''::bytea),
                    "ContentType" = COALESCE("ContentType", 'application/pdf'),
                    "FileName" = COALESCE("FileName", '');
                """);

            migrationBuilder.AlterColumn<byte[]>(
                name: "Content",
                table: "Inspections",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                table: "Inspections",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "Inspections",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "Inspections",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_EquipmentId_InspectionDate",
                table: "Inspections",
                columns: new[] { "EquipmentId", "InspectionDate" });
        }
    }
}
