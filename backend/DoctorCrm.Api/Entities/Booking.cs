namespace DoctorCrm.Api.Entities;

public enum BookingStatus
{
    Booked,
    Completed,
    Rescheduled,
    Cancelled,
    NoShow,
}

/// <summary>
/// A consultation (spec §19, §40). One booking covers several treatments. Bookings are never
/// deleted: cancelling, no-shows and rescheduling change the status, and a reschedule creates a
/// new booking linked back through <see cref="OriginalBookingId"/>.
/// </summary>
public class Booking : AuditableEntity
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Optional. Overlaps are checked per doctor (bookings without a doctor share one calendar).</summary>
    public int? DoctorId { get; set; }
    public User? Doctor { get; set; }

    /// <summary>Clinic-local date and times.</summary>
    public DateOnly BookingDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Booked;
    public string? Notes { get; set; }

    /// <summary>Entered when the consultation is completed. Always 0 for a rescheduled booking.</summary>
    public decimal? ConsultationCharge { get; set; }

    /// <summary>Total received so far (Paid entries). Kept in step by <see cref="Services.BookingMoney"/>.</summary>
    public decimal AmountPaid { get; set; }

    /// <summary>
    /// Still owed on a completed consultation: charge − paid − waived, never below 0. What the
    /// customer's outstanding adds up. Kept in step by <see cref="Services.BookingMoney"/>.
    /// </summary>
    public decimal Balance { get; set; }

    public string? DoctorNotes { get; set; }

    /// <summary>Both set or both empty (spec §24).</summary>
    public DateOnly? NextTreatmentDate { get; set; }
    public int? NextTreatmentId { get; set; }
    public Treatment? NextTreatment { get; set; }

    /// <summary>On a booking created by rescheduling: the booking it replaced.</summary>
    public int? OriginalBookingId { get; set; }
    public Booking? OriginalBooking { get; set; }

    public int? CancellationReasonId { get; set; }
    public CancellationReason? CancellationReason { get; set; }
    public string? CancellationNote { get; set; }

    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? RescheduledAt { get; set; }
    public DateTime? NoShowAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>Set when something other than a GrowDesk user made the booking, e.g. "WhatsApp BOT".</summary>
    public string? Source { get; set; }

    public ICollection<BookingTreatment> Treatments { get; set; } = new List<BookingTreatment>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class BookingTreatment
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public int TreatmentId { get; set; }
    public Treatment Treatment { get; set; } = null!;
}

/// <summary>What a payment entry is.</summary>
public enum PaymentStatus
{
    /// <summary>Money received: the whole balance or part of it.</summary>
    Paid,

    /// <summary>
    /// Entries made before part payments: "nothing received yet", carrying the full charge. No
    /// longer created; ignored by balances and lists (what is owed is the booking's Balance).
    /// </summary>
    Pending,

    /// <summary>The remaining balance written off.</summary>
    Waived,
}

/// <summary>
/// A consultation payment (spec §28). The source of truth for payment state: bookings carry only
/// the charge. Kept as separate rows so history stays traceable.
/// </summary>
public class Payment
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public int? PaymentMethodId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>When the money was received; null while pending or waived.</summary>
    public DateTime? PaymentDate { get; set; }

    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
