using DoctorCrm.Api.DTOs;
using FluentValidation;

namespace DoctorCrm.Api.Validators;

public class LogFollowUpRequestValidator : AbstractValidator<LogFollowUpRequest>
{
    public LogFollowUpRequestValidator()
    {
        RuleFor(x => x.Outcome).NotEmpty().WithMessage("Choose how the follow-up went.");
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}

public class FollowUpSettingsDtoValidator : AbstractValidator<FollowUpSettingsDto>
{
    public FollowUpSettingsDtoValidator()
    {
        RuleFor(x => x.IntervalDays).InclusiveBetween(1, 30).WithMessage("Choose between 1 and 30 days.");
        RuleFor(x => x.MaxAttempts).InclusiveBetween(1, 20).WithMessage("Choose between 1 and 20 attempts.");
        RuleFor(x => x.ReminderTimes).NotNull().Must(t => t.Count <= 8).WithMessage("At most 8 reminder times.");
    }
}
