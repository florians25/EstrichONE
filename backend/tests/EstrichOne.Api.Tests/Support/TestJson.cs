using System.Text.Json;
using System.Text.Json.Serialization;

namespace EstrichOne.Api.Tests.Support;

/// <summary>Dieselben JSON-Regeln wie die API (Program.cs): Web-Standard, Aufzählungen als camelCase-Text.</summary>
public static class TestJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
