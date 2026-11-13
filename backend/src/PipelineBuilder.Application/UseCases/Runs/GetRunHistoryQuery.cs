using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Runs;

public sealed record GetRunHistoryInput(int Page, Guid? PipelineId = null) : IInput;
public sealed record GetRunHistoryOutput(RunRecord[] Runs, int Page, bool HasMore);
public sealed class GetRunHistoryQuery(IDesignerQueries queries) : IUseCase<GetRunHistoryInput, GetRunHistoryOutput>
{
    public const int PageSize = 25;

    public async Task<ErrorOr<GetRunHistoryOutput>> Handle(GetRunHistoryInput input, CancellationToken ct)
    {
        var runs = await queries.GetRuns((input.Page - 1) * PageSize, PageSize + 1, input.PipelineId, ct);
        return new GetRunHistoryOutput(runs.Take(PageSize).ToArray(), input.Page, runs.Length > PageSize);
    }
}
public sealed class GetRunHistoryValidator : AbstractValidator<GetRunHistoryInput>
{
    public GetRunHistoryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, int.MaxValue / GetRunHistoryQuery.PageSize);
        When(x => x.PipelineId.HasValue, () => RuleFor(x => x.PipelineId).NotEqual(Guid.Empty));
    }
}
