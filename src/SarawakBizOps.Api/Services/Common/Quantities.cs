namespace SarawakBizOps.Api.Services.Common;

/// <summary>
/// Stock quantities are decimal(18,3) in the database (litres, metres and kg as well as whole pieces).
/// A value with more places would be silently rounded by SQL Server, so it is refused up front instead.
/// </summary>
public static class Quantities
{
    public const int Scale = 3;

    /// <summary>A sanity ceiling well inside decimal(18,x), so an absurd value is a 400 rather than a SQL overflow.</summary>
    public const decimal Max = 1_000_000_000m;

    public static bool HasAtMostDecimals(decimal value, int places) => decimal.Round(value, places) == value;
}
