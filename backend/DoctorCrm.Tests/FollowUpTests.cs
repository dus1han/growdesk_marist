using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoctorCrm.Tests;

/// <summary>
/// Follow-up tracking: new enquiries are due the same day, each follow-up schedules the next,
/// a booking stops them, a cancellation restarts them, and after the attempt limit staff are
/// prompted to mark the customer Lost.
/// </summary>
public class FollowUpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _seq;

    private static DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai")));

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

    private static string Number() => $"054 76{Interlocked.Increment(ref _seq):00000}";

    private static async Task<CustomerDetailDto> NewCustomerAsync(HttpClient admin, int? assignedTo = null)
    {
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        return await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest($"Lead {Interlocked.Increment(ref _seq)}", Number(), null, null, null, null, 1, assignedTo, [treatments[0].Id], null, null, null, null)));
    }

    private static async Task<CustomerDetailDto> CustomerAsync(HttpClient admin, int id) =>
        await DataAsync<CustomerDetailDto>(await admin.GetAsync($"/api/customers/{id}"));

    private static async Task<FollowUpLoggedDto> LogAsync(HttpClient admin, int id, string outcome, DateOnly? next = null) =>
        await DataAsync<FollowUpLoggedDto>(await admin.PostAsJsonAsync($"/api/customers/{id}/follow-ups", new LogFollowUpRequest(outcome, "Called", next)));

    [Fact]
    public async Task A_new_enquiry_is_due_today_and_each_follow_up_schedules_the_next()
    {
        var admin = await AdminAsync();
        var customer = await NewCustomerAsync(admin);
        Assert.Equal(Today, customer.NextFollowUpDate);

        var queue = await DataAsync<FollowUpQueueDto>(await admin.GetAsync("/api/follow-ups"));
        var item = Assert.Single(queue.Items, i => i.Id == customer.Id);
        Assert.True(item.IsNew);
        Assert.Equal(0, item.Attempts);
        var summary = await DataAsync<FollowUpSummaryDto>(await admin.GetAsync("/api/follow-ups/summary"));
        Assert.Contains(summary.NewEnquiries, e => e.Id == customer.Id);
        Assert.Equal(["10:00", "14:00", "17:00"], summary.ReminderTimes);

        // No answer: again in 2 days, status Follow-up, out of today's queue.
        var logged = await LogAsync(admin, customer.Id, "NoAnswer");
        Assert.Equal((Today.AddDays(2), 1, false), (logged.NextFollowUpDate, logged.Attempts, logged.SuggestLost));
        Assert.Equal("Follow-up", logged.Stage);
        Assert.DoesNotContain((await DataAsync<FollowUpQueueDto>(await admin.GetAsync("/api/follow-ups"))).Items, i => i.Id == customer.Id);

        var after = await CustomerAsync(admin, customer.Id);
        Assert.Equal((Today, 1), (after.LastContactDate, after.FollowUpAttempts));
        var activity = await DataAsync<List<ActivityDto>>(await admin.GetAsync($"/api/customers/{customer.Id}/activity"));
        Assert.Contains(activity, a => a.Action == "Follow-up Logged");

        // A chosen date wins; a date in the past is refused.
        Assert.Equal(Today.AddDays(5), (await LogAsync(admin, customer.Id, "CallBack", Today.AddDays(5))).NextFollowUpDate);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/follow-ups",
            new LogFollowUpRequest("CallBack", null, Today.AddDays(-1)))).StatusCode);
    }

    [Fact]
    public async Task Booking_stops_follow_ups_and_cancelling_restarts_them_the_next_day()
    {
        var admin = await AdminAsync();
        var customer = await NewCustomerAsync(admin);
        await LogAsync(admin, customer.Id, "NoAnswer");
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));

        var booking = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(customer.Id, null, Today.AddDays(10), new TimeOnly(9, 0), new TimeOnly(9, 30), [treatments[0].Id], null)));
        var booked = await CustomerAsync(admin, customer.Id);
        Assert.Equal(((DateOnly?)null, 0), (booked.NextFollowUpDate, booked.FollowUpAttempts));

        var reasons = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/cancellation-reasons"));
        await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{booking.Id}/cancel", new CancelBookingRequest(reasons[0].Id, null)));
        var cancelled = await CustomerAsync(admin, customer.Id);
        Assert.Equal((Today.AddDays(1), 0), (cancelled.NextFollowUpDate, cancelled.FollowUpAttempts));
    }

    [Fact]
    public async Task After_the_attempt_limit_staff_are_asked_to_mark_lost_and_not_interested_stops()
    {
        var admin = await AdminAsync();
        var original = await DataAsync<FollowUpSettingsDto>(await admin.GetAsync("/api/follow-ups/settings"));
        await DataAsync<FollowUpSettingsDto>(await admin.PutAsJsonAsync("/api/follow-ups/settings", original with { MaxAttempts = 2 }));
        try
        {
            var customer = await NewCustomerAsync(admin);
            Assert.False((await LogAsync(admin, customer.Id, "NoAnswer")).SuggestLost);
            Assert.True((await LogAsync(admin, customer.Id, "NoAnswer")).SuggestLost);

            var lost = await LogAsync(admin, customer.Id, "NotInterested");
            Assert.Equal(("Lost", (DateOnly?)null, false), (lost.Stage, lost.NextFollowUpDate, lost.SuggestLost));
        }
        finally
        {
            await DataAsync<FollowUpSettingsDto>(await admin.PutAsJsonAsync("/api/follow-ups/settings", original));
        }
    }

    [Fact]
    public async Task Next_treatments_saved_without_a_booking_are_listed_until_booked()
    {
        var admin = await AdminAsync();
        var customer = await NewCustomerAsync(admin);
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var methods = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods"));
        var visit = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(customer.Id, null, Today, new TimeOnly(8, 0), new TimeOnly(8, 30), [treatments[0].Id], null)));
        await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{visit.Id}/complete",
            new CompleteBookingRequest(100, 100, methods[0].Id, null, null, null)));

        // As saved before next treatments were booked automatically: a date and a treatment, no booking.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Bookings.SingleAsync(b => b.Id == visit.Id);
            row.NextTreatmentDate = Today.AddDays(3);
            row.NextTreatmentId = treatments[1].Id;
            await db.SaveChangesAsync();
        }

        var queue = await DataAsync<FollowUpQueueDto>(await admin.GetAsync("/api/follow-ups"));
        var item = Assert.Single(queue.NextTreatments, n => n.BookingId == visit.Id);
        Assert.Equal((customer.Id, Today.AddDays(3), treatments[1].Id), (item.CustomerId, item.Date, item.Treatment!.Id));
        Assert.False((await DataAsync<BookingDetailDto>(await admin.GetAsync($"/api/bookings/{visit.Id}"))).NextTreatmentBooked);

        // Booked from the list: linked, and no longer offered.
        var booked = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(customer.Id, null, Today.AddDays(3), new TimeOnly(11, 0), new TimeOnly(11, 30), [treatments[1].Id], null, visit.Id)));
        var linked = await DataAsync<BookingDetailDto>(await admin.GetAsync($"/api/bookings/{visit.Id}"));
        Assert.Equal(booked.Id, linked.NextBooking!.Id);
        Assert.DoesNotContain((await DataAsync<FollowUpQueueDto>(await admin.GetAsync("/api/follow-ups"))).NextTreatments, n => n.BookingId == visit.Id);
    }

    [Fact]
    public async Task Mine_shows_own_and_unassigned_customers_everyone_shows_all()
    {
        var admin = await AdminAsync();
        var roles = await DataAsync<List<RoleDto>>(await admin.GetAsync("/api/roles"));
        var other = await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Other Receptionist", $"fu_rec_{Interlocked.Increment(ref _seq)}", null, roles.Single(r => r.Name == "Receptionist").Id, "Temp-Pass-1")));
        var theirs = await NewCustomerAsync(admin, other.Id);
        var unassigned = await NewCustomerAsync(admin);

        var mine = await DataAsync<FollowUpQueueDto>(await admin.GetAsync("/api/follow-ups"));
        Assert.DoesNotContain(mine.Items, i => i.Id == theirs.Id);
        Assert.Contains(mine.Items, i => i.Id == unassigned.Id);
        var all = await DataAsync<FollowUpQueueDto>(await admin.GetAsync("/api/follow-ups?scope=all"));
        Assert.Contains(all.Items, i => i.Id == theirs.Id);
    }

    [Fact]
    public async Task Captured_enquiries_are_due_today_including_returning_ones()
    {
        var admin = await AdminAsync();
        var created = await DataAsync<CaptureClientCreatedDto>(await admin.PostAsJsonAsync("/api/admin/capture-clients",
            new CreateCaptureClientRequest($"FU PC {Interlocked.Increment(ref _seq)}")));
        var tool = factory.CreateClient();
        var token = await DataAsync<CaptureTokenDto>(await tool.PostAsJsonAsync("/api/capture/token",
            new CaptureTokenRequest(created.Client.ClientId, created.ClientSecret)));
        tool.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var number = Number();
        var lead = new CaptureCustomerRequest("Captured Lead", number, null, null, null, null, null, null, null, null, "whatsapp");
        var first = await DataAsync<CaptureCustomerResultDto>(await tool.PostAsJsonAsync("/api/capture/customers", lead));
        Assert.Equal(Today, (await CustomerAsync(admin, first.CustomerId)).NextFollowUpDate);

        // Not interested earlier; enquires again: back in today's follow-ups.
        await LogAsync(admin, first.CustomerId, "NotInterested");
        await DataAsync<CaptureCustomerResultDto>(await tool.PostAsJsonAsync("/api/capture/customers", lead));
        Assert.Equal(Today, (await CustomerAsync(admin, first.CustomerId)).NextFollowUpDate);
    }

    [Fact]
    public async Task Settings_are_validated_and_only_admins_change_them_staff_can_not_log()
    {
        var admin = await AdminAsync();
        var original = await DataAsync<FollowUpSettingsDto>(await admin.GetAsync("/api/follow-ups/settings"));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/follow-ups/settings", original with { ReminderTimes = ["25:99"] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/follow-ups/settings", original with { IntervalDays = 0 })).StatusCode);

        var roles = await DataAsync<List<RoleDto>>(await admin.GetAsync("/api/roles"));
        var username = $"fu_staff_{Interlocked.Increment(ref _seq)}";
        await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Staff", username, null, roles.Single(r => r.Name == "Staff").Id, "Temp-Pass-1")));
        var staff = await factory.SignInNewUserAsync(username, "Temp-Pass-1");
        var customer = await NewCustomerAsync(admin);

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/follow-ups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/follow-ups/settings", original)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/customers/{customer.Id}/follow-ups",
            new LogFollowUpRequest("NoAnswer", null, null))).StatusCode);
    }
}
