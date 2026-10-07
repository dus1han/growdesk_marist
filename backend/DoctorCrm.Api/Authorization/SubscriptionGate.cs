using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace DoctorCrm.Api.Authorization;

/// <summary>Marks an endpoint as usable while the system is blocked for an unpaid subscription.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhileSubscriptionBlockedAttribute : Attribute;

/// <summary>
/// Blocks the system once the subscription is unpaid past its grace period: every endpoint that
/// needs a signed-in user or a capture/bot connection answers 402, except the ones needed to pay
/// (session check, billing) and anonymous ones (sign in, sign out, branding, Stripe's webhook).
/// The state comes from memory (<see cref="BillingAccessCache"/>), so the check is free.
/// </summary>
public class SubscriptionGate(RequestDelegate next)
{
    public const string ErrorCode = "subscription_required";

    public async Task InvokeAsync(HttpContext context, BillingAccessCache cache, BillingConfigStore config, AppDbContext db, TimeProvider time)
    {
        var endpoint = context.GetEndpoint();
        var gated = endpoint is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
            && endpoint.Metadata.GetMetadata<AllowWhileSubscriptionBlockedAttribute>() is null;

        if (!gated || !BillingRules.Evaluate(await cache.GetAsync(db), await config.GetAsync(db), time.GetUtcNow().UtcDateTime).IsBlocked)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(
            "GrowDesk is paused because the subscription payment is overdue.",
            [new ApiError(ErrorCode, "Subscription payment required.")]));
    }
}
