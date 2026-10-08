using DoctorCrm.Api.Entities;

namespace DoctorCrm.Api.Services;

/// <summary>
/// A consultation's money in one place: what was charged, what has been paid, and the balance
/// still owed. Payments are entries (part payments allowed); the booking keeps the totals so
/// lists, filters and a customer's outstanding are simple sums.
/// </summary>
public static class BookingMoney
{
    /// <summary>Payment states shown in lists and used as filters.</summary>
    public const string Paid = "Paid", PartlyPaid = "PartlyPaid", Unpaid = "Unpaid", Waived = "Waived", NoCharge = "NoCharge";

    /// <summary>"Outstanding": anything still owed, paid in part or not at all.</summary>
    public const string Outstanding = "Outstanding";

    /// <summary>Recomputes the totals from the booking's (loaded) payment entries.</summary>
    public static void Recalculate(Booking booking)
    {
        var paid = booking.Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
        var waived = booking.Payments.Where(p => p.Status == PaymentStatus.Waived).Sum(p => p.Amount);
        booking.AmountPaid = paid;
        booking.Balance = booking.Status == BookingStatus.Completed && booking.ConsultationCharge is { } charge
            ? Math.Max(0, charge - paid - waived)
            : 0;
    }

    /// <summary>The payment state of a consultation, or null before it is completed.</summary>
    public static string? State(BookingStatus status, decimal? charge, decimal paid, decimal balance) =>
        status != BookingStatus.Completed || charge is null ? null
        : charge == 0 ? NoCharge
        : balance == 0 ? (paid > 0 ? Paid : Waived)
        : paid > 0 ? PartlyPaid : Unpaid;

    /// <summary>Filters by payment state (also "Outstanding", and the old "Pending" meaning the same).</summary>
    public static IQueryable<Booking> WhereState(IQueryable<Booking> query, string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "outstanding" or "pending" => query.Where(b => b.Balance > 0),
        "unpaid" => query.Where(b => b.Balance > 0 && b.AmountPaid == 0),
        "partlypaid" => query.Where(b => b.Balance > 0 && b.AmountPaid > 0),
        "paid" => query.Where(b => b.Status == BookingStatus.Completed && b.ConsultationCharge > 0 && b.Balance == 0 && b.AmountPaid > 0),
        "waived" => query.Where(b => b.Status == BookingStatus.Completed && b.ConsultationCharge > 0 && b.Balance == 0 && b.AmountPaid == 0),
        "nocharge" => query.Where(b => b.Status == BookingStatus.Completed && b.ConsultationCharge == 0),
        _ => query,
    };
}
