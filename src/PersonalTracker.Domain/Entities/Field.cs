using PersonalTracker.Domain.Enums;
using PersonalTracker.Domain.ValueObjects;

namespace PersonalTracker.Domain.Entities;

public sealed class Field
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CollectionId { get; set; }
    public Collection? Collection { get; set; }

    public string Name { get; set; } = string.Empty;
    /// <summary>Immutable slug. Record JSON is keyed by this, so renaming a field never orphans data.</summary>
    public string Key { get; set; } = string.Empty;
    public string? Description { get; set; }
    public FieldType Type { get; set; }
    public bool Required { get; set; }
    public int SortOrder { get; set; }
    public FieldConfig Config { get; set; } = new();
    public AggregationType Aggregation { get; set; } = AggregationType.None;
    public bool IsTitle { get; set; }
    public bool ShowInList { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
