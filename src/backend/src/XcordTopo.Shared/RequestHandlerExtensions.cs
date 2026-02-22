using Microsoft.AspNetCore.Http;

namespace XcordTopo;

public static class RequestHandlerExtensions
{
    public static async Task<IResult> ExecuteAsync<TRequest, TResponse>(
        this IRequestHandler<TRequest, Result<TResponse>> handler,
        TRequest request,
        CancellationToken ct,
        Func<TResponse, IResult>? onSuccess = null)
    {
        if (handler is IValidatable<TRequest> validatable)
        {
            var error = validatable.Validate(request);
            if (error is not null)
                return Results.Problem(statusCode: error.StatusCode, title: error.Code, detail: error.Message);
        }

        var result = await handler.Handle(request, ct);
        return result.Match(
            success => onSuccess?.Invoke(success) ?? Results.Ok(success),
            err => Results.Problem(statusCode: err.StatusCode, title: err.Code, detail: err.Message));
    }
}
