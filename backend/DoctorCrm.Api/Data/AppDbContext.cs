using DoctorCrm.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<Treatment> Treatments => Set<Treatment>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<CancellationReason> CancellationReasons => Set<CancellationReason>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<CustomField> CustomFields => Set<CustomField>();
    public DbSet<CustomFieldOption> CustomFieldOptions => Set<CustomFieldOption>();
    public DbSet<CaptureFieldConfiguration> CaptureFieldConfigurations => Set<CaptureFieldConfiguration>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerTreatment> CustomerTreatments => Set<CustomerTreatment>();
    public DbSet<CustomerCustomFieldValue> CustomerCustomFieldValues => Set<CustomerCustomFieldValue>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<CalendarBlock> CalendarBlocks => Set<CalendarBlock>();
    public DbSet<BookingTreatment> BookingTreatments => Set<BookingTreatment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CaptureClient> CaptureClients => Set<CaptureClient>();
    public DbSet<BillingAccount> BillingAccounts => Set<BillingAccount>();
    public DbSet<BillingSettings> BillingSettings => Set<BillingSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Username).HasMaxLength(50).IsRequired();
            e.Property(x => x.NormalizedUsername).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.NormalizedUsername).IsUnique();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.PasswordHash).HasMaxLength(100).IsRequired();
        });

        b.Entity<Role>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(250);
        });

        b.Entity<Permission>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Description).HasMaxLength(250);
        });

        b.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId);
        });

        b.Entity<RolePermission>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Role).WithMany(x => x.RolePermissions).HasForeignKey(x => x.RoleId);
            e.HasOne(x => x.Permission).WithMany(x => x.RolePermissions).HasForeignKey(x => x.PermissionId);
        });

        b.Entity<Stage>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.SystemKey).HasMaxLength(50);
            e.HasIndex(x => x.SystemKey).IsUnique();
            e.Property(x => x.Color).HasMaxLength(9).IsRequired();
            e.HasIndex(x => x.DisplayOrder);
        });

        b.Entity<Treatment>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => x.DisplayOrder);
        });

        ConfigureSimpleLookup<LeadSource>(b);
        b.Entity<LeadSource>(e =>
        {
            e.Property(x => x.SystemKey).HasMaxLength(30);
            e.HasIndex(x => x.SystemKey).IsUnique().HasFilter("system_key IS NOT NULL");
        });
        ConfigureSimpleLookup<CancellationReason>(b);
        ConfigureSimpleLookup<PaymentMethod>(b);

        b.Entity<CustomField>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Label).HasMaxLength(100).IsRequired();
            e.Property(x => x.FieldType).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.DisplayOrder);
            e.Ignore(x => x.HasOptions);
            e.HasMany(x => x.Options).WithOne(x => x.CustomField).HasForeignKey(x => x.CustomFieldId);
        });

        b.Entity<CustomFieldOption>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.CustomFieldId, x.DisplayOrder });
        });

        b.Entity<CaptureFieldConfiguration>(e =>
        {
            e.Property(x => x.FieldKey).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.FieldKey).IsUnique();
            e.HasOne(x => x.CustomField).WithMany().HasForeignKey(x => x.CustomFieldId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.WhatsAppNumber).HasMaxLength(20);
            e.Property(x => x.SecondaryPhone).HasMaxLength(20);
            e.Property(x => x.InstagramName).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Notes).HasMaxLength(4000);

            // Duplicate detection (spec §35): unique when present.
            e.HasIndex(x => x.WhatsAppNumber).IsUnique().HasFilter("whats_app_number IS NOT NULL");
            e.HasIndex(x => x.InstagramName).IsUnique().HasFilter("instagram_name IS NOT NULL");
            e.HasIndex(x => x.StageId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.NextFollowUpDate);

            e.HasOne(x => x.Stage).WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LeadSource).WithMany().HasForeignKey(x => x.LeadSourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CustomerTreatment>(e =>
        {
            e.HasKey(x => new { x.CustomerId, x.TreatmentId });
            e.HasOne(x => x.Customer).WithMany(x => x.Treatments).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Treatment).WithMany().HasForeignKey(x => x.TreatmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TreatmentId);
        });

        b.Entity<CustomerCustomFieldValue>(e =>
        {
            e.HasKey(x => new { x.CustomerId, x.CustomFieldId });
            e.Property(x => x.Value).HasMaxLength(4000).IsRequired();
            e.HasOne(x => x.Customer).WithMany(x => x.CustomFieldValues).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CustomField).WithMany().HasForeignKey(x => x.CustomFieldId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Booking>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.DoctorNotes).HasMaxLength(4000);
            e.Property(x => x.CancellationNote).HasMaxLength(1000);
            e.Property(x => x.Source).HasMaxLength(40);
            e.Property(x => x.ConsultationCharge).HasPrecision(12, 2);

            // Calendar ranges and the per-doctor overlap check.
            e.HasIndex(x => new { x.BookingDate, x.StartTime });
            e.HasIndex(x => new { x.DoctorId, x.BookingDate });
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.OriginalBookingId);

            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.NextTreatment).WithMany().HasForeignKey(x => x.NextTreatmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OriginalBooking).WithMany().HasForeignKey(x => x.OriginalBookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CancellationReason).WithMany().HasForeignKey(x => x.CancellationReasonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<BookingTreatment>(e =>
        {
            // Spec §41: a treatment appears once per booking.
            e.HasIndex(x => new { x.BookingId, x.TreatmentId }).IsUnique();
            e.HasOne(x => x.Booking).WithMany(x => x.Treatments).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Treatment).WithMany().HasForeignKey(x => x.TreatmentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.CreatedAt);
            e.HasOne(x => x.Booking).WithMany(x => x.Payments).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PaymentMethod).WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<SystemSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Value).HasMaxLength(2000).IsRequired();
        });

        b.Entity<CalendarBlock>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(200);
            e.HasIndex(x => new { x.StartDate, x.EndDate });
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<BillingAccount>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.StripeCustomerId).HasMaxLength(100);
            e.Property(x => x.StripeSubscriptionId).HasMaxLength(100);
            e.Property(x => x.SubscriptionStatus).HasMaxLength(30);
            e.Property(x => x.PlanCurrency).HasMaxLength(3);
            e.Property(x => x.PlanInterval).HasMaxLength(10);
            e.Property(x => x.CardBrand).HasMaxLength(30);
            e.Property(x => x.CardLast4).HasMaxLength(4);
            e.Property(x => x.UnpaidInvoiceId).HasMaxLength(100);
            e.Property(x => x.UnpaidCurrency).HasMaxLength(3);
            e.Property(x => x.UnpaidInvoiceUrl).HasMaxLength(500);
            e.Property(x => x.FinalRetryError).HasMaxLength(500);
        });

        b.Entity<BillingSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SecretKeyProtected).HasMaxLength(1000);
            e.Property(x => x.SecretKeyHint).HasMaxLength(40);
            e.Property(x => x.WebhookSecretProtected).HasMaxLength(1000);
            e.Property(x => x.PriceId).HasMaxLength(100);
        });

        b.Entity<CaptureClient>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.ClientId).IsUnique();
            e.Property(x => x.SecretHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.ExtensionVersion).HasMaxLength(20);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });
    }

    private static void ConfigureSimpleLookup<T>(ModelBuilder b) where T : class, ILookupEntity =>
        b.Entity<T>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.DisplayOrder);
        });

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
