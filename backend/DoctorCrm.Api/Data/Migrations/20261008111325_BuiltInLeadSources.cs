using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorCrm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BuiltInLeadSources : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// WhatsApp and Instagram become built-in lead sources (the seeder adopts the existing rows
        /// by name on start-up). The capture field is switched on so toolbars older than 1.0.13,
        /// which preset the source only when it is shown, keep sending it until they update.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE capture_field_configurations SET is_enabled = TRUE WHERE field_key = 'lead_source';");

            migrationBuilder.AddColumn<string>(
                name: "system_key",
                table: "lead_sources",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lead_sources_system_key",
                table: "lead_sources",
                column: "system_key",
                unique: true,
                filter: "system_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lead_sources_system_key",
                table: "lead_sources");

            migrationBuilder.DropColumn(
                name: "system_key",
                table: "lead_sources");
        }
    }
}
