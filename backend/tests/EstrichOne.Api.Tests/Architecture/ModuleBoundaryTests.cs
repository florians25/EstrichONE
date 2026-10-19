using EstrichOne.Api.Infrastructure.Modules;
using NetArchTest.Rules;

namespace EstrichOne.Api.Tests.Architecture;

/// <summary>Erzwingt die Modulgrenzen aus ADR 0002.</summary>
public sealed class ModuleBoundaryTests
{
    private const string ModulesNamespace = "EstrichOne.Api.Modules";

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var module in ModuleCatalog.All)
        {
            data.Add(ModuleName(module.GetType()));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_UsesOtherModulesOnlyThroughTheirPublicNamespace(string module)
    {
        var forbidden = ModuleCatalog.All
            .Select(m => ModuleName(m.GetType()))
            .Where(other => other != module)
            .SelectMany(other => InternalNamespacesOf(other))
            .ToArray();

        if (forbidden.Length == 0)
        {
            return;
        }

        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().ResideInNamespace($"{ModulesNamespace}.{module}")
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Modul {module} greift auf Interna anderer Module zu: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void SharedInfrastructure_DoesNotDependOnModules()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().ResideInNamespace("EstrichOne.Api.Infrastructure.Http")
            .Or().ResideInNamespace("EstrichOne.Api.Infrastructure.Persistence")
            .ShouldNot().HaveDependencyOn(ModulesNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static string ModuleName(Type moduleType) =>
        moduleType.Namespace!.Replace(ModulesNamespace + ".", string.Empty, StringComparison.Ordinal);

    /// <summary>Alle Namespaces eines Moduls außer <c>.Public</c>.</summary>
    private static IEnumerable<string> InternalNamespacesOf(string module) =>
        typeof(Program).Assembly.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(ns => ns == $"{ModulesNamespace}.{module}" || ns.StartsWith($"{ModulesNamespace}.{module}.", StringComparison.Ordinal))
            .Where(ns => !ns.StartsWith($"{ModulesNamespace}.{module}.Public", StringComparison.Ordinal))
            .Distinct();
}
