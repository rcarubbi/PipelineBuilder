using FluentValidation;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Schedules;

internal static class ScheduleRules
{
    public const int AliasMaxLength = 64;
    public const int CronMaxLength = 100;

    public static IRuleBuilderOptions<T, string> ScheduleAlias<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().MaximumLength(AliasMaxLength)
        .Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$").WithMessage("Alias may contain letters, digits, '-' and '_', and must start with a letter or digit.");

    public static IRuleBuilderOptions<T, string> CronExpression<T>(this IRuleBuilder<T, string> rule, ICronExpressions cron) => rule
        .NotEmpty().MaximumLength(CronMaxLength)
        .Must(cron.IsValid).WithMessage("Use a valid five-field cron expression: minute hour day-of-month month day-of-week (UTC).");
}
