namespace CPCREDO.Application.Common;

public sealed class Result<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static Result<T> Ok(T value) => new() { IsSuccess = true, Value = value };

    public static Result<T> Fail(string code, string message) =>
        new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message };
}
