using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Clinic.API;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // Клиент сам отменил запрос — это не ошибка сервера
        if (exception is OperationCanceledException)
        {
            context.Response.StatusCode = 499;
            return true;
        }

        var pg = exception as PostgresException ?? exception.InnerException as PostgresException;
        var (status, title) = pg is null
            ? (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера")
            : Describe(pg);

        if (status >= 500)
            logger.LogError(exception, "Необработанная ошибка: {Method} {Path}",
                context.Request.Method, context.Request.Path);
        else
            logger.LogWarning(exception, "Отклонён запрос: {Method} {Path}: {Title}",
                context.Request.Method, context.Request.Path, title);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title }, cancellationToken);
        return true;
    }

    private static (int Status, string Title) Describe(PostgresException pg) => pg.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation     => (409, "Такая запись уже существует"),
        PostgresErrorCodes.ForeignKeyViolation => (409, "Связанная запись не найдена или используется другими данными"),
        PostgresErrorCodes.NotNullViolation    => (400, "Не заполнено обязательное поле"),
        // у ограничений CHECK имя заполнено, а у наших триггеров нет: их текст предназначен пользователю
        PostgresErrorCodes.CheckViolation when pg.ConstraintName is null => (400, pg.MessageText),
        PostgresErrorCodes.CheckViolation      => (400, "Данные не прошли проверку: недопустимое значение"),
        _ => (500, "Внутренняя ошибка сервера")
    };
}