namespace PersonalTracker.Application.Fields;

/// <summary>Validates a <see cref="FieldInput"/> and turns it into a <see cref="Field"/>. Shared by collection creation and field creation.</summary>
public sealed class FieldBuilder(IFieldTypeRegistry registry)
{
    public Field Build(
        Guid collectionId, FieldInput input, int sortOrder, IReadOnlyCollection<Field> siblings,
        ConfigContext context, ErrorBag errors, DateTime now)
    {
        var name = Rules.Required(input.Name, 80, "name", "Field name", errors);
        var description = Rules.Optional(input.Description, 300, "description", "Description", errors);

        if (name.Length > 0 && siblings.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            errors.Add("name", $"A field named '{name}' already exists.");

        var field = new Field
        {
            CollectionId = collectionId,
            Name = name,
            Description = description,
            Type = input.Type,
            Required = input.Required && input.Type != FieldType.Boolean,
            SortOrder = sortOrder,
            IsTitle = input.IsTitle,
            ShowInList = input.ShowInList,
            CreatedAt = now
        };

        if (!Enum.IsDefined(input.Type))
        {
            errors.Add("type", "Unknown field type.");
            return field;
        }

        var handler = registry.Get(input.Type);
        field.Config = handler.NormalizeConfig(input.Config, context, errors);
        field.Key = FieldKeys.Generate(name.Length > 0 ? name : "field", siblings.Select(s => s.Key));

        if (input.Aggregation != AggregationType.None && !handler.Aggregations.Contains(input.Aggregation))
            errors.Add("aggregation", $"{input.Aggregation} is not available for {handler.Label} fields.");
        else
            field.Aggregation = input.Aggregation;

        return field;
    }
}
