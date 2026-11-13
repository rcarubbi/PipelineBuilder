namespace PipelineBuilder.Application.Contracts;

public sealed record ScheduledPipelineRecord(Guid PipelineId, string PipelineName, string Environment);
public sealed record ScheduleRecord(Guid Id, string Alias, string CronExpression, bool IsEnabled, int Revision, ScheduledPipelineRecord[] Pipelines)
{
    /// <summary>A schedule is registered with the job scheduler only while it is enabled and has pipelines to trigger.</summary>
    public bool IsActive => IsEnabled && Pipelines.Length > 0;
}
