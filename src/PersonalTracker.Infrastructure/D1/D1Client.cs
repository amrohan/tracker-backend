using System.Net.Http.Json;
using System.Text.Json;

namespace PersonalTracker.Infrastructure.D1;

public sealed class D1QueryException(string message) : Exception(message);

public sealed record D1QueryResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int Changes);

public interface ID1Client
{
    Task<D1QueryResult> QueryAsync(string sql, object?[]? parameters, CancellationToken ct);
}

/// <summary>
/// Talks to D1 over Cloudflare's REST API (control-plane style HTTP, not the fast in-Worker binding).
/// Fine for a single-user app's traffic; Cloudflare's own guidance treats this path as best for
/// admin/bulk use rather than high-volume production queries — see the note in the chat reply.
/// </summary>
public sealed class HttpD1Client(HttpClient http, D1Options options) : ID1Client
{
    private readonly string _path = $"accounts/{options.AccountId}/d1/database/{options.DatabaseId}/query";

    public async Task<D1QueryResult> QueryAsync(string sql, object?[]? parameters, CancellationToken ct)
    {
        var body = new { sql, @params = parameters ?? Array.Empty<object?>() };
        using var response = await http.PostAsJsonAsync(_path, body, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode || (root.TryGetProperty("success", out var ok) && !ok.GetBoolean()))
        {
            var message = root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                ? string.Join("; ",
                    errors.EnumerateArray().Select(e =>
                        e.TryGetProperty("message", out var m) ? m.GetString() : e.GetRawText()))
                : $"D1 query failed with status {(int)response.StatusCode}.";
            throw new D1QueryException(message);
        }

        var results = root.GetProperty("result");
        var first = results.GetArrayLength() > 0 ? results[0] : default;
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var changes = 0;

        if (first.ValueKind == JsonValueKind.Object)
        {
            if (first.TryGetProperty("results", out var resultRows) && resultRows.ValueKind == JsonValueKind.Array)
                foreach (var row in resultRows.EnumerateArray())
                    rows.Add(ToDictionary(row));

            if (first.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("changes", out var changesEl) &&
                changesEl.ValueKind == JsonValueKind.Number)
                changes = changesEl.GetInt32();
        }

        return new D1QueryResult(rows, changes);
    }

    private static Dictionary<string, object?> ToDictionary(JsonElement row)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in row.EnumerateObject())
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                _ => prop.Value.GetRawText(),
            };
        return dict;
    }
}