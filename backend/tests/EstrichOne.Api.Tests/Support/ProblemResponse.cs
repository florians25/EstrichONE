using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EstrichOne.Api.Tests.Support;

public sealed record ProblemResponse(
    int Status,
    string Title,
    string? Detail,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("traceId")] string TraceId);

public static class ProblemResponseExtensions
{
    public static async Task<ProblemResponse> ReadProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(TestJson.Options);
        problem.ShouldNotBeNull();
        return problem;
    }
}
