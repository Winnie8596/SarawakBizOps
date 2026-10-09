namespace SarawakBizOps.Api.DTOs;

/// <summary>
/// A request that carries the record's concurrency token (Phase 5: a required body field on every mutating call).
/// <see cref="Filters.RowVersionFilter"/> refuses the request with a 400 before the action runs unless
/// <see cref="RowVersion"/> is valid base64, so an action reads <see cref="RowVersionRequestExtensions.RowVersionBytes"/>
/// without checking it again.
/// </summary>
public interface IRowVersionRequest
{
    string RowVersion { get; }
}

public static class RowVersionRequestExtensions
{
    public static byte[] RowVersionBytes(this IRowVersionRequest request) => Convert.FromBase64String(request.RowVersion);
}
