namespace SarawakBizOps.Api.Services.Common;

/// <summary>
/// Enum values as they travel in query strings and request bodies: the exact member name, case-sensitive.
/// Numbers ("2") and other casings ("high") are refused, so the API only ever accepts what it documents.
/// </summary>
public static class EnumNames
{
    public static bool TryParseName<TEnum>(string value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        return Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out result);
    }

    public static string NameListMessage<TEnum>(string field) where TEnum : struct, Enum
        => $"{field} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";
}
