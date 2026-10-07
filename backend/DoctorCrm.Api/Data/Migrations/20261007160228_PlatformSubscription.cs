using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorCrm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformSubscription : Migration
    {
        /// <summary>
        /// GrowDesk subscription billing (Stripe) and platform owners. Everyone who is an active
        /// Admin when this runs becomes a platform owner (on Marist: Charith and Dushan, the people
        /// who run the platform); admins added later are ordinary clinic admins. A fresh database
        /// has no users yet: the seeder makes the first admin an owner.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_platform_owner",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE users u SET is_platform_owner = TRUE
                WHERE u.is_active AND EXISTS (
                    SELECT 1 FROM user_roles ur JOIN roles r ON r.id = ur.role_id
                    WHERE ur.user_id = u.id AND r.name = 'Admin');
                """);

            migrationBuilder.CreateTable(
                name: "billing_accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    stripe_customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    stripe_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    subscription_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    current_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    billing_cycle_anchor = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    plan_amount = table.Column<long>(type: "bigint", nullable: true),
                    plan_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    plan_interval = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    plan_interval_count = table.Column<int>(type: "integer", nullable: false),
                    card_brand = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    card_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    unpaid_invoice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    unpaid_amount = table.Column<long>(type: "bigint", nullable: true),
                    unpaid_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    unpaid_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unpaid_invoice_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    overdue_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    final_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    final_retry_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "billing_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    secret_key_protected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    secret_key_hint = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    webhook_secret_protected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    price_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    grace_days = table.Column<int>(type: "integer", nullable: false),
                    first_payment_due = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_accounts");

            migrationBuilder.DropTable(
                name: "billing_settings");

            migrationBuilder.DropColumn(
                name: "is_platform_owner",
                table: "users");
        }
    }
}
