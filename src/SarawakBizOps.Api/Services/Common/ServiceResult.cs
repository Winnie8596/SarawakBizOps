namespace SarawakBizOps.Api.Services.Common;

public enum ServiceErrorKind
{
    /// <summary>The target record does not exist (HTTP 404).</summary>
    NotFound,
    /// <summary>The request is invalid, e.g. weak password, unknown role (HTTP 400).</summary>
    Validation,
    /// <summary>A business rule forbids the change in the current state (HTTP 409).</summary>
    Conflict
}

/// <summary>
/// Outcome of a service call that can fail for expected, business reasons.
/// Controllers translate the kind into a ProblemDetails response; services
/// never deal with HTTP.
/// </summary>
public class ServiceResult
{
    public ServiceErrorKind? ErrorKind { get; protected init; }
    public string? ErrorMessage { get; protected init; }
    public bool Succeeded => ErrorKind is null;

    public static ServiceResult Ok() => new();
    public static ServiceResult Fail(ServiceErrorKind kind, string message)
        => new() { ErrorKind = kind, ErrorMessage = message };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Value { get; private init; }

    public static ServiceResult<T> Ok(T value) => new() { Value = value };
    public new static ServiceResult<T> Fail(ServiceErrorKind kind, string message)
        => new() { ErrorKind = kind, ErrorMessage = message };
}
