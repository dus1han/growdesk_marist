using System.Net;
using System.Net.Http.Json;
using DoctorCrm.Api.DTOs;

namespace DoctorCrm.Tests;

/// <summary>Payments (spec §28): current state, history, settling pending payments, totals, export.</summary>
public class PaymentIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _seq;

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

    /// <summary>A customer with three completed consultations: paid 300, pending 150, waived 100.</summary>
    private async Task<(CustomerDetailDto Customer, int PaidId, int PendingId, int WaivedId, List<LookupItemDto> Methods)> SeedAsync(HttpClient admin)
    {
        var n = Interlocked.Increment(ref _seq);
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var methods = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods"));
        var customer = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest($"Payer {n}", $"054 {n + 3000000:0000000}", null, null, null, null, 1, null, [treatments[0].Id], null, null, null, null)));

        async Task<int> CompleteAsync(int hour, decimal charge, string status, int? method)
        {
            var b = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
                new CreateBookingRequest(customer.Id, null, new DateOnly(2025, 5, 1).AddDays(n), new TimeOnly(hour, 0), new TimeOnly(hour, 30), [treatments[0].Id], null)));
            await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{b.Id}/complete",
                new CompleteBookingRequest(charge, status, method, null, null, null)));
            return b.Id;
        }

        var paid = await CompleteAsync(9, 300, "Paid", methods[0].Id);
        var pending = await CompleteAsync(10, 150, "Pending", null);
        var waived = await CompleteAsync(11, 100, "Waived", null);
        return (customer, paid, pending, waived, methods);
    }

    [Fact]
    public async Task Summary_splits_collected_outstanding_and_waived()
    {
        var admin = await AdminAsync();
        var (customer, _, _, _, _) = await SeedAsync(admin);

        var summary = await DataAsync<PaymentSummaryDto>(await admin.GetAsync($"/api/payments/summary?customerId={customer.Id}"));
        Assert.Equal(300, summary.Collected);
        Assert.Equal(150, summary.Outstanding);
        Assert.Equal(100, summary.Waived);
        Assert.Equal("AED", summary.Currency);

        var pendingOnly = await DataAsync<PagedResult<PaymentListItemDto>>(await admin.GetAsync($"/api/payments?customerId={customer.Id}&status=Pending"));
        Assert.Equal(150, Assert.Single(pendingOnly.Items).Amount);
    }

    [Fact]
    public async Task Settling_a_pending_payment_adds_an_entry_and_keeps_the_history()
    {
        var admin = await AdminAsync();
        var (customer, paidId, pendingId, _, methods) = await SeedAsync(admin);

        var settled = await DataAsync<PaymentListItemDto>(await admin.PostAsJsonAsync($"/api/bookings/{pendingId}/payments",
            new RecordPaymentRequest(methods[1].Id, null)));
        Assert.Equal("Paid", settled.Status);
        Assert.Equal(150, settled.Amount);
        Assert.True(settled.IsCurrent);

        // Current view: three bookings, all settled or waived.
        var current = await DataAsync<PagedResult<PaymentListItemDto>>(await admin.GetAsync($"/api/payments?customerId={customer.Id}"));
        Assert.Equal(3, current.TotalCount);
        Assert.DoesNotContain(current.Items, p => p.Status == "Pending");

        // Full history still has the pending entry.
        var history = await DataAsync<PagedResult<PaymentListItemDto>>(await admin.GetAsync($"/api/payments?customerId={customer.Id}&currentOnly=false"));
        Assert.Equal(4, history.TotalCount);
        Assert.Contains(history.Items, p => p.Status == "Pending" && !p.IsCurrent);

        var summary = await DataAsync<PaymentSummaryDto>(await admin.GetAsync($"/api/payments/summary?customerId={customer.Id}"));
        Assert.Equal(450, summary.Collected);
        Assert.Equal(0, summary.Outstanding);

        // Already paid / not pending → refused.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/bookings/{pendingId}/payments", new RecordPaymentRequest(methods[0].Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/bookings/{paidId}/payments", new RecordPaymentRequest(methods[0].Id, null))).StatusCode);
    }

    [Fact]
    public async Task Payment_needs_a_completed_consultation_a_method_and_a_past_date()
    {
        var admin = await AdminAsync();
        var (customer, _, pendingId, _, methods) = await SeedAsync(admin);
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var booked = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(customer.Id, null, new DateOnly(2032, 1, 1), new TimeOnly(9, 0), new TimeOnly(9, 30), [treatments[0].Id], null)));

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/bookings/{booked.Id}/payments", new RecordPaymentRequest(methods[0].Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/bookings/{pendingId}/payments", new RecordPaymentRequest(0, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/bookings/{pendingId}/payments",
            new RecordPaymentRequest(methods[0].Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5)))).StatusCode);
    }

    [Fact]
    public async Task Export_has_the_filtered_rows_and_a_total()
    {
        var admin = await AdminAsync();
        var (customer, _, _, _, _) = await SeedAsync(admin);

        var file = await admin.GetAsync($"/api/payments/export?customerId={customer.Id}");
        using var workbook = new ClosedXML.Excel.XLWorkbook(await file.Content.ReadAsStreamAsync());
        var sheet = workbook.Worksheet(1);
        Assert.Contains("Amount (AED)", sheet.Row(1).CellsUsed().Select(c => c.GetString()));
        Assert.Equal("Total", sheet.Cell(5, 4).GetString()); // header + 3 rows, then the total
        Assert.Equal(550, sheet.Cell(5, 5).GetDouble());
    }

    [Fact]
    public async Task Staff_cannot_see_payments()
    {
        var admin = await AdminAsync();
        var roles = await DataAsync<List<RoleDto>>(await admin.GetAsync("/api/roles"));
        var username = $"staff_p{Interlocked.Increment(ref _seq)}";
        await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Staff", username, null, roles.Single(r => r.Name == "Staff").Id, "Staff-Pass-1")));
        var staff = await factory.SignInNewUserAsync(username, "Staff-Pass-1");

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/payments/summary")).StatusCode);
    }
}
