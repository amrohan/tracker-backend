using System.Text.Json;
using PersonalTracker.Application.Common;
using PersonalTracker.Domain.ValueObjects;

namespace PersonalTracker.Infrastructure.Persistence;

public static class FieldConfigJson
{
    public static string Serialize(FieldConfig? config) =>
        JsonSerializer.Serialize(config ?? new FieldConfig(), JsonDefaults.Options);

    public static FieldConfig Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new FieldConfig()
            : JsonSerializer.Deserialize<FieldConfig>(json, JsonDefaults.Options) ?? new FieldConfig();
}