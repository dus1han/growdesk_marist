using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorCrm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FollowUpTracking : Migration
    {
        /// <summary>
        /// Follow-up tracking: attempts and the last follow-up on each customer. Recent enquiries
        /// (the last 14 days) still Interested or in Follow-up, with nothing booked and no
        /// follow-up date, are put into today's follow-ups so none is missed; older leads are left
        /// as they are.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "follow_up_attempts",
                table: "customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_follow_up_at",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE customers c SET next_follow_up_date = (now() AT TIME ZONE 'Asia/Dubai')::date
                FROM stages s
                WHERE s.id = c.stage_id AND s.system_key IN ('interested', 'follow_up')
                  AND c.is_active AND c.deleted_at IS NULL AND c.next_follow_up_date IS NULL
                  AND c.created_at >= now() - interval '14 days'
                  AND NOT EXISTS (SELECT 1 FROM bookings b WHERE b.customer_id = c.id AND b.status = 'Booked' AND b.deleted_at IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "follow_up_attempts",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "last_follow_up_at",
                table: "customers");
        }
    }
}
