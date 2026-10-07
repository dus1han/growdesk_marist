using Stripe;

namespace DoctorCrm.Api.Services;

/// <summary>Everything GrowDesk asks of the payment provider. Stripe in production, a fake in tests.</summary>
public interface IBillingGateway
{
    Task<string> CreateCustomerAsync(string name, string? email, CancellationToken ct);

    /// <summary>The customer's current subscription, card and oldest unpaid subscription invoice.</summary>
    Task<BillingSnapshot> FetchAsync(string customerId, CancellationToken ct);

    Task<PlanInfo> GetPlanAsync(string priceId, CancellationToken ct);

    /// <summary>
    /// Checks a secret key and price before they are saved: the key works and the price is a
    /// recurring one. Throws <see cref="BillingConfigException"/> with a message for the owner.
    /// </summary>
    Task<PlanInfo> ValidateAsync(string secretKey, string priceId, CancellationToken ct);

    /// <summary>
    /// A Stripe Checkout page where the card is entered and the first payment taken. With
    /// <paramref name="billingCycleAnchor"/>, renewals fall on that date (the clinic's original
    /// billing date) and only the days until then are charged now.
    /// </summary>
    Task<string> CreateSubscriptionCheckoutAsync(string customerId, string priceId, DateTime? billingCycleAnchor,
        string successUrl, string cancelUrl, CancellationToken ct);

    /// <summary>Stripe's billing portal, where the admin changes the card on file.</summary>
    Task<string> CreatePortalAsync(string customerId, string returnUrl, CancellationToken ct);

    /// <summary>Charges the card on file for an open invoice.</summary>
    Task<PayResult> PayInvoiceAsync(string invoiceId, CancellationToken ct);

    Task<IReadOnlyList<BillingInvoice>> ListInvoicesAsync(string customerId, CancellationToken ct);

    /// <summary>Verifies a webhook's signature and returns the Stripe customer it concerns (if any).
    /// Throws <see cref="BillingWebhookException"/> when the signature is invalid.</summary>
    WebhookEvent ParseWebhook(string json, string signature, string secret);
}

public record BillingSnapshot(
    string? SubscriptionId,
    string? Status,
    bool CancelAtPeriodEnd,
    DateTime? CurrentPeriodEnd,
    DateTime? EndedAt,
    DateTime? BillingCycleAnchor,
    PlanInfo? Plan,
    string? CardBrand,
    string? CardLast4,
    UnpaidInvoice? Unpaid);

public record PlanInfo(string? Name, long Amount, string Currency, string Interval, int IntervalCount);

public record UnpaidInvoice(string Id, long Amount, string Currency, DateTime DueSince, string? HostedUrl);

public record PayResult(bool Paid, string? Error);

public record BillingInvoice(
    string Id,
    string? Number,
    DateTime Date,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    long Amount,
    long AmountPaid,
    string Currency,
    string Status,
    DateTime? PaidAt,
    int AttemptCount,
    string? HostedUrl,
    string? PdfUrl);

public record WebhookEvent(string Type, string? CustomerId);

public class BillingWebhookException(string message) : Exception(message);

public class BillingConfigException(string message, string field) : Exception(message)
{
    public string Field { get; } = field;
}

public class StripeBillingGateway(BillingConfigStore config) : IBillingGateway
{
    // One client per key, recreated when an owner saves a different key in Stripe Settings.
    private (string Key, StripeClient Client)? _stripe;

    private StripeClient _client
    {
        get
        {
            var key = config.Current.SecretKey;
            if (_stripe is not { } s || s.Key != key) _stripe = s = (key, new StripeClient(key));
            return s.Client;
        }
    }

    /// <summary>Statuses of a subscription that is still running (paid up or being chased for payment).</summary>
    private static readonly string[] LiveStatuses = ["active", "trialing", "past_due", "unpaid", "incomplete", "paused"];

    public async Task<string> CreateCustomerAsync(string name, string? email, CancellationToken ct)
    {
        var customer = await new Stripe.CustomerService(_client).CreateAsync(new CustomerCreateOptions
        {
            Name = name,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            Metadata = new Dictionary<string, string> { ["app"] = "growdesk" },
        }, cancellationToken: ct);
        return customer.Id;
    }

    public async Task<BillingSnapshot> FetchAsync(string customerId, CancellationToken ct)
    {
        var subscriptions = await new SubscriptionService(_client).ListAsync(new SubscriptionListOptions
        {
            Customer = customerId,
            Status = "all",
            Limit = 10,
            Expand = ["data.default_payment_method", "data.items.data.price.product"],
        }, cancellationToken: ct);

        // The running subscription if there is one, otherwise the most recent one that ended.
        var sub = subscriptions.Data
            .OrderByDescending(s => LiveStatuses.Contains(s.Status))
            .ThenByDescending(s => s.Created)
            .FirstOrDefault();

        var customer = await new Stripe.CustomerService(_client).GetAsync(customerId,
            new CustomerGetOptions { Expand = ["invoice_settings.default_payment_method"] }, cancellationToken: ct);
        var card = sub?.DefaultPaymentMethod?.Card ?? customer.InvoiceSettings?.DefaultPaymentMethod?.Card;

        var unpaid = await OldestUnpaidAsync(customerId, ct);
        var item = sub?.Items?.Data.FirstOrDefault();

        return new BillingSnapshot(
            sub?.Id,
            sub?.Status,
            sub?.CancelAtPeriodEnd ?? false,
            item is null ? null : Utc(item.CurrentPeriodEnd),
            sub?.EndedAt is { } ended ? Utc(ended) : null,
            sub is null ? null : Utc(sub.BillingCycleAnchor),
            item?.Price is { } price ? ToPlan(price) : null,
            card?.Brand,
            card?.Last4,
            unpaid);
    }

    /// <summary>
    /// The oldest subscription invoice Stripe has already tried to charge without success. An open
    /// invoice that hasn't been attempted yet is just a renewal in progress, not a missed payment.
    /// </summary>
    private async Task<UnpaidInvoice?> OldestUnpaidAsync(string customerId, CancellationToken ct)
    {
        var service = new InvoiceService(_client);
        var candidates = new List<Invoice>();
        foreach (var status in new[] { "open", "uncollectible" })
        {
            var page = await service.ListAsync(new InvoiceListOptions { Customer = customerId, Status = status, Limit = 20 }, cancellationToken: ct);
            candidates.AddRange(page.Data);
        }

        var invoice = candidates
            .Where(i => i.Parent?.SubscriptionDetails is not null && i.Attempted && i.AmountRemaining > 0)
            .OrderBy(DueSince)
            .FirstOrDefault();

        return invoice is null
            ? null
            : new UnpaidInvoice(invoice.Id, invoice.AmountRemaining, invoice.Currency, DueSince(invoice), invoice.HostedInvoiceUrl);
    }

    private static DateTime DueSince(Invoice i) => Utc(i.DueDate ?? i.EffectiveAt ?? i.Created);

    public async Task<PlanInfo> GetPlanAsync(string priceId, CancellationToken ct)
    {
        var price = await new PriceService(_client).GetAsync(priceId, new PriceGetOptions { Expand = ["product"] }, cancellationToken: ct);
        return ToPlan(price);
    }

    public async Task<PlanInfo> ValidateAsync(string secretKey, string priceId, CancellationToken ct)
    {
        Price price;
        try
        {
            price = await new PriceService(new StripeClient(secretKey)).GetAsync(priceId, new PriceGetOptions { Expand = ["product"] }, cancellationToken: ct);
        }
        catch (StripeException ex) when (ex.StripeError?.Type is "invalid_request_error" && ex.StripeError.Code == "resource_missing")
        {
            throw new BillingConfigException("No price with this ID in this Stripe account (check test vs live mode).", "priceId");
        }
        catch (StripeException ex) when (ex.HttpStatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw new BillingConfigException("Stripe didn't accept this secret key.", "secretKey");
        }
        catch (StripeException ex)
        {
            throw new BillingConfigException($"Stripe said: {ex.StripeError?.Message ?? ex.Message}", "secretKey");
        }

        if (price.Recurring is null)
            throw new BillingConfigException("This price is a one-time price. Choose a recurring (monthly or yearly) price.", "priceId");
        if (!price.Active)
            throw new BillingConfigException("This price is archived in Stripe. Choose an active price.", "priceId");
        return ToPlan(price);
    }

    private static PlanInfo ToPlan(Price price) => new(
        price.Product?.Name,
        price.UnitAmount ?? 0,
        price.Currency,
        price.Recurring?.Interval ?? "month",
        (int)(price.Recurring?.IntervalCount ?? 1));

    public async Task<string> CreateSubscriptionCheckoutAsync(string customerId, string priceId, DateTime? billingCycleAnchor,
        string successUrl, string cancelUrl, CancellationToken ct)
    {
        var session = await new Stripe.Checkout.SessionService(_client).CreateAsync(new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            Customer = customerId,
            LineItems = [new Stripe.Checkout.SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SubscriptionData = billingCycleAnchor is null ? null : new Stripe.Checkout.SessionSubscriptionDataOptions
            {
                BillingCycleAnchor = billingCycleAnchor,
                ProrationBehavior = "create_prorations",
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
        }, cancellationToken: ct);
        return session.Url;
    }

    public async Task<string> CreatePortalAsync(string customerId, string returnUrl, CancellationToken ct)
    {
        var session = await new Stripe.BillingPortal.SessionService(_client).CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl }, cancellationToken: ct);
        return session.Url;
    }

    public async Task<PayResult> PayInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        try
        {
            var invoice = await new InvoiceService(_client).PayAsync(invoiceId, new InvoicePayOptions { OffSession = true }, cancellationToken: ct);
            return new PayResult(invoice.Status == "paid", invoice.Status == "paid" ? null : "The payment did not go through.");
        }
        catch (StripeException ex) when (ex.StripeError is not null)
        {
            // Card errors carry a message written for the cardholder ("Your card was declined.").
            return new PayResult(false, ex.StripeError.Type == "card_error" ? ex.StripeError.Message : "The payment did not go through.");
        }
    }

    public async Task<IReadOnlyList<BillingInvoice>> ListInvoicesAsync(string customerId, CancellationToken ct)
    {
        var page = await new InvoiceService(_client).ListAsync(new InvoiceListOptions { Customer = customerId, Limit = 36 }, cancellationToken: ct);
        return page.Data
            .Where(i => i.Status != "draft")
            .Select(i =>
            {
                var period = i.Lines?.Data.FirstOrDefault()?.Period;
                return new BillingInvoice(
                    i.Id,
                    i.Number,
                    Utc(i.EffectiveAt ?? i.Created),
                    period is null ? null : Utc(period.Start),
                    period is null ? null : Utc(period.End),
                    i.Total,
                    i.AmountPaid,
                    i.Currency,
                    i.Status,
                    i.StatusTransitions?.PaidAt is { } paid ? Utc(paid) : null,
                    (int)i.AttemptCount,
                    i.HostedInvoiceUrl,
                    i.InvoicePdf);
            })
            .ToList();
    }

    public WebhookEvent ParseWebhook(string json, string signature, string secret)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signature, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new BillingWebhookException(ex.Message);
        }

        var customerId = stripeEvent.Data.Object switch
        {
            Invoice i => i.CustomerId,
            Subscription s => s.CustomerId,
            Stripe.Checkout.Session s => s.CustomerId,
            Customer c => c.Id,
            _ => null,
        };
        return new WebhookEvent(stripeEvent.Type, customerId);
    }

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
