using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Runs;

public sealed record GetRunHistoryInput(int Page, Guid? PipelineId = null, int PageSize = GetRunHistoryQuery.DefaultPageSize) : IInput;
public sealed record GetRunHistoryOutput(RunRecord[] Runs, int Page, bool HasMore, int TotalCount);
public sealed class GetRunHistoryQuery(IDesignerQueries queries) : IUseCase<GetRunHistoryInput, GetRunHistoryOutput>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public async Task<ErrorOr<GetRunHistoryOutput>> Handle(GetRunHistoryInput input, CancellationToken ct)
    {
        var runs = await queries.GetRuns((input.Page - 1) * input.PageSize, input.PageSize + 1, input.PipelineId, ct);
        var total = await queries.CountRuns(input.PipelineId, ct);
        return new GetRunHistoryOutput(runs.Take(input.PageSize).ToArray(), input.Page, runs.Length > input.PageSize, total);
    }
}
public sealed class GetRunHistoryValidator : AbstractValidator<GetRunHistoryInput>
{
    public GetRunHistoryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetRunHistoryQuery.MaxPageSize);
        RuleFor(x => x.Page).Must((input, page) => page >= 1 && page <= int.MaxValue / Math.Max(1, input.PageSize));
        When(x => x.PipelineId.HasValue, () => RuleFor(x => x.PipelineId).NotEqual(Guid.Empty));
    }
}
