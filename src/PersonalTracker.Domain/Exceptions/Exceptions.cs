namespace PersonalTracker.Domain.Exceptions;

public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string message) : AppException(message);

public sealed class AuthenticationFailedException(string message) : AppException(message);

public sealed class ConflictException(string message, IReadOnlyDictionary<string, object?>? details = null) : AppException(message)
{
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
}

public sealed class RequestValidationException(IDictionary<string, string[]> errors) : AppException("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
