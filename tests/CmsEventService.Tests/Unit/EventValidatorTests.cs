using System.Text.Json;

using CmsEventService.Api.Application;

namespace CmsEventService.Tests.Unit;

public class EventValidatorTests
{
    private static bool TryParse(string json, out ParsedEvent? parsed, out string? error) =>
        EventValidator.TryParse(JsonDocument.Parse(json).RootElement, out parsed, out error);

    [Theory]
    [InlineData("publish", EventType.Publish)]
    [InlineData("PUBLISH", EventType.Publish)]
    [InlineData("add", EventType.Publish)]
    [InlineData("update", EventType.Publish)]
    [InlineData("unPublish", EventType.Unpublish)]
    public void Maps_event_types_case_insensitively(string type, EventType expected)
    {
        var ok = TryParse($$"""{ "type": "{{type}}", "id": "A", "version": 1, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""",
            out var parsed, out _);

        Assert.True(ok);
        Assert.Equal(expected, parsed!.Type);
    }

    [Fact]
    public void Delete_needs_neither_version_nor_payload()
    {
        var ok = TryParse("""{ "type": "delete", "id": "Y", "timestamp": "2024-01-01T00:00:00Z" }""", out var parsed, out _);

        Assert.True(ok);
        Assert.Equal(EventType.Delete, parsed!.Type);
        Assert.Null(parsed.Version);
        Assert.Null(parsed.Payload);
    }

    [Fact]
    public void Id_is_trimmed()
    {
        TryParse("""{ "type": "publish", "id": "  A  ", "version": 1, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""",
            out var parsed, out _);

        Assert.Equal("A", parsed!.Id);
    }

    [Fact]
    public void Timestamp_with_offset_is_normalized_to_utc()
    {
        TryParse("""{ "type": "delete", "id": "A", "timestamp": "2024-01-01T02:00:00+02:00" }""", out var parsed, out _);

        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), parsed!.Timestamp);
        Assert.Equal(TimeSpan.Zero, parsed.Timestamp.Offset);
    }

    [Theory]
    [InlineData("""{ "id": "A", "version": 1, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "bogus", "id": "A", "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "version": 1, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "has space", "version": 1, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "<script>", "version": 1, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "A", "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "A", "version": 0, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "A", "version": "2", "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "A", "version": 1.5, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "A", "version": 1, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "A", "version": 1, "payload": "text", "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "unPublish", "id": "A", "version": 1, "timestamp": "2024-01-01T00:00:00Z" }""")]
    [InlineData("""{ "type": "publish", "id": "A", "version": 1, "payload": {} }""")]
    [InlineData("""{ "type": "publish", "id": "A", "version": 1, "payload": {}, "timestamp": "not a date" }""")]
    [InlineData("""{ "type": "delete", "id": "A" }""")]
    [InlineData("\"just a string\"")]
    public void Invalid_events_are_rejected_with_a_reason(string json)
    {
        var ok = TryParse(json, out var parsed, out var error);

        Assert.False(ok);
        Assert.Null(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Oversized_payload_is_rejected()
    {
        var big = new string('x', EventValidator.MaxPayloadLength);

        var ok = TryParse($$"""{ "type": "publish", "id": "A", "version": 1, "payload": { "d": "{{big}}" }, "timestamp": "2024-01-01T00:00:00Z" }""",
            out _, out var error);

        Assert.False(ok);
        Assert.Contains("payload", error);
    }
}
