using Microsoft.Extensions.DependencyInjection;

namespace PersonalTracker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // One handler per field type. To add a new type, add a handler here.
        services.AddSingleton<IFieldTypeHandler>(new TextFieldHandler(FieldType.Text, "Text", 500));
        services.AddSingleton<IFieldTypeHandler>(new TextFieldHandler(FieldType.LongText, "Long text", 20_000));
        services.AddSingleton<IFieldTypeHandler, UrlFieldHandler>();
        services.AddSingleton<IFieldTypeHandler>(new NumberFieldHandler(FieldType.Number, "Number", isCurrency: false));
        services.AddSingleton<IFieldTypeHandler>(new NumberFieldHandler(FieldType.Currency, "Currency", isCurrency: true));
        services.AddSingleton<IFieldTypeHandler, RatingFieldHandler>();
        services.AddSingleton<IFieldTypeHandler, DateFieldHandler>();
        services.AddSingleton<IFieldTypeHandler, DateTimeFieldHandler>();
        services.AddSingleton<IFieldTypeHandler, BooleanFieldHandler>();
        services.AddSingleton<IFieldTypeHandler>(new SelectFieldHandler(multiple: false));
        services.AddSingleton<IFieldTypeHandler>(new SelectFieldHandler(multiple: true));
        services.AddSingleton<IFieldTypeHandler>(new ReferenceFieldHandler(multiple: false));
        services.AddSingleton<IFieldTypeHandler>(new ReferenceFieldHandler(multiple: true));
        services.AddSingleton<IFieldTypeRegistry, FieldTypeRegistry>();

        services.AddScoped<FieldBuilder>();
        services.AddScoped<IReferenceResolver, ReferenceResolver>();
        services.AddScoped<IRecordValueValidator, RecordValueValidator>();
        services.AddScoped<IRecordQueryEngine, RecordQueryEngine>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IFieldService, FieldService>();
        services.AddScoped<ICoverService, CoverService>();
        services.AddScoped<IRecordService, RecordService>();
        return services;
    }
}
