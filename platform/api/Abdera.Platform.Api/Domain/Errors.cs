namespace Abdera.Platform.Api.Domain;

// Okul uygulamasındaki hata modeliyle aynı sözleşme: 400 doğrulama, 404 yok, 409 çakışma.
public class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors) : Exception("Doğrulama hatası.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationFailedException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

public class NotFoundException(string message) : Exception(message);

public class ConflictException(string message) : Exception(message);
