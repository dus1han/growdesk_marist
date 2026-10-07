using System.Net;
using System.Net.Http.Json;
using DoctorCrm.Api.DTOs;

namespace DoctorCrm.Tests;

/// <summary>Dashboard (spec §10, §11): every figure comes from live data, in the clinic's day.</summary>
public class DashboardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateCookieClient();
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminUsername, ApiFactory.AdminPassword))).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> DataAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.True(body!.Success, body.Message);
        return body.Data!;
    }

    private static SaveCustomerRequest Customer(string name, string whatsApp, int treatmentId, DateOnly? followUp) =>
        new(name, whatsApp, null, null, null, null, 1, null, [treatmentId], null, followUp, null, null);

    [Fact]
    public async Task Dashboard_reflects_bookings_customers_follow_ups_and_activity()
    {
        var admin = await AdminAsync();
        var before = await DataAsync<DashboardDto>(await admin.GetAsync("/api/dashboard"));
        var today = before.Today;
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var reasons = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/cancellation-reasons"));

        // A: a potential customer whose follow-up is overdue. B: has consultations.
        var a = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            Customer("Dash Alice", "054 7100001", treatments[0].Id, today.AddDays(-2))));
        var b = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            Customer("Dash Bob", "054 7100002", treatments[1].Id, null)));

        async Task<int> BookAsync(DateOnly date, int hour) =>
            (await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
                new CreateBookingRequest(b.Id, null, date, new TimeOnly(hour, 0), new TimeOnly(hour, 30), [treatments[0].Id, treatments[1].Id], null)))).Id;

        var completed = await BookAsync(today, 6);
        var cancelled = await BookAsync(today, 7);
        var stillBooked = await BookAsync(today, 8);
        await BookAsync(today.AddDays(-1), 9);
        await BookAsync(today.AddDays(1), 10);

        await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{completed}/complete",
            new CompleteBookingRequest(200, "Paid", (await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods")))[0].Id, null, null, null)));
        await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{cancelled}/cancel",
            new CancelBookingRequest(reasons[0].Id, null)));

        var after = await DataAsync<DashboardDto>(await admin.GetAsync("/api/dashboard"));

        // Consultations: the cancelled one does not count.
        Assert.Equal(before.Bookings!.Today + 2, after.Bookings!.Today);
        Assert.Equal(before.Bookings.StillBookedToday + 1, after.Bookings.StillBookedToday);
        Assert.Equal(before.Bookings.Yesterday + 1, after.Bookings.Yesterday);
        Assert.Equal(before.Bookings.Upcoming + 1, after.Bookings.Upcoming);
        Assert.Equal(before.Bookings.UpcomingThisWeek + 1, after.Bookings.UpcomingThisWeek);

        // Today's appointments, in time order, without the cancelled booking.
        var mine = after.TodaysAppointments!.Where(x => x.Customer.Id == b.Id).ToList();
        Assert.Equal([completed, stillBooked], mine.Select(x => x.Id));
        Assert.Equal(["Completed", "Booked"], mine.Select(x => x.Status));
        Assert.Equal(2, mine[0].Treatments.Count);

        // Customers: B moved on to later stages, so only A is still potential.
        Assert.Equal(before.Customers!.Potential + 1, after.Customers!.Potential);
        Assert.Equal(before.Customers.NewToday + 2, after.Customers.NewToday);
        Assert.Equal(before.Customers.FollowUpsDue + 1, after.Customers.FollowUpsDue);
        Assert.Equal(before.Customers.FollowUpsOverdue + 1, after.Customers.FollowUpsOverdue);

        var followUp = Assert.Single(after.FollowUps!, f => f.Id == a.Id);
        Assert.Equal(today.AddDays(-2), followUp.Date);
        Assert.Equal("interested", followUp.Stage.SystemKey);

        var interested = after.Stages!.Single(s => s.SystemKey == "interested");
        Assert.Equal(before.Stages!.Single(s => s.SystemKey == "interested").Count + 1, interested.Count);

        // Activity names the customer; the payment taken at completion is not a separate entry.
        var latest = after.Activity!.First();
        Assert.Equal("Booking Cancelled", latest.Action);
        Assert.Equal("Dash Bob", latest.Customer!.Name);
        Assert.Equal(cancelled, latest.BookingId);
        Assert.Contains(after.Activity!, x => x.Action == "Consultation Completed" && x.BookingId == completed);
        Assert.DoesNotContain(after.Activity!, x => x.Action == "Payment Recorded" && x.BookingId == completed);
        Assert.DoesNotContain(after.Activity!, x => x.Action == "Stage Changed" && x.Customer?.Id == b.Id);
    }

    [Fact]
    public async Task Settling_a_pending_payment_appears_in_activity()
    {
        var admin = await AdminAsync();
        var today = (await DataAsync<DashboardDto>(await admin.GetAsync("/api/dashboard"))).Today;
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var methods = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods"));
        var c = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            Customer("Dash Carol", "054 7100003", treatments[0].Id, null)));
        var booking = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(c.Id, null, today.AddDays(-3), new TimeOnly(12, 0), new TimeOnly(12, 30), [treatments[0].Id], null)));
        await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{booking.Id}/complete",
            new CompleteBookingRequest(90, "Pending", null, null, null, null)));
        await DataAsync<PaymentListItemDto>(await admin.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments",
            new RecordPaymentRequest(methods[0].Id, null)));

        var latest = (await DataAsync<DashboardDto>(await admin.GetAsync("/api/dashboard"))).Activity!.First();
        Assert.Equal("Payment Recorded", latest.Action);
        Assert.Equal("Dash Carol", latest.Customer!.Name);
    }

    [Fact]
    public async Task Dashboard_requires_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/dashboard")).StatusCode);
    }
}
