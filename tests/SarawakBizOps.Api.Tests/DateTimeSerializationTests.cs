using System.Text.Json;
using SarawakBizOps.Api.Serialization;
using SarawakBizOps.Api.Tests.Infrastructure;
using static SarawakBizOps.Api.Tests.Infrastructure.TestData;

namespace SarawakBizOps.Api.Tests;

/// <summary>Every DateTime the API sends carries its UTC offset, so a browser never reads it as its own local time.</summary>
[Collection(ApiCollection.Name)]
public class DateTimeSerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeConverter() } };

    private readonly ApiFactory _factory;

    public DateTimeSerializationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void Each_kind_of_DateTime_is_written_as_UTC_with_a_Z()
    {
        // EF Core reads datetime2 back as Unspecified: it is UTC, it just does not say so.
        Assert.Equal("\"2026-09-29T02:00:00Z\"",
            JsonSerializer.Serialize(new DateTime(2026, 9, 29, 2, 0, 0, DateTimeKind.Unspecified), Options));
        Assert.Equal("\"2026-09-29T02:00:00.5Z\"",
            JsonSerializer.Serialize(new DateTime(2026, 9, 29, 2, 0, 0, 500, DateTimeKind.Utc), Options));

        var local = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Local);
        Assert.Equal(JsonSerializer.Serialize(local.ToUniversalTime(), Options), JsonSerializer.Serialize(local, Options));
    }

    [Fact]
    public void Nullable_DateTimes_are_written_the_same_way()
    {
        Assert.Equal("{\"At\":\"2026-09-29T02:00:00Z\",\"Never\":null}", JsonSerializer.Serialize(
            new { At = (DateTime?)new DateTime(2026, 9, 29, 2, 0, 0), Never = (DateTime?)null }, Options));
    }

    [Fact]
    public async Task A_DateTime_read_from_the_database_goes_out_with_its_offset()
    {
        var admin = await _factory.AdminClientAsync();
        var created = await CreateCustomerAsync(admin);

        using var json = JsonDocument.Parse(await admin.GetStringAsync($"/api/customers/{created.Id}"));

        Assert.EndsWith("Z", json.RootElement.GetProperty("createdAt").GetString());
    }
}
