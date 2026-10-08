using System.Net;
using System.Net.Http.Json;
using DoctorCrm.Api.DTOs;

namespace DoctorCrm.Tests;

/// <summary>
/// Payments (spec §28): a consultation's amount, what was paid and the balance; part payments,
/// waiving, outstanding per customer, totals and export.
/// </summary>
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

    private sealed record Seed(CustomerDetailDto Customer, int FullId, int PartId, int UnpaidId, List<LookupItemDto> Methods);

    /// <summary>
    /// A customer with three completed consultations: 300 paid in full, 150 with 50 paid (owes
    /// 100), and 100 with nothing paid (owes 100).
    /// </summary>
    private async Task<Seed> SeedAsync(HttpClient admin)
    {
        var n = Interlocked.Increment(ref _seq);
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var methods = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods"));
        var customer = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest($"Payer {n}", $"054 {n + 3000000:0000000}", null, null, null, null, 1, null, [treatments[0].Id], null, null, null, null)));

        async Task<int> CompleteAsync(int hour, decimal charge, decimal paid)
        {
            var b = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
                new CreateBookingRequest(customer.Id, null, new DateOnly(2025, 5, 1).AddDays(n), new TimeOnly(hour, 0), new TimeOnly(hour, 30), [treatments[0].Id], null)));
            await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{b.Id}/complete",
                new CompleteBookingRequest(charge, paid, paid > 0 ? methods[0].Id : null, null, null, null)));
            return b.Id;
        }

        return new Seed(customer, await CompleteAsync(9, 300, 300), await CompleteAsync(10, 150, 50), await CompleteAsync(11, 100, 0), methods);
    }

    private static async Task<BookingDetailDto> BookingAsync(HttpClient admin, int id) =>
        await DataAsync<BookingDetailDto>(await admin.GetAsync($"/api/bookings/{id}"));

    [Fact]
    public async Task Completing_records_the_amount_what_was_paid_and_the_balance()
    {
        var admin = await AdminAsync();
        var s = await SeedAsync(admin);

        var full = await BookingAsync(admin, s.FullId);
        Assert.Equal((300m, 300m, 0m, "Paid"), (full.ConsultationCharge!.Value, full.AmountPaid, full.Balance, full.PaymentStatus));
        var part = await BookingAsync(admin, s.PartId);
        Assert.Equal((150m, 50m, 100m, "PartlyPaid"), (part.ConsultationCharge!.Value, part.AmountPaid, part.Balance, part.PaymentStatus));
        var unpaid = await BookingAsync(admin, s.UnpaidId);
        Assert.Equal((100m, 0m, 100m, "Unpaid"), (unpaid.ConsultationCharge!.Value, unpaid.AmountPaid, unpaid.Balance, unpaid.PaymentStatus));
        Assert.Empty(unpaid.Payments);

        var summary = await DataAsync<PaymentSummaryDto>(await admin.GetAsync($"/api/payments/summary?customerId={s.Customer.Id}"));
        Assert.Equal((350m, 200m, 2, 0m), (summary.Collected, summary.Outstanding, summary.OutstandingCount, summary.Waived));
        Assert.Equal("AED", summary.Currency);

        var owing = await DataAsync<PagedResult<BookingListItemDto>>(await admin.GetAsync($"/api/bookings?customerId={s.Customer.Id}&paymentStatus=Outstanding"));
        Assert.Equal([s.PartId, s.UnpaidId], owing.Items.Select(b => b.Id).Order());
    }

    [Fact]
    public async Task Paid_more_than_the_amount_or_without_a_method_is_refused()
    {
        var admin = await AdminAsync();
        var s = await SeedAsync(admin);
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var b = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(s.Customer.Id, null, new DateOnly(2025, 4, 1), new TimeOnly(9, 0), new TimeOnly(9, 30), [treatments[0].Id], null)));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/bookings/{b.Id}/complete",
            new CompleteBookingRequest(100, 150, s.Methods[0].Id, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/bookings/{b.Id}/complete",
            new CompleteBookingRequest(100, 40, null, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task Balance_can_be_paid_in_parts_until_nothing_is_owed()
    {
        var admin = await AdminAsync();
        var s = await SeedAsync(admin);

        var first = await DataAsync<PaymentListItemDto>(await admin.PostAsJsonAsync($"/api/bookings/{s.PartId}/payments",
            new RecordPaymentRequest(60, s.Methods[1].Id, null)));
        Assert.Equal(("Paid", 60m, 40m), (first.Status, first.Amount, first.BookingBalance));

        // More than the balance is refused; the rest settles it.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/bookings/{s.PartId}/payments",
            new RecordPaymentRequest(50, s.Methods[0].Id, null))).StatusCode);
        await DataAsync<PaymentListItemDto>(await admin.PostAsJsonAsync($"/api/bookings/{s.PartId}/payments",
            new RecordPaymentRequest(40, s.Methods[0].Id, null)));

        var settled = await BookingAsync(admin, s.PartId);
        Assert.Equal((150m, 0m, "Paid", 3), (settled.AmountPaid, settled.Balance, settled.PaymentStatus, settled.Payments.Count));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/bookings/{s.PartId}/payments",
            new RecordPaymentRequest(1, s.Methods[0].Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/bookings/{s.FullId}/payments",
            new RecordPaymentRequest(1, s.Methods[0].Id, null))).StatusCode);

        // Every entry is listed: 300, 50, 60 and 40.
        var entries = await DataAsync<PagedResult<PaymentListItemDto>>(await admin.GetAsync($"/api/payments?customerId={s.Customer.Id}"));
        Assert.Equal([40m, 50m, 60m, 300m], entries.Items.Select(p => p.Amount).Order());

        var summary = await DataAsync<PaymentSummaryDto>(await admin.GetAsync($"/api/payments/summary?customerId={s.Customer.Id}"));
        Assert.Equal((450m, 100m, 1), (summary.Collected, summary.Outstanding, summary.OutstandingCount));
    }

    [Fact]
    public async Task Waiving_writes_off_the_balance()
    {
        var admin = await AdminAsync();
        var s = await SeedAsync(admin);

        var waived = await DataAsync<PaymentListItemDto>(await admin.PostAsync($"/api/bookings/{s.UnpaidId}/payments/waive", null));
        Assert.Equal(("Waived", 100m, 0m), (waived.Status, waived.Amount, waived.BookingBalance));
        Assert.Equal("Waived", (await BookingAsync(admin, s.UnpaidId)).PaymentStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/bookings/{s.UnpaidId}/payments/waive", null)).StatusCode);

        // Part paid, rest waived: still counted as paid.
        await DataAsync<PaymentListItemDto>(await admin.PostAsync($"/api/bookings/{s.PartId}/payments/waive", null));
        Assert.Equal("Paid", (await BookingAsync(admin, s.PartId)).PaymentStatus);

        var summary = await DataAsync<PaymentSummaryDto>(await admin.GetAsync($"/api/payments/summary?customerId={s.Customer.Id}"));
        Assert.Equal((350m, 0m, 200m), (summary.Collected, summary.Outstanding, summary.Waived));
    }

    [Fact]
    public async Task Outstanding_is_listed_and_totalled_per_customer_for_payment_users_only()
    {
        var admin = await AdminAsync();
        var s = await SeedAsync(admin);

        var list = await DataAsync<PagedResult<OutstandingItemDto>>(await admin.GetAsync($"/api/payments/outstanding?customerId={s.Customer.Id}"));
        Assert.Equal([(s.PartId, 150m, 50m, 100m), (s.UnpaidId, 100m, 0m, 100m)],
            list.Items.Select(i => (i.BookingId, i.Charge, i.Paid, i.Balance)));

        Assert.Equal(200m, (await DataAsync<CustomerDetailDto>(await admin.GetAsync($"/api/customers/{s.Customer.Id}"))).Outstanding);
        var owing = await DataAsync<PagedResult<CustomerListItemDto>>(await admin.GetAsync($"/api/customers?hasOutstanding=true&search={Uri.EscapeDataString(s.Customer.Name)}"));
        Assert.Equal(200m, Assert.Single(owing.Items).Outstanding);

        // Staff don't see money.
        var roles = await DataAsync<List<RoleDto>>(await admin.GetAsync("/api/roles"));
        var username = $"staff_o{Interlocked.Increment(ref _seq)}";
        await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Staff", username, null, roles.Single(r => r.Name == "Staff").Id, "Staff-Pass-1")));
        var staff = await factory.SignInNewUserAsync(username, "Staff-Pass-1");
        Assert.Null((await DataAsync<CustomerDetailDto>(await staff.GetAsync($"/api/customers/{s.Customer.Id}"))).Outstanding);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/payments/outstanding")).StatusCode);
    }

    [Fact]
    public async Task Payment_needs_a_completed_consultation_an_amount_a_method_and_a_past_date()
    {
        var admin = await AdminAsync();
        var s = await SeedAsync(admin);
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var booked = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(s.Customer.Id, null, new DateOnly(2032, 1, 1), new TimeOnly(9, 0), new TimeOnly(9, 30), [treatments[0].Id], null)));

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/bookings/{booked.Id}/payments", new RecordPaymentRequest(10, s.Methods[0].Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/bookings/{s.UnpaidId}/payments", new RecordPaymentRequest(0, s.Methods[0].Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/bookings/{s.UnpaidId}/payments", new RecordPaymentRequest(10, 0, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/bookings/{s.UnpaidId}/payments",
            new RecordPaymentRequest(10, s.Methods[0].Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5)))).StatusCode);
    }

    [Fact]
    public async Task Export_has_the_filtered_rows_and_a_total()
    {
        var admin = await AdminAsync();
        var s = await SeedAsync(admin);

        var file = await admin.GetAsync($"/api/payments/export?customerId={s.Customer.Id}");
        using var workbook = new ClosedXML.Excel.XLWorkbook(await file.Content.ReadAsStreamAsync());
        var sheet = workbook.Worksheet(1);
        Assert.Contains("Amount (AED)", sheet.Row(1).CellsUsed().Select(c => c.GetString()));
        Assert.Equal("Total", sheet.Cell(4, 4).GetString()); // header + 2 payments (300, 50), then the total
        Assert.Equal(350, sheet.Cell(4, 5).GetDouble());
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
