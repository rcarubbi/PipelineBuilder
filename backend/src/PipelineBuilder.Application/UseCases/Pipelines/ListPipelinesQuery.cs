using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Pipelines;

public sealed record ListPipelinesInput(int Page) : IInput;
public sealed record ListPipelinesOutput(PipelineSummaryRecord[] Pipelines, int Page, bool HasMore);
public sealed class ListPipelinesQuery(IDesignerQueries queries) : IUseCase<ListPipelinesInput, ListPipelinesOutput>
{
    public const int PageSize = 25;
    public async Task<ErrorOr<ListPipelinesOutput>> Handle(ListPipelinesInput input, CancellationToken ct)
    {
        var pipelines = await queries.GetPipelines((input.Page - 1) * PageSize, PageSize + 1, ct);
        return new ListPipelinesOutput(pipelines.Take(PageSize).ToArray(), input.Page, pipelines.Length > PageSize);
    }
}
public sealed class ListPipelinesValidator : AbstractValidator<ListPipelinesInput>
{
    public ListPipelinesValidator() => RuleFor(x => x.Page).InclusiveBetween(1, int.MaxValue / ListPipelinesQuery.PageSize);
}
