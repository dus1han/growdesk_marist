using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Entities;
using FluentValidation;

namespace DoctorCrm.Api.Validators;

public class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Choose a customer.");
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).WithMessage("The end time must be after the start time.");
        RuleFor(x => x.TreatmentIds).NotEmpty().WithMessage("Choose at least one treatment.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class UpdateBookingRequestValidator : AbstractValidator<UpdateBookingRequest>
{
    public UpdateBookingRequestValidator()
    {
        RuleFor(x => x.TreatmentIds).NotEmpty().WithMessage("Choose at least one treatment.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class RescheduleBookingRequestValidator : AbstractValidator<RescheduleBookingRequest>
{
    public RescheduleBookingRequestValidator()
    {
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).WithMessage("The end time must be after the start time.");
    }
}

public class CancelBookingRequestValidator : AbstractValidator<CancelBookingRequest>
{
    public CancelBookingRequestValidator()
    {
        RuleFor(x => x.CancellationReasonId).GreaterThan(0).WithMessage("Choose a cancellation reason.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

/// <summary>
/// Spec §24, enforced here and in the frontend: next treatment date and next treatment are both
/// empty or both filled.
/// </summary>
public class CompleteBookingRequestValidator : AbstractValidator<CompleteBookingRequest>
{
    public CompleteBookingRequestValidator()
    {
        RuleFor(x => x.ConsultationCharge).GreaterThanOrEqualTo(0).WithMessage("The charge can't be negative.")
            .LessThan(1_000_000).WithMessage("That charge looks too large.");
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0).WithMessage("The paid amount can't be negative.")
            .LessThanOrEqualTo(x => x.ConsultationCharge).WithMessage("The paid amount can't be more than the consultation amount.");
        RuleFor(x => x.PaymentMethodId).NotNull().WithMessage("Choose how the customer paid.").When(x => x.PaidAmount > 0);
        RuleFor(x => x.NextTreatmentId).NotNull().WithMessage("Choose the next treatment, or clear the date.")
            .When(x => x.NextTreatmentDate is not null);
        RuleFor(x => x.NextTreatmentDate).NotNull().WithMessage("Choose the next treatment date, or clear the treatment.")
            .When(x => x.NextTreatmentId is not null);
        RuleFor(x => x.DoctorNotes).MaximumLength(4000);
    }
}
