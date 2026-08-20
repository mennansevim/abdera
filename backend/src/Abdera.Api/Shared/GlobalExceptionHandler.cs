using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// Tüm modüllerin ortak hata sözleşmesi: RFC 7807 ProblemDetails. Handler'lar Shared/ApiExceptions.cs
// içindeki istisnaları fırlatır, burada tek yerden HTTP status koduna çevrilir.
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Kayıt bulunamadı"),
            ValidationFailedException => (StatusCodes.Status400BadRequest, "Doğrulama hatası"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Yetkisiz işlem"),
            ConflictException => (StatusCodes.Status409Conflict, "Çakışma"),
            // ARC-1 (docs/13-audit-fix-prompt.md): xmin tabanlı optimistic concurrency
            // (bkz. ReceivableConfiguration/BankIncomingTransactionConfiguration) bir kaydın
            // okunduktan sonra başka bir işlemce değiştirildiğini burada yakalar - sessizce
            // ezmek yerine 409 döner, istemci güncel veriyi çekip tekrar denemeli.
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Kayıt başka bir işlemce güncellendi"),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu"),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Beklenmeyen hata: {Path}", httpContext.Request.Path);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
        };

        if (exception is ValidationFailedException validationFailed)
        {
            problemDetails.Extensions["errors"] = validationFailed.Errors;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
