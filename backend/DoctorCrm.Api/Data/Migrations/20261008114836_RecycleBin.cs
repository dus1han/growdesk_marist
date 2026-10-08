using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorCrm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecycleBin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_treatments_name",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "ix_customers_instagram_name",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_whats_app_number",
                table: "customers");

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "treatments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deleted_by_id",
                table: "treatments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "stages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deleted_by_id",
                table: "stages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deleted_by_id",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "payment_methods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deleted_by_id",
                table: "payment_methods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "lead_sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deleted_by_id",
                table: "lead_sources",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deleted_by_id",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "cancellation_reasons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deleted_by_id",
                table: "cancellation_reasons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deleted_by_id",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_treatments_name",
                table: "treatments",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_instagram_name",
                table: "customers",
                column: "instagram_name",
                unique: true,
                filter: "instagram_name IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_whats_app_number",
                table: "customers",
                column: "whats_app_number",
                unique: true,
                filter: "whats_app_number IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_treatments_name",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "ix_customers_instagram_name",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_whats_app_number",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "treatments");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "treatments");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "stages");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "stages");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "payment_methods");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "payment_methods");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "lead_sources");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "lead_sources");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "cancellation_reasons");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "cancellation_reasons");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_treatments_name",
                table: "treatments",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_instagram_name",
                table: "customers",
                column: "instagram_name",
                unique: true,
                filter: "instagram_name IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_whats_app_number",
                table: "customers",
                column: "whats_app_number",
                unique: true,
                filter: "whats_app_number IS NOT NULL");
        }
    }
}
