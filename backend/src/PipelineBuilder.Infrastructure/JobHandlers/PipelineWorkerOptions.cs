using System.ComponentModel.DataAnnotations;

namespace PipelineBuilder.Infrastructure.JobHandlers;

public sealed class PipelineWorkerOptions
{
    [Range(1, 64)]
    public int PoolSize { get; set; } = 2;
}
