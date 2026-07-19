using System.Net.Http.Json;
using System.Text.Json;

namespace Rulewright.Sample.BlazorBuilder.Services;

public sealed record ExampleEntry(string File, string Description);

/// <summary>
/// Lists and fetches the <c>examples/*.json</c> rule-schema documents bundled under
/// <c>wwwroot/examples</c> (copied from the repo's top-level <c>examples/</c> folder), so the
/// builder's "load example" picker has real, varied documents to demo against.
/// </summary>
public sealed class ExampleCatalogService
{
    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private IReadOnlyList<ExampleEntry>? _manifest;

    public ExampleCatalogService(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<ExampleEntry>> GetManifestAsync()
    {
        _manifest ??= await _http.GetFromJsonAsync<ExampleEntry[]>("examples/manifest.json", ManifestOptions)
            ?? Array.Empty<ExampleEntry>();
        return _manifest;
    }

    public Task<string> GetExampleJsonAsync(string file) => _http.GetStringAsync($"examples/{file}");
}
