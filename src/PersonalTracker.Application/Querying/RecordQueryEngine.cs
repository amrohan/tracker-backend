using System.Text;

namespace PersonalTracker.Application.Querying;

public interface IRecordQueryEngine
{
    /// <summary>Applies filters, search and sorting (not paging) and returns every matching row.</summary>
    Task<IReadOnlyList<RecordRow>> ExecuteAsync(
        Guid userId, IReadOnlyList<Field> fields, IEnumerable<TrackerRecord> records,
        RecordQuery query, CancellationToken ct);
}

/// <summary>
/// In-memory query pipeline over JSON records. It sits behind an interface so it can be replaced by a
/// SQL implementation (json_extract / jsonb) without touching endpoints or services.
/// </summary>
public sealed class RecordQueryEngine(IFieldTypeRegistry registry, IReferenceResolver resolver) : IRecordQueryEngine
{
    public async Task<IReadOnlyList<RecordRow>> ExecuteAsync(
        Guid userId, IReadOnlyList<Field> fields, IEnumerable<TrackerRecord> records,
        RecordQuery query, CancellationToken ct)
    {
        if (query.Filters.Count > Limits.MaxFilters)
            throw FilterEvaluator.Invalid($"You can use at most {Limits.MaxFilters} filters at once.");

        var rows = records.Select(r => new RecordRow(r, RecordData.Parse(r.DataJson))).ToList();
        var fieldsById = fields.ToDictionary(f => f.Id);

        // 1) field filters
        IEnumerable<RecordRow> current = rows;
        foreach (var filter in query.Filters)
        {
            if (!fieldsById.TryGetValue(filter.FieldId, out var field))
                throw FilterEvaluator.Invalid("A filter refers to a field that no longer exists.");
            current = current.Where(FilterEvaluator.Compile(field, registry.Get(field.Type), filter));
        }
        var filtered = current.ToList();

        // 2) labels (only when search or sort needs reference labels)
        var tokens = SplitTokens(query.Search);
        var referenceFields = fields.Where(f => registry.Get(f.Type).IsReference).ToList();
        var sortField = ResolveSortField(query, fieldsById);
        var needLabels = (tokens.Count > 0 && referenceFields.Count > 0) ||
                         (sortField is not null && registry.Get(sortField.Type).IsReference);

        var labels = new Dictionary<Guid, RecordReferenceDto>();
        if (needLabels)
        {
            var ids = filtered.SelectMany(r => ReferenceIds.From(r, referenceFields)).Distinct().ToList();
            labels = await resolver.ResolveAsync(userId, ids, ct);
        }

        // 3) search
        if (tokens.Count > 0)
            filtered = filtered.Where(r => MatchesSearch(r, fields, tokens, labels)).ToList();

        // 4) sort
        var descending = query.SortDirection == SortDirection.Desc;
        IEnumerable<RecordRow> sorted;
        if (sortField is null)
        {
            var byUpdated = string.Equals(query.SortBy, "updatedAt", StringComparison.OrdinalIgnoreCase);
            sorted = byUpdated
                ? (descending ? filtered.OrderByDescending(r => r.Entity.UpdatedAt) : filtered.OrderBy(r => r.Entity.UpdatedAt))
                : (descending ? filtered.OrderByDescending(r => r.Entity.CreatedAt) : filtered.OrderBy(r => r.Entity.CreatedAt));
        }
        else
        {
            var handler = registry.Get(sortField.Type);
            sorted = filtered
                .OrderBy(r => SortKey(r, sortField, handler, labels), new NullsLastComparer(descending))
                .ThenByDescending(r => r.Entity.CreatedAt);
        }

        return sorted.ToList();
    }

    private Field? ResolveSortField(RecordQuery query, Dictionary<Guid, Field> fieldsById)
    {
        var sortBy = query.SortBy;
        if (string.IsNullOrWhiteSpace(sortBy) ||
            sortBy.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ||
            sortBy.Equals("updatedAt", StringComparison.OrdinalIgnoreCase)) return null;

        if (!Guid.TryParse(sortBy, out var id) || !fieldsById.TryGetValue(id, out var field))
            throw new RequestValidationException(new Dictionary<string, string[]> { ["sortBy"] = ["Unknown sort field."] });

        if (!FilterOperators.IsSortable(registry.Get(field.Type).Kind))
            throw new RequestValidationException(new Dictionary<string, string[]> { ["sortBy"] = [$"'{field.Name}' cannot be sorted."] });
        return field;
    }

    private static List<string> SplitTokens(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? new List<string>()
            : search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(10).ToList();

    private bool MatchesSearch(RecordRow row, IReadOnlyList<Field> fields, List<string> tokens, Dictionary<Guid, RecordReferenceDto> labels)
    {
        var sb = new StringBuilder();
        foreach (var field in fields)
        {
            var handler = registry.Get(field.Type);
            if (!handler.IsSearchable || row.Get(field.Key) is not { } value) continue;

            if (handler.IsReference)
            {
                foreach (var s in JsonValues.Strings(value))
                    if (Guid.TryParse(s, out var id) && labels.TryGetValue(id, out var label))
                        sb.Append(label.Label).Append('\n');
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in JsonValues.Strings(value)) sb.Append(s).Append('\n');
            }
            else
            {
                sb.Append(handler.ToDisplayText(value, field)).Append('\n');
            }
        }

        var haystack = sb.ToString();
        return tokens.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static IComparable? SortKey(
        RecordRow row, Field field, IFieldTypeHandler handler, Dictionary<Guid, RecordReferenceDto> labels)
    {
        if (row.Get(field.Key) is not { } v) return null;

        switch (handler.Kind)
        {
            case ValueKind.Text:
                return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            case ValueKind.Number:
                if (JsonValues.TryGetDecimal(v, out var number)) return number;
                return null;
            case ValueKind.Date:
                if (JsonValues.TryGetDate(v, out var date)) return date;
                return null;
            case ValueKind.DateTime:
                if (JsonValues.TryGetInstant(v, out var instant)) return instant;
                return null;
            case ValueKind.Boolean:
                return v.ValueKind == JsonValueKind.True;
            case ValueKind.Choice:
                if (handler.IsReference)
                {
                    if (Guid.TryParse(v.GetString(), out var id) && labels.TryGetValue(id, out var label)) return label.Label;
                    return null;
                }
                return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            default:
                return null;
        }
    }

    private sealed class NullsLastComparer(bool descending) : IComparer<IComparable?>
    {
        public int Compare(IComparable? x, IComparable? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return 1;   // empty values are always last, in both directions
            if (y is null) return -1;
            var c = x is string a && y is string b
                ? string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
                : x.CompareTo(y);
            return descending ? -c : c;
        }
    }
}
