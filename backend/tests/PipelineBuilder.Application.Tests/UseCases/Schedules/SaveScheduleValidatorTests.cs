using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Schedules;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Schedules;

[TestClass]
public sealed class SaveScheduleValidatorTests
{
    private readonly Mock<ICronExpressions> _cron = new();
    private SaveScheduleValidator _sut = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public SaveScheduleValidatorTests() => CreateSut();

    private void CreateSut()
    {
        _cron.Reset();
        _cron.Setup(c => c.IsValid("0 8 * * 1-5")).Returns(true);
        _sut = new(_cron.Object);
    }

    [TestMethod]
    public async Task ValidateAsync_When_AliasAndCronValid_Then_Accepts()
    {
        var result = await _sut.ValidateAsync(new SaveScheduleInput(Guid.NewGuid(), 0, "weekday-morning_1", "0 8 * * 1-5", true), _ct);

        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("-starts-with-dash")]
    [DataRow("has space")]
    [DataRow("emoji😀")]
    public async Task ValidateAsync_When_AliasInvalid_Then_Rejects(string alias)
    {
        var result = await _sut.ValidateAsync(new SaveScheduleInput(Guid.NewGuid(), 0, alias, "0 8 * * 1-5", true), _ct);

        result.Errors.ShouldContain(e => e.PropertyName == nameof(SaveScheduleInput.Alias));
    }

    [TestMethod]
    public async Task ValidateAsync_When_CronInvalid_Then_ExplainsExpectedFormat()
    {
        var result = await _sut.ValidateAsync(new SaveScheduleInput(Guid.NewGuid(), 0, "daily", "every day", true), _ct);

        result.Errors.ShouldContain(e => e.PropertyName == nameof(SaveScheduleInput.CronExpression) && e.ErrorMessage.Contains("five-field"));
    }
}
