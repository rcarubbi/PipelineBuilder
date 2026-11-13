using Microsoft.VisualStudio.TestTools.UnitTesting;
using PipelineBuilder.Infrastructure.Scheduling;
using Shouldly;

namespace PipelineBuilder.Infrastructure.Tests.Scheduling;

[TestClass]
public sealed class NCrontabCronExpressionsTests
{
    private NCrontabCronExpressions _sut = null!;
    public NCrontabCronExpressionsTests() => CreateSut();
    private void CreateSut() => _sut = new();

    [TestMethod]
    [DataRow("*/15 * * * *")]
    [DataRow("30 8 * * 1,2,3,4,5")]
    [DataRow("0 2 1 * *")]
    public void IsValid_When_FiveFieldExpression_Then_Accepts(string expression) => _sut.IsValid(expression).ShouldBeTrue();

    [TestMethod]
    [DataRow("")]
    [DataRow("0 0 8 * * *")]
    [DataRow("61 * * * *")]
    [DataRow("every day")]
    public void IsValid_When_NotFiveFieldExpression_Then_Rejects(string expression) => _sut.IsValid(expression).ShouldBeFalse();

    [TestMethod]
    public void GetNextOccurrences_When_WeekdaySchedule_Then_ReturnsUtcWeekdaysInOrder()
    {
        var friday = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

        var next = _sut.GetNextOccurrences("30 8 * * 1-5", friday, 3);

        next.ShouldBe([
            new DateTimeOffset(2026, 11, 16, 8, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 17, 8, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 18, 8, 30, 0, TimeSpan.Zero)]);
        next.ShouldAllBe(occurrence => occurrence.Offset == TimeSpan.Zero);
    }

    [TestMethod]
    public void GetNextOccurrences_When_StartIsNotUtc_Then_ComputesFromSameInstant()
    {
        var local = new DateTimeOffset(2026, 11, 13, 6, 0, 0, TimeSpan.FromHours(-3));

        _sut.GetNextOccurrences("0 * * * *", local, 1).Single().ShouldBe(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));
    }
}
