using DoctorCrm.Api.DTOs;
using FluentValidation;

namespace DoctorCrm.Api.Validators;

/// <summary>Shape checks only. Normalisation, duplicates and lookups live in CustomerService.</summary>
public class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Please enter the customer's name.").MaximumLength(150);
        RuleFor(x => x.WhatsApp).MaximumLength(30);
        RuleFor(x => x.SecondaryPhone).MaximumLength(30);
        RuleFor(x => x.Instagram).MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().WithMessage("Please enter a valid email address.").MaximumLength(254)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.LeadSourceId).NotNull().WithMessage("Please choose a lead source.");
        RuleFor(x => x.Notes).MaximumLength(4000);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.WhatsApp) || !string.IsNullOrWhiteSpace(x.Instagram))
            .WithName("whatsApp").OverridePropertyName("whatsApp")
            .WithMessage("Add a WhatsApp number or an Instagram name.");
    }
}
