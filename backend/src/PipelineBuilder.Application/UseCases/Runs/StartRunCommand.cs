using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Runs;

public sealed record StartRunInput(Guid PipelineId, int Revision, string Environment) : ITransactionalInput;
public sealed record StartRunOutput(RunRecord Run);
public sealed class StartRunCommand(IDesignerQueries queries, IPipelineRepository repository) : IUseCase<StartRunInput, StartRunOutput>
{
    public async Task<ErrorOr<StartRunOutput>> Handle(StartRunInput input, CancellationToken ct)
    {
        var pipeline = await queries.GetPipeline(input.PipelineId, ct);
        if (pipeline is null) return Error.NotFound("Pipeline.NotFound", "Pipeline not found.");
        if (pipeline.Revision != input.Revision) return Error.Conflict("Pipeline.Revision", "The saved pipeline has changed. Reload before running.");
        var snapshot = RunSnapshotFactory.Create(pipeline, input.Environment, RunTriggers.Manual);
        if (snapshot.IsError) return snapshot.Errors;
        var result = await repository.Enqueue(input.PipelineId, input.Revision, snapshot.Value, ct);
        return result.IsError ? result.Errors : new StartRunOutput(result.Value);
    }
}
public sealed class StartRunValidator : AbstractValidator<StartRunInput>
{
    public StartRunValidator()
    {
        RuleFor(x => x.PipelineId).NotEmpty();
        RuleFor(x => x.Revision).GreaterThan(0);
        RuleFor(x => x.Environment).NotEmpty();
    }
}
