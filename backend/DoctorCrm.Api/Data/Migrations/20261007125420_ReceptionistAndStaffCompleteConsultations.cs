using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorCrm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceptionistAndStaffCompleteConsultations : Migration
    {
        /// <summary>
        /// Receptionists and Staff can complete consultations too. The seeder only gives a brand-new
        /// role its default set, so existing Receptionist and Staff roles are granted it here. A fresh
        /// database has no roles yet: the seeder applies the new defaults.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission_id)
                SELECT r.id, p.id
                FROM roles r CROSS JOIN permissions p
                WHERE r.name IN ('Receptionist', 'Staff') AND p.key = 'bookings.complete'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM role_permissions rp
                USING roles r, permissions p
                WHERE rp.role_id = r.id AND rp.permission_id = p.id
                  AND r.name IN ('Receptionist', 'Staff') AND p.key = 'bookings.complete';
                """);
        }
    }
}
