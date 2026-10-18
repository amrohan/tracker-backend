namespace PersonalTracker.Domain.Entities;

public sealed class SavedFilter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CollectionId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>JSON-serialised ApiFilter[] array.</summary>
    public string FiltersJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
