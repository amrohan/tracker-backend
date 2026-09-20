namespace PersonalTracker.Application.Common;

public static class Rules
{
    public static string Required(string? value, int max, string key, string label, ErrorBag errors)
    {
        var v = value?.Trim() ?? string.Empty;
        if (v.Length == 0) errors.Add(key, $"{label} is required.");
        else if (v.Length > max) errors.Add(key, $"{label} must be at most {max} characters.");
        return v;
    }

    public static string? Optional(string? value, int max, string key, string label, ErrorBag errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) errors.Add(key, $"{label} must be at most {max} characters.");
        return v;
    }
}
