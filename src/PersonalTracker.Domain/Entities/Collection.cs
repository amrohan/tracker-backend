using PersonalTracker.Domain.Enums;

namespace PersonalTracker.Domain.Entities;

public sealed class Collection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Icon { get; set; } = "📁";

    public CoverType CoverType { get; set; } = CoverType.None;
    /// <summary>Gradient key, #RRGGBB colour, or the relative storage path of the uploaded image.</summary>
    public string? CoverValue { get; set; }
    /// <summary>Changes whenever the cover changes; used for cache busting.</summary>
    public DateTime? CoverUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Field> Fields { get; set; } = new List<Field>();
}
