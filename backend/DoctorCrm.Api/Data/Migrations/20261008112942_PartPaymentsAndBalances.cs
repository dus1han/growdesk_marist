using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorCrm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PartPaymentsAndBalances : Migration
    {
        /// <summary>
        /// Part payments: each booking keeps the amount paid and the balance still owed. Existing
        /// consultations get them from their entries: Paid counts as received, Waived as written
        /// off, and the old Pending entries (nothing received yet) leave the balance owed.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount_paid",
                table: "bookings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "balance",
                table: "bookings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_id_balance",
                table: "bookings",
                columns: new[] { "customer_id", "balance" },
                filter: "balance > 0");

            migrationBuilder.Sql("""
                UPDATE bookings b SET
                    amount_paid = COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.booking_id = b.id AND p.status = 'Paid'), 0),
                    balance = CASE
                        WHEN b.status = 'Completed' AND b.consultation_charge IS NOT NULL THEN GREATEST(0,
                            b.consultation_charge
                            - COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.booking_id = b.id AND p.status IN ('Paid', 'Waived')), 0))
                        ELSE 0 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_customer_id_balance",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "amount_paid",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "balance",
                table: "bookings");
        }
    }
}
