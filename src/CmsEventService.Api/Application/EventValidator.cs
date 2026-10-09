using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CmsEventService.Api.Application;

/// <summary>
/// Validates and sanitizes a raw event. Works on <see cref="JsonElement"/> so a single malformed
/// event is rejected on its own instead of failing model binding for the whole batch.
/// </summary>
public static partial class EventValidator
{
    /// <summary>Largest accepted payload, in characters.</summary>
    public const int MaxPayloadLength = 256 * 1024;

    /// <summary>Allowed entity ids. Also published in the OpenAPI document.</summary>
    public const string IdRegex = "^[A-Za-z0-9._:-]{1,100}$";

    /// <summary>How far ahead of this service's clock an event timestamp may be.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    [GeneratedRegex(IdRegex)]
    private static partial Regex IdPattern();

    /// <summary>Validates and normalizes a raw event; returns false with the reason when it is invalid.</summary>
    /// <param name="now">Current time, used to reject timestamps in the future.</param>
    public static bool TryParse(JsonElement raw, DateTimeOffset now, out ParsedEvent? parsed, out string? error)
    {
        parsed = null;

        if (raw.ValueKind != JsonValueKind.Object)
            return Fail("event must be a JSON object", out error);

        if (!TryGetString(raw, "type", out var typeText))
            return Fail("'type' is required and must be a string", out error);

        if (!TryMapType(typeText, out var type))
            return Fail($"unknown event type '{Truncate(typeText)}'", out error);

        if (!TryGetString(raw, "id", out var idText))
            return Fail("'id' is required and must be a string", out error);

        var id = idText.Trim();
        if (!IdPattern().IsMatch(id))
            return Fail("'id' must be 1-100 characters of letters, digits, '.', '_', ':' or '-'", out error);

        if (!TryGetTimestamp(raw, out var timestamp))
            return Fail("'timestamp' is required and must be an ISO-8601 date", out error);

        // A future timestamp would make every later event for the entity look stale, freezing it for good.
        if (timestamp > now + MaxClockSkew)
            return Fail("'timestamp' is in the future", out error);

        if (type == EventType.Delete)
        {
            parsed = new ParsedEvent(type, id, null, null, timestamp);
            error = null;
            return true;
        }

        if (!raw.TryGetProperty("version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out var version) ||
            version < 1)
            return Fail("'version' is required and must be an integer >= 1", out error);

        if (!raw.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return Fail("'payload' is required and must be a JSON object", out error);

        var payloadText = payload.GetRawText();
        if (payloadText.Length > MaxPayloadLength)
            return Fail($"'payload' exceeds {MaxPayloadLength} characters", out error);

        parsed = new ParsedEvent(type, id, version, payloadText, timestamp);
        error = null;
        return true;
    }

    // "add" and "update" are named in the scenario; both are the CMS publishing a new version.
    private static bool TryMapType(string value, out EventType type)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "publish":
            case "add":
            case "update":
                type = EventType.Publish;
                return true;
            case "unpublish":
                type = EventType.Unpublish;
                return true;
            case "delete":
                type = EventType.Delete;
                return true;
            default:
                type = default;
                return false;
        }
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        if (element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { Length: > 0 } text)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    // TryGetDateTimeOffset enforces ISO-8601 but reads a value without an offset as server-local time;
    // parsing again with AssumeUniversal treats that case as UTC, whatever the server's time zone.
    private static bool TryGetTimestamp(JsonElement element, out DateTimeOffset timestamp)
    {
        timestamp = default;

        return element.TryGetProperty("timestamp", out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.TryGetDateTimeOffset(out _) &&
            DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }

    private static string Truncate(string value) => value.Length <= 30 ? value : value[..30] + "...";
}
