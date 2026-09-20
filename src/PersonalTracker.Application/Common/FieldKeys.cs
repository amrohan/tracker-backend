using System.Globalization;
using System.Text;

namespace PersonalTracker.Application.Common;

public static class FieldKeys
{
    /// <summary>Creates a stable, unique, lower_snake_case key from a display name.</summary>
    public static string Generate(string name, IEnumerable<string> existingKeys)
    {
        var sb = new StringBuilder();
        var lastWasSeparator = false;
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && sb.Length > 0)
            {
                sb.Append('_');
                lastWasSeparator = true;
            }
        }

        var slug = sb.ToString().Trim('_');
        if (slug.Length == 0) slug = "field";
        if (char.IsAsciiDigit(slug[0])) slug = "f_" + slug;
        if (slug.Length > 60) slug = slug[..60].TrimEnd('_');

        var used = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);
        var key = slug;
        var n = 2;
        while (!used.Add(key)) key = $"{slug}_{n++}";
        return key;
    }
}
