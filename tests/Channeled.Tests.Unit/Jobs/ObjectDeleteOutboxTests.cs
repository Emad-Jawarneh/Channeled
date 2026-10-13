using System.Text.Json;
using Channeled.Infrastructure.Jobs;
using FluentAssertions;

namespace Channeled.Tests.Unit.Jobs;

public class ObjectDeleteOutboxTests
{
    [Fact]
    public void Parse_round_trips_keys_and_multiparts()
    {
        var payload = new ObjectDeletePayload(
            ["k1", "k2"],
            [new ObjectDeleteMultipart("k1", "upload-1")]);
        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var parsed = ObjectDeleteOutbox.Parse(json);
        parsed.Should().NotBeNull();
        parsed!.Keys.Should().Equal("k1", "k2");
        parsed.Multiparts.Should().ContainSingle(m => m.ObjectKey == "k1" && m.MultipartId == "upload-1");
    }

    [Fact]
    public void Parse_null_or_empty_returns_null()
    {
        ObjectDeleteOutbox.Parse(null).Should().BeNull();
        ObjectDeleteOutbox.Parse("").Should().BeNull();
        ObjectDeleteOutbox.Parse("   ").Should().BeNull();
    }
}
