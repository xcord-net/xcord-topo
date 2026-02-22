namespace XcordTopo;

public sealed record Error
{
    public string Code { get; }
    public string Message { get; }
    public int StatusCode { get; }

    private Error(string code, string message, int statusCode)
    {
        Code = code;
        Message = message;
        StatusCode = statusCode;
    }

    public static Error NotFound(string code, string message) => new(code, message, 404);
    public static Error Validation(string code, string message) => new(code, message, 400);
    public static Error BadRequest(string code, string message) => new(code, message, 400);
    public static Error Conflict(string code, string message) => new(code, message, 409);
    public static Error Failure(string code, string message) => new(code, message, 500);
}
