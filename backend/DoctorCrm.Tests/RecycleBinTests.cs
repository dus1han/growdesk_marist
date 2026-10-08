using System.Net;
using System.Net.Http.Json;
using DoctorCrm.Api.DTOs;

namespace DoctorCrm.Tests;

/// <summary>
/// Deleting to the recycle bin, lowest level first (payment → booking → customer; admin list
/// items once unused), and restoring in the reverse order.
/// </summary>
public class RecycleBinTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    private static async Task<string?> MessageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<object>>())!.Message;

    private static string Number() => $"054 75{Interlocked.Increment(ref _seq):00000}";

    private async Task<CustomerDetailDto> CustomerAsync(HttpClient admin, string? number = null)
    {
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        return await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest($"Binned {Interlocked.Increment(ref _seq)}", number ?? Number(), null, null, null, null, 1, null, [treatments[0].Id], null, null, null, null)));
    }

    /// <summary>A customer with one completed consultation: 300, of which 100 paid.</summary>
    private async Task<(CustomerDetailDto Customer, int BookingId, int PaymentId)> SeedAsync(HttpClient admin)
    {
        var customer = await CustomerAsync(admin);
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var methods = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods"));
        var booking = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(customer.Id, null, new DateOnly(2025, 3, 1).AddDays(_seq % 300), new TimeOnly(9, 0), new TimeOnly(9, 30), [treatments[0].Id], null)));
        var done = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{booking.Id}/complete",
            new CompleteBookingRequest(300, 100, methods[0].Id, null, null, null)));
        return (customer, booking.Id, Assert.Single(done.Payments).Id);
    }

    [Fact]
    public async Task Deleting_goes_lowest_level_first_and_restoring_the_other_way()
    {
        var admin = await AdminAsync();
        var (customer, bookingId, paymentId) = await SeedAsync(admin);

        // Higher levels can't go while lower ones are there.
        var tooSoon = await admin.DeleteAsync($"/api/customers/{customer.Id}");
        Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);
        Assert.Contains("1 booking", await MessageAsync(tooSoon));
        var bookingTooSoon = await admin.DeleteAsync($"/api/bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.Conflict, bookingTooSoon.StatusCode);
        Assert.Contains("1 payment", await MessageAsync(bookingTooSoon));

        // Payment, then booking, then customer.
        await DataAsync<object>(await admin.DeleteAsync($"/api/payments/{paymentId}"));
        var owing = await DataAsync<BookingDetailDto>(await admin.GetAsync($"/api/bookings/{bookingId}"));
        Assert.Equal((0m, 300m), (owing.AmountPaid, owing.Balance));
        Assert.Empty(owing.Payments);
        await DataAsync<object>(await admin.DeleteAsync($"/api/bookings/{bookingId}"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/bookings/{bookingId}")).StatusCode);
        await DataAsync<object>(await admin.DeleteAsync($"/api/customers/{customer.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/customers/{customer.Id}")).StatusCode);
        var search = await DataAsync<PagedResult<CustomerListItemDto>>(await admin.GetAsync($"/api/customers?search={Uri.EscapeDataString(customer.Name)}"));
        Assert.Empty(search.Items);

        // All three are in the bin.
        var bin = await DataAsync<PagedResult<RecycleBinItemDto>>(await admin.GetAsync($"/api/recycle-bin?search={Uri.EscapeDataString(customer.Name)}"));
        Assert.Equal(["customer", "booking", "payment"], bin.Items.Select(i => i.Type).Order().Reverse().OrderBy(t => t switch { "customer" => 0, "booking" => 1, _ => 2 }));
        Assert.All(bin.Items, i => Assert.NotNull(i.DeletedBy));

        // Back in reverse order: a booking needs its customer, a payment its booking.
        var orphan = await admin.PostAsync($"/api/recycle-bin/booking/{bookingId}/restore", null);
        Assert.Equal(HttpStatusCode.Conflict, orphan.StatusCode);
        Assert.Contains("customer first", await MessageAsync(orphan));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/recycle-bin/payment/{paymentId}/restore", null)).StatusCode);

        await DataAsync<object>(await admin.PostAsync($"/api/recycle-bin/customer/{customer.Id}/restore", null));
        await DataAsync<object>(await admin.PostAsync($"/api/recycle-bin/booking/{bookingId}/restore", null));
        await DataAsync<object>(await admin.PostAsync($"/api/recycle-bin/payment/{paymentId}/restore", null));

        var restored = await DataAsync<BookingDetailDto>(await admin.GetAsync($"/api/bookings/{bookingId}"));
        Assert.Equal((100m, 200m), (restored.AmountPaid, restored.Balance));
        Assert.Equal(200m, (await DataAsync<CustomerDetailDto>(await admin.GetAsync($"/api/customers/{customer.Id}"))).Outstanding);

        // The audit log still names them, and finds them by name.
        var log = await DataAsync<PagedResult<AuditLogDto>>(await admin.GetAsync($"/api/audit-logs?search={Uri.EscapeDataString(customer.Name)}"));
        Assert.Contains(log.Items, e => e.Action == "Customer Deleted" && e.Subject == customer.Name);
        Assert.Contains(log.Items, e => e.Action == "Booking Restored");
    }

    [Fact]
    public async Task A_payment_paid_again_since_can_not_be_restored_twice_over()
    {
        var admin = await AdminAsync();
        var (_, bookingId, paymentId) = await SeedAsync(admin);
        var methods = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods"));

        await DataAsync<object>(await admin.DeleteAsync($"/api/payments/{paymentId}"));
        await DataAsync<PaymentListItemDto>(await admin.PostAsJsonAsync($"/api/bookings/{bookingId}/payments", new RecordPaymentRequest(250, methods[0].Id, null)));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/recycle-bin/payment/{paymentId}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task A_deleted_customers_number_can_be_used_again_and_then_blocks_restoring()
    {
        var admin = await AdminAsync();
        var number = Number();
        var first = await CustomerAsync(admin, number);
        await DataAsync<object>(await admin.DeleteAsync($"/api/customers/{first.Id}"));

        var again = await CustomerAsync(admin, number);
        Assert.NotEqual(first.Id, again.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/recycle-bin/customer/{first.Id}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task List_items_are_deleted_only_when_unused_and_built_ins_never()
    {
        var admin = await AdminAsync();
        await SeedAsync(admin);

        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments?includeInactive=true"));
        var used = await admin.DeleteAsync($"/api/treatments/{treatments[0].Id}");
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
        Assert.Contains("deactivate it instead", await MessageAsync(used));

        var stages = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/stages?includeInactive=true"));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/stages/{stages.First(s => s.SystemKey != null).Id}")).StatusCode);

        // Unused: deleted, the name is free again, and the old one can't come back over it.
        var name = $"Flyer {Interlocked.Increment(ref _seq)}";
        var source = await DataAsync<LookupItemDto>(await admin.PostAsJsonAsync("/api/lead-sources", new SaveLookupItemRequest(name, null, null)));
        await DataAsync<object>(await admin.DeleteAsync($"/api/lead-sources/{source.Id}"));
        Assert.DoesNotContain(await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/lead-sources?includeInactive=true")), s => s.Id == source.Id);
        var twin = await DataAsync<LookupItemDto>(await admin.PostAsJsonAsync("/api/lead-sources", new SaveLookupItemRequest(name, null, null)));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/recycle-bin/lead-source/{source.Id}/restore", null)).StatusCode);
        await DataAsync<object>(await admin.DeleteAsync($"/api/lead-sources/{twin.Id}"));
        await DataAsync<object>(await admin.PostAsync($"/api/recycle-bin/lead-source/{source.Id}/restore", null));
    }

    [Fact]
    public async Task Only_admins_delete_and_see_the_recycle_bin()
    {
        var admin = await AdminAsync();
        var (customer, bookingId, paymentId) = await SeedAsync(admin);
        var roles = await DataAsync<List<RoleDto>>(await admin.GetAsync("/api/roles"));

        foreach (var role in new[] { "Doctor", "Receptionist" })
        {
            var username = $"bin_{role.ToLowerInvariant()}_{Interlocked.Increment(ref _seq)}";
            await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
                new CreateUserRequest(role, username, null, roles.Single(r => r.Name == role).Id, "Temp-Pass-1")));
            var user = await factory.SignInNewUserAsync(username, "Temp-Pass-1");

            Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/payments/{paymentId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/bookings/{bookingId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/customers/{customer.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/recycle-bin")).StatusCode);
        }
    }
}
