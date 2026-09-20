namespace PersonalTracker.Domain.ValueObjects;

/// <summary>Type-specific configuration of a field. Only the properties relevant to the type are used.</summary>
public sealed class FieldConfig
{
    public List<string>? Options { get; set; }
    public Guid? TargetCollectionId { get; set; }
    public string? Currency { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
}
