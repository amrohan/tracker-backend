namespace PersonalTracker.Domain.Entities;

public sealed class TrackerRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CollectionId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>JSON object keyed by <see cref="Field.Key"/>.</summary>
    public string DataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Optimistic concurrency token; incremented on every update.</summary>
    public long Version { get; set; } = 1;
}
