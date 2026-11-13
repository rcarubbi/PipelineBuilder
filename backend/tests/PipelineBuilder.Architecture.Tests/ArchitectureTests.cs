using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetArchTest.Rules;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;
using PipelineBuilder.Infrastructure.Persistence;
using PipelineBuilder.Presentation.Controllers;
using Shouldly;

namespace PipelineBuilder.Architecture.Tests;

[TestClass]
public sealed class ArchitectureTests
{
    [TestMethod]
    public void Dependencies_When_InspectingLayers_Then_PointInward()
    {
        Types.InAssembly(typeof(PipelineDefinition).Assembly).ShouldNot().HaveDependencyOnAny("PipelineBuilder.Application", "PipelineBuilder.Infrastructure", "PipelineBuilder.Presentation").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(typeof(IInput).Assembly).ShouldNot().HaveDependencyOnAny("PipelineBuilder.Infrastructure", "PipelineBuilder.Presentation").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(typeof(PipelineDbContext).Assembly).ShouldNot().HaveDependencyOn("PipelineBuilder.Presentation").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(typeof(PipelinesController).Assembly).That().ResideInNamespace("PipelineBuilder.Presentation.Controllers")
            .ShouldNot().HaveDependencyOnAny("PipelineBuilder.Domain", "PipelineBuilder.Infrastructure").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [TestMethod]
    public void UseCases_When_InspectingHandlers_Then_HaveNamedContracts()
    {
        var handlers = typeof(IInput).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUseCase<,>))).ToArray();

        handlers.ShouldNotBeEmpty();
        handlers.ShouldAllBe(t => t.Name.EndsWith("Command", StringComparison.Ordinal) || t.Name.EndsWith("Query", StringComparison.Ordinal));
    }
}
