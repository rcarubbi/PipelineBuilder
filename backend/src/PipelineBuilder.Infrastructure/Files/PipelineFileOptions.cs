using System.ComponentModel.DataAnnotations;

namespace PipelineBuilder.Infrastructure.Files;

public sealed class PipelineFileOptions
{
    [Required] public string InputRoot { get; set; } = "../../../data/input";
    [Required] public string OutputRoot { get; set; } = "../../../data/output";
}
