using System.Text.RegularExpressions;
using ErrorOr;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.UseCases.Runs;

internal static partial class RunSnapshotFactory
{
    public const string EngineVersion = "1.0.0";

    public static ErrorOr<RunSnapshotRecord> Create(SavedPipelineRecord pipeline, string environment, string trigger)
    {
        if (!pipeline.Document.Environments.TryGetValue(environment, out var variables)) return Error.Validation("Environment", "Select an existing environment.");
        try
        {
            string Resolve(string path) => VariablePattern().Replace(path, match => variables.TryGetValue(match.Groups[1].Value, out var value)
                ? value : throw new PipelineDataException($"Missing environment variable '{match.Groups[1].Value}'."));
            var definition = pipeline.Document.Definition with
            {
                Steps = pipeline.Document.Definition.Steps.Select(step => step switch
                {
                    CsvRecord csv => (StepRecord)(csv with { Path = Resolve(csv.Path) }),
                    ParquetRecord output => output with { Path = Resolve(output.Path) },
                    _ => step
                }).ToArray()
            };
            var analysis = PipelineValidation.Analyze(definition.MapTo());
            if (analysis.Issues.Length > 0) return analysis.Issues.Select(i => Error.Validation(i.BlockId, i.Message)).ToList();
            return new RunSnapshotRecord(definition, environment, new(variables), EngineVersion, trigger);
        }
        catch (PipelineDataException ex) { return Error.Validation("Run.Configuration", ex.Message); }
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
