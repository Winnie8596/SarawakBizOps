using System.Text.Json;
using System.Text.Json.Serialization;

namespace SarawakBizOps.Api.Serialization;

/// <summary>
/// Every DateTime the API sends is UTC (PRD Q-02), and says so: "2026-09-29T02:00:00Z". EF Core reads
/// <c>datetime2</c> columns back as <see cref="DateTimeKind.Unspecified"/>, which System.Text.Json would write with
/// no offset at all, and a browser parses an offset-less time as its own local time. Unspecified is therefore taken
/// as UTC (that is all the database holds); a Local value is converted. Applies to <c>DateTime?</c> too.
/// Reading is unchanged: a request's own offset is honoured as before.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        });
}
