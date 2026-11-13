using System.Text.Json.Serialization;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.UseCases.Schedules;

namespace PipelineBuilder.Presentation.Models;

public sealed record SaveScheduleRequest([property: JsonRequired] int ExpectedRevision, string Alias, string CronExpression, [property: JsonRequired] bool IsEnabled);
public sealed record PreviewScheduleRequest(string CronExpression);
public sealed record PreviewScheduleResponse(DateTimeOffset[] NextRuns);
public sealed record DeleteScheduleResponse(bool Deleted);
public sealed record ScheduledPipelineResponse(Guid PipelineId, string PipelineName, string Environment);
public sealed record ScheduleResponse(Guid Id, string Alias, string CronExpression, bool IsEnabled, int Revision,
    ScheduledPipelineResponse[] Pipelines, DateTimeOffset[] NextRuns)
{
    public static ScheduleResponse MapFrom(ScheduleRecord schedule, DateTimeOffset[] nextRuns) => new(schedule.Id, schedule.Alias, schedule.CronExpression,
        schedule.IsEnabled, schedule.Revision, schedule.Pipelines.Select(p => new ScheduledPipelineResponse(p.PipelineId, p.PipelineName, p.Environment)).ToArray(), nextRuns);
    public static ScheduleResponse MapFrom(ScheduleSummaryRecord summary) => MapFrom(summary.Schedule, summary.NextRuns);
}
public sealed record ScheduleListResponse(ScheduleResponse[] Schedules);
