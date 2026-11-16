using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Runs;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Runs;

[TestClass]
public sealed class GetRunHistoryQueryTests
{
    private readonly Mock<IDesignerQueries> _queries = new();
    private GetRunHistoryQuery _sut = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public GetRunHistoryQueryTests() => CreateSut();
    private void CreateSut() { _queries.Reset(); _sut = new(_queries.Object); }

    [TestMethod]
    public async Task Handle_When_MoreRunsExist_Then_ReturnsOneBoundedPage()
    {
        var runs = Enumerable.Range(0, 26).Select(_ => new RunRecord(Guid.NewGuid(), Guid.NewGuid(), 1, "test", "Succeeded", DateTimeOffset.UtcNow, null, null, 3, 2, null, null)).ToArray();
        _queries.Setup(q => q.GetRuns(25, 26, null, _ct)).ReturnsAsync(runs);
        _queries.Setup(q => q.CountRuns(null, _ct)).ReturnsAsync(40);

        var result = await _sut.Handle(new(2), _ct);

        result.IsError.ShouldBeFalse();
        result.Value.Page.ShouldBe(2);
        result.Value.HasMore.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(40);
        result.Value.Runs.ShouldBe(runs.Take(25).ToArray());
    }

    [TestMethod]
    public async Task Handle_When_PageSizeIsProvided_Then_UsesItForPaging()
    {
        var runs = Enumerable.Range(0, 6).Select(_ => new RunRecord(Guid.NewGuid(), Guid.NewGuid(), 1, "test", "Succeeded", DateTimeOffset.UtcNow, null, null, 3, 2, null, null)).ToArray();
        _queries.Setup(q => q.GetRuns(10, 6, null, _ct)).ReturnsAsync(runs);
        _queries.Setup(q => q.CountRuns(null, _ct)).ReturnsAsync(12);

        var result = await _sut.Handle(new(3, PageSize: 5), _ct);

        result.Value.Runs.ShouldBe(runs.Take(5).ToArray());
        result.Value.HasMore.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(12);
    }

    [TestMethod]
    public async Task Handle_When_HistoryIsEmpty_Then_ReturnsEmptyLastPage()
    {
        _queries.Setup(q => q.GetRuns(0, 26, null, _ct)).ReturnsAsync([]);
        _queries.Setup(q => q.CountRuns(null, _ct)).ReturnsAsync(0);

        var result = await _sut.Handle(new(1), _ct);

        result.Value.Runs.ShouldBeEmpty();
        result.Value.HasMore.ShouldBeFalse();
        result.Value.TotalCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task Handle_When_PipelineSelected_Then_FiltersHistory()
    {
        var id = Guid.NewGuid();
        _queries.Setup(q => q.GetRuns(0, 26, id, _ct)).ReturnsAsync([]);
        _queries.Setup(q => q.CountRuns(id, _ct)).ReturnsAsync(0);

        await _sut.Handle(new(1, id), _ct);

        _queries.Verify(q => q.GetRuns(0, 26, id, _ct), Times.Once);
        _queries.Verify(q => q.CountRuns(id, _ct), Times.Once);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public async Task ValidateAsync_When_PageIsInvalid_Then_RejectsRequest(int page)
    {
        var result = await new GetRunHistoryValidator().ValidateAsync(new GetRunHistoryInput(page), _ct);

        result.IsValid.ShouldBeFalse();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(101)]
    public async Task ValidateAsync_When_PageSizeIsInvalid_Then_RejectsRequest(int pageSize)
    {
        var result = await new GetRunHistoryValidator().ValidateAsync(new GetRunHistoryInput(1, PageSize: pageSize), _ct);

        result.IsValid.ShouldBeFalse();
    }
}
