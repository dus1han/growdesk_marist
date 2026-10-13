using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorCrm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NextTreatmentBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "next_booking_id",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_next_booking_id",
                table: "bookings",
                column: "next_booking_id");

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_bookings_next_booking_id",
                table: "bookings",
                column: "next_booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bookings_bookings_next_booking_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_next_booking_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "next_booking_id",
                table: "bookings");
        }
    }
}
