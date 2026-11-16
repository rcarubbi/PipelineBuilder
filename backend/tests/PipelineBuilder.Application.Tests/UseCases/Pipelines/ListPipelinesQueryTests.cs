using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Pipelines;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Pipelines;

[TestClass]
public sealed class ListPipelinesQueryTests
{
    private readonly Mock<IDesignerQueries> _queries = new();
    private ListPipelinesQuery _sut = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public ListPipelinesQueryTests() => CreateSut();
    private void CreateSut() { _queries.Reset(); _sut = new(_queries.Object); }

    [TestMethod]
    public async Task Handle_When_MorePipelinesExist_Then_ReturnsBoundedPage()
    {
        var pipelines = Enumerable.Range(0, 26).Select(i => new PipelineSummaryRecord(Guid.NewGuid(), $"Pipeline {i}", 1, ["test"])).ToArray();
        _queries.Setup(q => q.GetPipelines(25, 26, _ct)).ReturnsAsync(pipelines);
        _queries.Setup(q => q.CountPipelines(_ct)).ReturnsAsync(50);

        var result = await _sut.Handle(new(2), _ct);

        result.Value.Page.ShouldBe(2);
        result.Value.HasMore.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(50);
        result.Value.Pipelines.ShouldBe(pipelines.Take(25).ToArray());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public async Task ValidateAsync_When_PageIsInvalid_Then_RejectsRequest(int page)
    {
        var result = await new ListPipelinesValidator().ValidateAsync(new ListPipelinesInput(page), _ct);

        result.IsValid.ShouldBeFalse();
    }
}
