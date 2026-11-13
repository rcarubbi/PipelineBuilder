using NCrontab;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Infrastructure.Scheduling;

/// <summary>Five-field UTC cron expressions, matching the parser TickerQ uses for cron tickers.</summary>
public sealed class NCrontabCronExpressions : ICronExpressions
{
    private static readonly TimeSpan SearchWindow = TimeSpan.FromDays(366 * 5);

    public bool IsValid(string expression) => !string.IsNullOrWhiteSpace(expression) && CrontabSchedule.TryParse(expression.Trim()) is not null;

    public DateTimeOffset[] GetNextOccurrences(string expression, DateTimeOffset after, int count)
    {
        var schedule = CrontabSchedule.TryParse(expression.Trim()) ?? throw new ArgumentException("Invalid cron expression.", nameof(expression));
        var start = after.UtcDateTime;
        return schedule.GetNextOccurrences(start, start + SearchWindow).Take(count)
            .Select(occurrence => new DateTimeOffset(DateTime.SpecifyKind(occurrence, DateTimeKind.Utc))).ToArray();
    }
}
