namespace Channeled.Application.Abstractions.Results;

public sealed class AppResult
{
    private AppResult(bool succeeded, string? code, string? message)
    {
        Succeeded = succeeded;
        Code = code;
        Message = message;
    }

    public bool Succeeded { get; }
    public string? Code { get; }
    public string? Message { get; }

    public static AppResult Ok() => new(true, null, null);

    public static AppResult Fail(string code, string message) => new(false, code, message);
}

public sealed class AppResult<T>
{
    private AppResult(bool succeeded, T? value, string? code, string? message)
    {
        Succeeded = succeeded;
        Value = value;
        Code = code;
        Message = message;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public string? Code { get; }
    public string? Message { get; }

    public static AppResult<T> Ok(T value) => new(true, value, null, null);

    public static AppResult<T> Fail(string code, string message) => new(false, default, code, message);
}
