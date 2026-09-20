namespace PersonalTracker.Application.Common;

/// <summary>Collects validation errors and throws them together as one <see cref="RequestValidationException"/>.</summary>
public sealed class ErrorBag
{
    private readonly Dictionary<string, List<string>> _errors;
    private readonly string _prefix;

    public ErrorBag() : this(new Dictionary<string, List<string>>(), string.Empty) { }

    private ErrorBag(Dictionary<string, List<string>> errors, string prefix)
    {
        _errors = errors;
        _prefix = prefix;
    }

    public bool HasErrors => _errors.Count > 0;

    /// <summary>Returns a view that prefixes every key (used for nested input such as <c>fields[2].</c>).</summary>
    public ErrorBag Scope(string prefix) => new(_errors, _prefix + prefix);

    public void Add(string key, string message)
    {
        var full = _prefix + key;
        if (!_errors.TryGetValue(full, out var list))
        {
            list = new List<string>();
            _errors[full] = list;
        }
        list.Add(message);
    }

    public void ThrowIfAny()
    {
        if (HasErrors)
            throw new RequestValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
