using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Files;
using PipelineBuilder.Domain.Pipelines;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Files;

[TestClass]
public sealed class DiscoverSchemaQueryTests
{
    private readonly Mock<IPipelineFiles> _files = new();
    private DiscoverSchemaQuery _sut = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;

    public DiscoverSchemaQueryTests() => CreateSut();
    private void CreateSut() { _files.Reset(); _sut = new(_files.Object); }

    [TestMethod]
    public async Task Handle_When_PostgresSelected_Then_UsesExternalQueryAndReturnsSchema()
    {
        var input = new PostgresInputRecord("postgres", "db.example", "5432", "customers", "reader", "${secret.PG_PASSWORD}",
            "Require", "SELECT id FROM customers", []);
        var schema = new[] { new ColumnRecord("id", ColumnTypeRecord.Int64) };
        _files.Setup(files => files.DiscoverPostgres(It.Is<PostgresInputStep>(step => step.Host == input.Host && step.Query == input.Query), _ct))
            .ReturnsAsync(schema);

        var result = await _sut.Handle(new("", ",", true, "postgres", Postgres: input), _ct);

        result.IsError.ShouldBeFalse();
        result.Value.Schema.ShouldBe(schema);
        result.Value.SampleLimit.ShouldBe(100);
        _files.Verify(files => files.DiscoverPostgres(It.IsAny<PostgresInputStep>(), _ct), Times.Once);
    }

    [TestMethod]
    [DataRow("password")]
    [DataRow("${env.PG_PASSWORD}")]
    public async Task ValidateAsync_When_PostgresPasswordIsNotSecretReference_Then_RejectsRequest(string password)
    {
        var postgres = new PostgresInputRecord("postgres", "db.example", "5432", "customers", "reader", password,
            "Require", "SELECT id FROM customers", []);

        var result = await new DiscoverSchemaValidator().ValidateAsync(new DiscoverSchemaInput("", ",", true, "postgres", Postgres: postgres), _ct);

        result.IsValid.ShouldBeFalse();
    }
}
