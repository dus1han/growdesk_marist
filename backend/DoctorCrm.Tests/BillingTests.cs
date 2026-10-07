using System.Net;
using System.Net.Http.Json;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DoctorCrm.Tests;

/// <summary>The API with Stripe replaced by a fake and a clock the test moves. Billing is switched
/// on by a platform owner through Stripe Settings, as in production.</summary>
public sealed class BillingApiFactory : ApiFactory
{
    public const string CustomerId = "cus_test";
    public FakeBillingGateway Stripe { get; } = new();
    public ManualTime Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Billing:WorkerEnabled", "false");
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IBillingGateway>(Stripe);
            s.AddSingleton<TimeProvider>(Time);
        });
    }

    /// <summary>Runs the background job's check once, as it would every few minutes.</summary>
    public async Task RunScheduledChecksAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingService>().RunScheduledChecksAsync(default);
    }
}

public sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

public sealed class FakeBillingGateway : IBillingGateway
{
    public static readonly PlanInfo Plan = new("GrowDesk", 50000, "aed", "month", 1);

    public const string GoodKey = "sk_test_51GoodKeyForTests0000abcd";
    public static readonly BillingSnapshot None = new(null, null, false, null, null, null, null, null, null, null);

    /// <summary>The original billing date of the test subscription: the 1st of the month, 08:00.</summary>
    public static readonly DateTime Anchor = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    public BillingSnapshot Snapshot { get; set; } = None;
    public bool NextPaySucceeds { get; set; }
    public int PayCalls { get; private set; }
    public int FetchCalls { get; private set; }
    public DateTime? LastCheckoutAnchor { get; private set; }

    public static BillingSnapshot Active(DateTime periodEnd, UnpaidInvoice? unpaid = null) =>
        new("sub_test", unpaid is null ? "active" : "past_due", false, periodEnd, null, Anchor, Plan, "visa", "4242", unpaid);

    public Task<PlanInfo> ValidateAsync(string secretKey, string priceId, CancellationToken ct) =>
        secretKey != GoodKey ? throw new BillingConfigException("Stripe didn't accept this secret key.", "secretKey")
        : priceId != "price_test" ? throw new BillingConfigException("No price with this ID in this Stripe account.", "priceId")
        : Task.FromResult(Plan);

    public Task<string> CreateCustomerAsync(string name, string? email, CancellationToken ct) =>
        Task.FromResult(BillingApiFactory.CustomerId);

    public Task<BillingSnapshot> FetchAsync(string customerId, CancellationToken ct)
    {
        FetchCalls++;
        return Task.FromResult(Snapshot);
    }

    public Task<PlanInfo> GetPlanAsync(string priceId, CancellationToken ct) => Task.FromResult(Plan);

    public Task<string> CreateSubscriptionCheckoutAsync(string customerId, string priceId, DateTime? billingCycleAnchor,
        string successUrl, string cancelUrl, CancellationToken ct)
    {
        LastCheckoutAnchor = billingCycleAnchor;
        return Task.FromResult($"https://checkout.stripe.test/{customerId}?success={Uri.EscapeDataString(successUrl)}");
    }

    public Task<string> CreatePortalAsync(string customerId, string returnUrl, CancellationToken ct) =>
        Task.FromResult("https://billing.stripe.test/portal");

    public Task<PayResult> PayInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        PayCalls++;
        if (!NextPaySucceeds) return Task.FromResult(new PayResult(false, "Your card was declined."));
        Snapshot = Snapshot with { Status = "active", Unpaid = null };
        return Task.FromResult(new PayResult(true, null));
    }

    public Task<IReadOnlyList<BillingInvoice>> ListInvoicesAsync(string customerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BillingInvoice>>(
        [
            new("in_2", "GD-0002", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), null, null, 50000, 0, "aed", "open", null, 2, "https://invoice.stripe.test/in_2", null),
            new("in_1", "GD-0001", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), null, null, 50000, 50000, "aed", "paid",
                new DateTime(2026, 9, 1, 0, 5, 0, DateTimeKind.Utc), 1, "https://invoice.stripe.test/in_1", "https://invoice.stripe.test/in_1.pdf"),
        ]);

    public WebhookEvent ParseWebhook(string json, string signature, string secret) =>
        signature == "valid" ? new WebhookEvent("invoice.paid", json) : throw new BillingWebhookException("bad signature");
}

/// <summary>Grace period, last automatic charge, blocking and Pay now, end to end.</summary>
public class BillingTests(BillingApiFactory factory) : IClassFixture<BillingApiFactory>
{
    private static int _seq;

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateCookieClient();
        client.DefaultRequestHeaders.Add("Origin", "https://crm.example.test");
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminUsername, ApiFactory.AdminPassword))).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> DataAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.True(body!.Success, body.Message);
        return body.Data!;
    }

    private DateTime Now => factory.Time.Now.UtcDateTime;

    private static SaveBillingSettingsRequest Settings(bool enabled = true, string? key = FakeBillingGateway.GoodKey) =>
        new(enabled, key, "whsec_test", false, "price_test", 3, null);

    /// <summary>Subscribed through Checkout and paid up, whatever earlier tests left behind.</summary>
    private async Task<HttpClient> SubscribedAdminAsync()
    {
        var admin = await AdminAsync();
        await DataAsync<BillingSettingsDto>(await admin.PutAsJsonAsync("/api/billing/settings", Settings()));
        factory.Stripe.Snapshot = FakeBillingGateway.None;
        await admin.PostAsync("/api/billing/sync", null);
        var notice = await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"));
        if (notice.State != nameof(BillingState.Active))
        {
            var checkout = await DataAsync<BillingRedirectDto>(await admin.PostAsync("/api/billing/subscribe", null));
            Assert.StartsWith("https://checkout.stripe.test/cus_test", checkout.Url);
            Assert.Contains(Uri.EscapeDataString("https://crm.example.test/administration/subscription?checkout=success"), checkout.Url);
        }

        // Back from Checkout: Stripe now has an active subscription.
        factory.Stripe.Snapshot = FakeBillingGateway.Active(Now.AddDays(30));
        var overview = await DataAsync<BillingOverviewDto>(await admin.PostAsync("/api/billing/sync", null));
        Assert.Equal(nameof(BillingState.Active), overview.Notice.State);
        Assert.Equal("4242", overview.CardLast4);
        Assert.Equal(50000, overview.Plan!.Amount);
        return admin;
    }

    private Task<HttpClient> StaffAsync() => NewUserAsync("Staff");

    /// <summary>A user created in the app: with role Admin, a clinic admin who is not a platform owner.</summary>
    private async Task<HttpClient> NewUserAsync(string role)
    {
        var admin = await AdminAsync();
        var roles = await DataAsync<List<RoleDto>>(await admin.GetAsync("/api/roles"));
        var username = $"bill_{role.ToLowerInvariant()}_{Interlocked.Increment(ref _seq)}";
        await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest($"Billing {role}", username, null, roles.Single(r => r.Name == role).Id, "Temp-Pass-1")));
        var client = await factory.SignInNewUserAsync(username, "Temp-Pass-1");
        client.DefaultRequestHeaders.Add("Origin", "https://crm.example.test");
        return client;
    }

    private void RenewalFails() =>
        factory.Stripe.Snapshot = FakeBillingGateway.Active(Now.AddDays(30),
            new UnpaidInvoice($"in_{Guid.NewGuid():N}", 50000, "aed", Now, "https://invoice.stripe.test/unpaid"));

    [Fact]
    public async Task Missed_payment_counts_down_retries_on_day_three_then_blocks_until_paid()
    {
        var admin = await SubscribedAdminAsync();
        var staff = await StaffAsync();

        // Renewal declined: a 3-day countdown, and the system keeps working.
        RenewalFails();
        await factory.RunScheduledChecksAsync(); // last sync is stale only after 30 minutes...
        factory.Time.Advance(TimeSpan.FromMinutes(31));
        await factory.RunScheduledChecksAsync(); // ...so this one picks the failed renewal up
        var notice = await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"));
        Assert.Equal(nameof(BillingState.PaymentDue), notice.State);
        Assert.Equal(nameof(BillingDueReason.PaymentFailed), notice.Reason);
        Assert.Equal(3, notice.DaysLeft);
        Assert.Equal(50000, notice.AmountDue);
        Assert.True(notice.CanManage);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/customers")).StatusCode);

        // Staff see the countdown but no amounts, and cannot pay.
        var staffNotice = await DataAsync<BillingNoticeDto>(await staff.GetAsync("/api/billing/notice"));
        Assert.Equal(3, staffNotice.DaysLeft);
        Assert.Null(staffNotice.AmountDue);
        Assert.False(staffNotice.CanManage);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync("/api/billing/pay-now", null)).StatusCode);

        factory.Time.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, (await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"))).DaysLeft);

        // Day 3: not blocked before the last automatic charge has been tried.
        factory.Time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(nameof(BillingState.FinalAttempt), (await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"))).State);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/customers")).StatusCode);

        // The last charge is declined: blocked.
        var paysBefore = factory.Stripe.PayCalls;
        factory.Stripe.NextPaySucceeds = false;
        await factory.RunScheduledChecksAsync();
        Assert.Equal(paysBefore + 1, factory.Stripe.PayCalls);

        var blocked = await staff.GetAsync("/api/customers");
        Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);
        var body = await blocked.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Contains(body!.Errors!, e => e.Field == "subscription_required");
        Assert.Equal(HttpStatusCode.PaymentRequired, (await admin.GetAsync("/api/dashboard")).StatusCode);

        // Still possible while blocked: the session, the notice, signing in, and paying.
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/auth/me")).StatusCode);
        Assert.True((await DataAsync<BillingNoticeDto>(await staff.GetAsync("/api/billing/notice"))).Blocked);
        var relogin = await AdminAsync();
        var overview = await DataAsync<BillingOverviewDto>(await relogin.GetAsync("/api/billing"));
        Assert.True(overview.Notice.Blocked);
        Assert.Equal("Your card was declined.", overview.FinalRetryError);

        // Only one last charge: further checks don't keep charging the card.
        await factory.RunScheduledChecksAsync();
        Assert.Equal(paysBefore + 1, factory.Stripe.PayCalls);

        // Pay now with the declined card: sent to Stripe's invoice page to use another card.
        var declined = await DataAsync<PayNowResultDto>(await admin.PostAsync("/api/billing/pay-now", null));
        Assert.False(declined.Paid);
        Assert.Equal("https://invoice.stripe.test/unpaid", declined.RedirectUrl);
        Assert.Contains("declined", declined.Message);

        // Pay now succeeds: unblocked straight away.
        factory.Stripe.NextPaySucceeds = true;
        var paid = await DataAsync<PayNowResultDto>(await admin.PostAsync("/api/billing/pay-now", null));
        Assert.True(paid.Paid);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/customers")).StatusCode);
        var after = await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"));
        Assert.Equal(nameof(BillingState.Active), after.State);
        Assert.False(after.Blocked);

        foreach (var action in new[] { "Subscription Payment Failed", "Subscription Blocked", "Subscription Paid" })
        {
            var audit = await DataAsync<PagedResult<AuditLogDto>>(await admin.GetAsync($"/api/audit-logs?action={Uri.EscapeDataString(action)}"));
            Assert.NotEmpty(audit.Items);
        }
    }

    [Fact]
    public async Task Last_automatic_charge_on_day_three_that_succeeds_never_blocks()
    {
        var admin = await SubscribedAdminAsync();
        RenewalFails();
        await admin.PostAsync("/api/billing/sync", null);

        factory.Time.Advance(TimeSpan.FromDays(3).Add(TimeSpan.FromMinutes(1)));
        factory.Stripe.NextPaySucceeds = true;
        await factory.RunScheduledChecksAsync();

        var notice = await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"));
        Assert.Equal(nameof(BillingState.Active), notice.State);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task Payment_made_on_stripe_during_grace_period_clears_the_notice_on_sync()
    {
        var admin = await SubscribedAdminAsync();
        RenewalFails();
        await admin.PostAsync("/api/billing/sync", null);
        Assert.Equal(nameof(BillingState.PaymentDue), (await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"))).State);

        // Stripe's own retry (or the hosted invoice page) collected it.
        factory.Stripe.Snapshot = FakeBillingGateway.Active(Now.AddDays(30));
        await admin.PostAsync("/api/billing/sync", null);
        Assert.Equal(nameof(BillingState.Active), (await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"))).State);
    }

    [Fact]
    public async Task Webhook_needs_a_valid_signature_and_only_syncs_for_this_clinic()
    {
        await SubscribedAdminAsync();
        var anonymous = factory.CreateClient();

        var bad = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(BillingApiFactory.CustomerId) };
        bad.Headers.Add("Stripe-Signature", "forged");
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.SendAsync(bad)).StatusCode);

        // The fake reads the customer id from the body.
        var fetches = factory.Stripe.FetchCalls;
        var other = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent("cus_other_clinic") };
        other.Headers.Add("Stripe-Signature", "valid");
        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(other)).StatusCode);
        Assert.Equal(fetches, factory.Stripe.FetchCalls);

        var ours = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(BillingApiFactory.CustomerId) };
        ours.Headers.Add("Stripe-Signature", "valid");
        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(ours)).StatusCode);
        Assert.Equal(fetches + 1, factory.Stripe.FetchCalls);
    }

    [Fact]
    public async Task Unpaid_on_day_three_stays_blocked_when_stripe_cancels_and_resubscribes_on_the_original_date()
    {
        var admin = await SubscribedAdminAsync();
        RenewalFails();
        await admin.PostAsync("/api/billing/sync", null);
        factory.Time.Advance(TimeSpan.FromDays(3).Add(TimeSpan.FromMinutes(1)));
        factory.Stripe.NextPaySucceeds = false;
        await factory.RunScheduledChecksAsync();
        Assert.True((await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"))).Blocked);

        // Later Stripe gives up: cancels the subscription and voids the invoice. Still blocked, no
        // fresh grace period.
        factory.Stripe.Snapshot = FakeBillingGateway.Active(Now.AddDays(27)) with { Status = "canceled", EndedAt = Now };
        await admin.PostAsync("/api/billing/sync", null);
        var notice = await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"));
        Assert.True(notice.Blocked);
        Assert.Equal(nameof(BillingDueReason.Ended), notice.Reason);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await admin.GetAsync("/api/customers")).StatusCode);

        // Pay now subscribes again, and renewals stay on the original date (the 1st, 08:00).
        var payNow = await DataAsync<PayNowResultDto>(await admin.PostAsync("/api/billing/pay-now", null));
        Assert.False(payNow.Paid);
        Assert.StartsWith("https://checkout.stripe.test/", payNow.RedirectUrl);
        var anchor = factory.Stripe.LastCheckoutAnchor!.Value;
        Assert.Equal((1, 8), (anchor.Day, anchor.Hour));
        Assert.True(anchor > Now && anchor <= Now.AddMonths(1).AddHours(1));

        // Back from Checkout: running again and unblocked.
        factory.Stripe.Snapshot = FakeBillingGateway.Active(anchor);
        await admin.PostAsync("/api/billing/sync", null);
        Assert.Equal(nameof(BillingState.Active), (await DataAsync<BillingNoticeDto>(await admin.GetAsync("/api/billing/notice"))).State);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task Stripe_settings_are_for_platform_owners_and_never_return_the_secret()
    {
        var owner = await AdminAsync();
        var me = await DataAsync<SessionDto>(await owner.GetAsync("/api/auth/me"));
        Assert.Contains("platform.billing", me.User.Permissions);

        var response = await owner.PutAsJsonAsync("/api/billing/settings", Settings());
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeBillingGateway.GoodKey, raw);
        Assert.DoesNotContain("whsec_test", raw);
        var saved = await DataAsync<BillingSettingsDto>(response);
        Assert.True(saved.Enabled);
        Assert.True(saved.HasSecretKey);
        Assert.True(saved.HasWebhookSecret);
        Assert.Equal("sk_test_…abcd", saved.SecretKeyHint);
        Assert.Equal("test", saved.Mode);
        Assert.Equal(50000, saved.Plan!.Amount);
        Assert.Equal("https://crm.example.test/api/billing/webhook", saved.WebhookUrl);

        // A key Stripe rejects is never saved; leaving the key empty keeps the saved one.
        var rejected = await owner.PutAsJsonAsync("/api/billing/settings", Settings(key: "sk_test_51RejectedKey000000wxyz"));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var publishable = await owner.PutAsJsonAsync("/api/billing/settings", Settings(key: "pk_test_51Publishable"));
        Assert.Equal(HttpStatusCode.BadRequest, publishable.StatusCode);
        var kept = await DataAsync<BillingSettingsDto>(await owner.PutAsJsonAsync("/api/billing/settings", Settings(key: null)));
        Assert.Equal("sk_test_…abcd", kept.SecretKeyHint);

        // A clinic admin pays and sees the subscription, but can't see or change Stripe Settings.
        var clinicAdmin = await NewUserAsync("Admin");
        Assert.DoesNotContain("platform.billing", (await DataAsync<SessionDto>(await clinicAdmin.GetAsync("/api/auth/me"))).User.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await clinicAdmin.GetAsync("/api/billing/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clinicAdmin.PutAsJsonAsync("/api/billing/settings", Settings(enabled: false))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clinicAdmin.GetAsync("/api/billing")).StatusCode);

        // The owner's switch: off means no reminders and no blocking.
        await DataAsync<BillingSettingsDto>(await owner.PutAsJsonAsync("/api/billing/settings", Settings(enabled: false, key: null)));
        Assert.Equal(nameof(BillingState.Disabled), (await DataAsync<BillingNoticeDto>(await owner.GetAsync("/api/billing/notice"))).State);
    }

    [Fact]
    public async Task Clinic_admins_cannot_change_a_platform_owner_account()
    {
        var owner = await AdminAsync();
        var clinicAdmin = await NewUserAsync("Admin");
        var users = await DataAsync<List<UserDto>>(await clinicAdmin.GetAsync("/api/users"));
        var ownerUser = users.Single(u => u.Username == ApiFactory.AdminUsername);
        Assert.True(ownerUser.IsPlatformOwner);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await clinicAdmin.PostAsJsonAsync($"/api/users/{ownerUser.Id}/reset-password", new ResetPasswordRequest("Takeover-Pass-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await clinicAdmin.PatchAsJsonAsync($"/api/users/{ownerUser.Id}/active", new SetActiveRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await clinicAdmin.PutAsJsonAsync($"/api/users/{ownerUser.Id}", new UpdateUserRequest("Renamed", ownerUser.Username, null, ownerUser.RoleId!.Value))).StatusCode);

        // The owner still signs in with their own password, and can manage clinic admins.
        (await factory.CreateCookieClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFactory.AdminUsername, ApiFactory.AdminPassword))).EnsureSuccessStatusCode();
        var clinicAdminUser = (await DataAsync<List<UserDto>>(await owner.GetAsync("/api/users"))).First(u => u.RoleName == "Admin" && !u.IsPlatformOwner);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsJsonAsync($"/api/users/{clinicAdminUser.Id}/reset-password", new ResetPasswordRequest("Reset-Pass-1"))).StatusCode);
    }

    [Fact]
    public async Task Payment_history_lists_stripe_invoices_for_admins_only()
    {
        var admin = await SubscribedAdminAsync();
        var invoices = await DataAsync<List<BillingInvoice>>(await admin.GetAsync("/api/billing/invoices"));
        Assert.Equal(["GD-0002", "GD-0001"], invoices.Select(i => i.Number));
        Assert.Equal("paid", invoices[1].Status);

        var staff = await StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/billing/invoices")).StatusCode);
    }
}

/// <summary>Without Stripe configured, billing never gets in the way.</summary>
public class BillingDisabledTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Billing_is_off_without_stripe_settings()
    {
        var client = factory.CreateCookieClient();
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminUsername, ApiFactory.AdminPassword))).EnsureSuccessStatusCode();
        var notice = (await client.GetFromJsonAsync<ApiResponse<BillingNoticeDto>>("/api/billing/notice"))!.Data!;
        Assert.Equal(nameof(BillingState.Disabled), notice.State);
        Assert.False(notice.Blocked);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/billing/subscribe", null)).StatusCode);
    }
}
