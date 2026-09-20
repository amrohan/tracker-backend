namespace PersonalTracker.Application.Fields;

public sealed record ConfigContext(Guid CollectionId, IReadOnlySet<Guid> UserCollectionIds);

/// <summary>
/// One implementation per field type. Adding a new type = add a handler and register it in DI.
/// Everything the engine needs to know about a type (validation, aggregations, kind) lives here.
/// </summary>
public interface IFieldTypeHandler
{
    FieldType Type { get; }
    string Label { get; }
    ValueKind Kind { get; }
    bool IsReference { get; }
    bool IsSearchable { get; }
    IReadOnlyList<AggregationType> Aggregations { get; }

    /// <summary>Validates and normalises the type-specific config. Errors are added under "config.*".</summary>
    FieldConfig NormalizeConfig(FieldConfig? input, ConfigContext context, ErrorBag errors);

    /// <summary>
    /// Validates a non-empty value. Returns the normalised CLR value (string, decimal, bool or List&lt;string&gt;)
    /// or null with <paramref name="error"/> set. <paramref name="existing"/> is the stored value (for leniency rules).
    /// </summary>
    object? NormalizeValue(JsonElement value, Field field, JsonElement? existing, out string? error);

    string? ToDisplayText(JsonElement value, Field field);
}

public interface IFieldTypeRegistry
{
    IFieldTypeHandler Get(FieldType type);
    IReadOnlyList<IFieldTypeHandler> All { get; }
}

public sealed class FieldTypeRegistry(IEnumerable<IFieldTypeHandler> handlers) : IFieldTypeRegistry
{
    private readonly Dictionary<FieldType, IFieldTypeHandler> _map = handlers.ToDictionary(h => h.Type);

    public IFieldTypeHandler Get(FieldType type) =>
        _map.TryGetValue(type, out var handler)
            ? handler
            : throw new InvalidOperationException($"No handler registered for field type '{type}'.");

    public IReadOnlyList<IFieldTypeHandler> All => _map.Values.OrderBy(h => h.Type).ToList();
}
