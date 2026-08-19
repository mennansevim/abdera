namespace Abdera.Api.Shared;

// Ortak hata modeli - controller/handler'lar HTTP status kodunu bilmek zorunda kalmadan
// bu istisnaları fırlatır, GlobalExceptionHandler bunları ProblemDetails'e çevirir.

public class NotFoundException(string message) : Exception(message);

public class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Doğrulama hatası.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public class ForbiddenException(string message) : Exception(message);

public class ConflictException(string message) : Exception(message);
