using Microsoft.VisualStudio.TestTools.UnitTesting;
using PipelineBuilder.Domain.Pipelines;
using Shouldly;

namespace PipelineBuilder.Domain.Tests.Pipelines;

[TestClass]
public sealed class ValueConversionTests
{
    [TestMethod]
    public void Convert_When_DecimalWouldLosePrecision_Then_RejectsValue()
    {
        Should.Throw<FormatException>(() => ValueConversion.Convert("12.345", new("amount", ColumnType.Decimal, Scale: 2)));
        Should.Throw<FormatException>(() => ValueConversion.Convert("1000.00", new("amount", ColumnType.Decimal, Precision: 5, Scale: 2)));
        Should.Throw<FormatException>(() => ValueConversion.Convert("0.12345678901234567890123456789", new("amount", ColumnType.Decimal, Precision: 28, Scale: 28)));
        ValueConversion.Convert("999.99", new("amount", ColumnType.Decimal, Precision: 5, Scale: 2)).ShouldBe(999.99m);
    }

    [TestMethod]
    public void Convert_When_NonFiniteDouble_Then_RejectsValue() =>
        Should.Throw<FormatException>(() => ValueConversion.Convert("NaN", new("number", ColumnType.Double)));

    [TestMethod]
    public void Convert_When_EmptyNullableNumber_Then_ReturnsNull() =>
        ValueConversion.Convert("", new("age", ColumnType.Int64, true)).ShouldBeNull();

    [TestMethod]
    public void Evaluate_When_ValueIsNull_Then_OrdinaryComparisonsDoNotPass()
    {
        Dictionary<string, object?> row = new() { ["age"] = null };
        Column[] schema = [new("age", ColumnType.Int64, true)];

        new ComparisonPredicate("age", ComparisonOperator.NotEqual, "18").Evaluate(row, schema).ShouldBeFalse();
        new NullPredicate("age", true).Evaluate(row, schema).ShouldBeTrue();
    }
}
