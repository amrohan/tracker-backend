namespace PersonalTracker.Infrastructure.D1;

/// <summary>
/// Replaces EF Core's change tracker. Repositories queue writes here instead of executing them
/// immediately, so "load, mutate properties, call SaveChangesAsync once" — the pattern every
/// Application service already uses — keeps working without any service-layer changes.
/// </summary>
public sealed class D1UnitOfWork : IUnitOfWork
{
    private readonly List<Func<CancellationToken, Task>> _pending = new();

    public void Enqueue(Func<CancellationToken, Task> write) => _pending.Add(write);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var work = _pending.ToList();
        _pending.Clear();

        foreach (var action in work)
        {
            try
            {
                await action(ct);
            }
            catch (D1QueryException ex) when (ex.Message.Contains("UNIQUE constraint failed",
                                                  StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("The change conflicts with existing data.");
            }
        }

        return work.Count;
    }
}