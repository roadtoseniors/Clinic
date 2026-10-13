using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Clinic.API;

// Ловит любые необработанные исключения и превращает их в аккуратный JSON-ответ.
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // Клиент сам отменил запрос — это не ошибка сервера
        if (exception is OperationCanceledException)
        {
            context.Response.StatusCode = 499;
            return true;
        }

        var (status, title) = Describe(exception);

        if (status >= 500)
            logger.LogError(exception, "Необработанная ошибка: {Method} {Path}",
                context.Request.Method, context.Request.Path);
        else
            logger.LogWarning("Отклонён запрос {Method} {Path}: {Title}",
                context.Request.Method, context.Request.Path, title);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title }, cancellationToken);
        return true;
    }

    private static (int Status, string Title) Describe(Exception exception)
    {
        // Кривой JSON, не тот тип поля, нет обязательного параметра в запросе
        if (exception is BadHttpRequestException bad)
            return (bad.StatusCode, "Неверный формат запроса: проверьте JSON и параметры");

        var pg = exception as PostgresException ?? exception.InnerException as PostgresException;
        if (pg is not null)
            return pg.SqlState switch
            {
                PostgresErrorCodes.UniqueViolation => (409, "Такая запись уже существует"),
                PostgresErrorCodes.ForeignKeyViolation => (409, "Связанная запись не найдена или используется другими данными"),
                PostgresErrorCodes.NotNullViolation => (400, "Не заполнено обязательное поле"),
                // У триггеров clinic.sql имя ограничения пустое, а текст написан для пользователя
                PostgresErrorCodes.CheckViolation when pg.ConstraintName is null => (400, pg.MessageText),
                PostgresErrorCodes.CheckViolation => (400, "Данные не прошли проверку: недопустимое значение"),
                PostgresErrorCodes.RaiseException => (400, pg.MessageText),
                _ => (500, "Внутренняя ошибка сервера")
            };

        if (exception is DbUpdateException)
            return (409, "Не удалось сохранить изменения");

        return (500, "Внутренняя ошибка сервера");
    }
}
