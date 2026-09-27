using System.Globalization;

namespace PersonalTracker.Infrastructure.D1;

/// <summary>Column <-> CLR conversions for D1 rows. Dates are stored as fixed-width UTC ISO-8601 text so SQL ORDER BY sorts them correctly; booleans as 0/1 integers (SQLite has no native bool).</summary>
internal static class D1Convert
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static string ToSql(DateTime utc) =>
        System.DateTime.SpecifyKind(utc, System.DateTimeKind.Utc)
            .ToString(Format, CultureInfo.InvariantCulture);

    public static Guid Guid(IReadOnlyDictionary<string, object?> row, string col) =>
        System.Guid.Parse((string)row[col]!);

    public static string Str(IReadOnlyDictionary<string, object?> row, string col) => (string)row[col]!;
    public static string? StrOrNull(IReadOnlyDictionary<string, object?> row, string col) => row[col] as string;

    public static DateTime DateTime(IReadOnlyDictionary<string, object?> row, string col) =>
        System.DateTime.Parse((string)row[col]!, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public static DateTime? DateTimeOrNull(IReadOnlyDictionary<string, object?> row, string col) =>
        row[col] is string s
            ? System.DateTime.Parse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
            : null;

    public static bool Bool(IReadOnlyDictionary<string, object?> row, string col) =>
        System.Convert.ToInt64(row[col]) != 0;

    public static int Int(IReadOnlyDictionary<string, object?> row, string col) => System.Convert.ToInt32(row[col]);
    public static long Long(IReadOnlyDictionary<string, object?> row, string col) => System.Convert.ToInt64(row[col]);
}