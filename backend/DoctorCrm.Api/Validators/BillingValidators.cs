using DoctorCrm.Api.DTOs;
using FluentValidation;

namespace DoctorCrm.Api.Validators;

public class SaveBillingSettingsRequestValidator : AbstractValidator<SaveBillingSettingsRequest>
{
    public SaveBillingSettingsRequestValidator()
    {
        RuleFor(x => x.SecretKey)
            .Must(k => string.IsNullOrWhiteSpace(k) || k.Trim().StartsWith("sk_") || k.Trim().StartsWith("rk_"))
            .WithMessage("Use the secret key (it starts with sk_live_ or sk_test_), not the publishable key.")
            .MaximumLength(300);
        RuleFor(x => x.WebhookSecret)
            .Must(k => string.IsNullOrWhiteSpace(k) || k.Trim().StartsWith("whsec_"))
            .WithMessage("The webhook signing secret starts with whsec_.")
            .MaximumLength(300);
        RuleFor(x => x.PriceId)
            .Must(p => string.IsNullOrWhiteSpace(p) || p.Trim().StartsWith("price_"))
            .WithMessage("The price ID starts with price_ (not prod_, which is the product).")
            .MaximumLength(100);
        RuleFor(x => x.GraceDays).InclusiveBetween(0, 30).WithMessage("Choose between 0 and 30 days.");
    }
}
