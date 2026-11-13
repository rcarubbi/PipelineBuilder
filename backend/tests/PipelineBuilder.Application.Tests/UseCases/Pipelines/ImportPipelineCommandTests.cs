using System.Text.Json;
using ErrorOr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Pipelines;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Pipelines;

[TestClass]
public sealed class ImportPipelineCommandTests
{
    private readonly Mock<IPipelineRepository> _repository = new();
    private readonly Mock<IDesignerQueries> _queries = new();
    private ImportPipelineCommand _sut = null!;
    private DocumentRecord _document = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public ImportPipelineCommandTests() => CreateSut();

    private void CreateSut()
    {
        _repository.Reset(); _queries.Reset();
        _document = new(" Customers ", new(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "out.parquet")]),
            JsonSerializer.SerializeToElement(new { blocks = new { } }), new() { ["test"] = new() { ["INPUT"] = "in.csv" } });
        _repository.Setup(r => r.Save(It.IsAny<Guid>(), 0, It.IsAny<DocumentRecord>(), _ct))
            .ReturnsAsync((Guid id, int _, DocumentRecord document, CancellationToken _) => new SavedPipelineRecord(id, 1, document));
        _sut = new(_repository.Object, _queries.Object);
    }

    [TestMethod]
    public async Task Handle_When_NameIsFree_Then_CreatesNewPipelineWithTrimmedName()
    {
        var result = await _sut.Handle(new(_document), _ct);

        result.Value.Pipeline.Id.ShouldNotBe(Guid.Empty);
        result.Value.Pipeline.Document.Name.ShouldBe("Customers");
        _repository.Verify(r => r.Save(result.Value.Pipeline.Id, 0, It.Is<DocumentRecord>(d => d.Environments["test"]["INPUT"] == "in.csv"), _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_NamesAreTaken_Then_UsesNextImportedSuffix()
    {
        _queries.Setup(q => q.PipelineNameExists("Customers", _ct)).ReturnsAsync(true);
        _queries.Setup(q => q.PipelineNameExists("Customers (imported)", _ct)).ReturnsAsync(true);

        var result = await _sut.Handle(new(_document), _ct);

        result.Value.Pipeline.Document.Name.ShouldBe("Customers (imported 2)");
    }

    [TestMethod]
    public async Task Handle_When_ImportedTwice_Then_CreatesDistinctPipelines()
    {
        var first = await _sut.Handle(new(_document), _ct);
        var second = await _sut.Handle(new(_document), _ct);

        first.Value.Pipeline.Id.ShouldNotBe(second.Value.Pipeline.Id);
    }

    [TestMethod]
    public async Task Handle_When_DefinitionInvalid_Then_CreatesNothing()
    {
        _document = _document with { Definition = new(1, [new ParquetRecord("out", "out.parquet"), new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)])]) };

        var result = await _sut.Handle(new(_document), _ct);

        result.FirstError.Type.ShouldBe(ErrorType.Validation);
        _repository.Verify(r => r.Save(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ValidateAsync_When_EnvironmentsOrWorkspaceMissing_Then_Rejects()
    {
        var result = await new ImportPipelineValidator().ValidateAsync(new ImportPipelineInput(_document with
        {
            Environments = [], Workspace = JsonSerializer.SerializeToElement(Array.Empty<int>())
        }), _ct);

        result.Errors.Select(e => e.ErrorMessage).ShouldContain("Provide at least one named environment of name/value variables.");
        result.Errors.Select(e => e.ErrorMessage).ShouldContain("Workspace must be a JSON object.");
    }
}
