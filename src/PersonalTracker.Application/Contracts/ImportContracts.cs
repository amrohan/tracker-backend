namespace PersonalTracker.Application.Contracts;

public enum MissingPolicy
{
    Error,
    Skip,
    Create
}

public sealed record ImportMapping(int Column, Guid FieldId, string? DateOrder, bool DecimalComma);

public sealed record ImportOptions(
    bool SkipInvalidRows,
    MissingPolicy MissingReference,
    MissingPolicy MissingOption,
    string? TimeZone);

public sealed record ImportRequest(
    List<ImportMapping> Mappings,
    List<List<string?>> Rows,
    ImportOptions Options,
    bool DryRun);

public sealed record ImportRowError(int Row, string Field, string Message);

public sealed record ImportResult(
    int Total,
    int Valid,
    int Imported,
    int ErrorCount,
    IReadOnlyList<ImportRowError> Errors,
    bool Committed);