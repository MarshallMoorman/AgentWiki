using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentWiki.Core.Generation;

/// <summary>
/// JSON helpers tolerant of common LLM shape drift (string vs object vs array).
/// </summary>
public static class LlmJson
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new FlexibleStringConverter(),
            new FlexibleStringListConverter()
        }
    };

    /// <summary>
    /// Extracts a JSON object or array payload from model output (fences / prose).
    /// </summary>
    public static string ExtractPayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("LLM returned empty content.");
        }

        var text = raw.Trim().TrimStart('\uFEFF');
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
            {
                text = text[(firstNewline + 1)..];
            }

            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }

            text = text.Trim();
        }

        var objStart = text.IndexOf('{');
        var arrStart = text.IndexOf('[');
        if (objStart < 0 && arrStart < 0)
        {
            throw new InvalidOperationException(
                "LLM response did not contain JSON. Preview: " + Preview(text, 200));
        }

        if (arrStart >= 0 && (objStart < 0 || arrStart < objStart))
        {
            var end = text.LastIndexOf(']');
            if (end <= arrStart)
            {
                throw new InvalidOperationException("LLM response contained an incomplete JSON array.");
            }

            return text[arrStart..(end + 1)];
        }

        var start = text.IndexOf('{');
        var endObj = text.LastIndexOf('}');
        if (start < 0 || endObj <= start)
        {
            throw new InvalidOperationException("LLM response did not contain a JSON object.");
        }

        return text[start..(endObj + 1)];
    }

    public static string Preview(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(empty)";
        }

        var flat = text.TrimStart().Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    /// <summary>
    /// Reads a string-ish property from a JSON object, accepting common aliases and nested objects.
    /// </summary>
    public static string? ReadStringish(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(obj, name, out var prop))
            {
                continue;
            }

            var value = FlexibleStringConverter.TokenToString(prop);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    public static List<string> ReadStringList(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(obj, name, out var prop))
            {
                continue;
            }

            return FlexibleStringListConverter.TokenToList(prop);
        }

        return [];
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

/// <summary>Deserializes JSON strings, numbers, objects, or arrays into a single string.</summary>
public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return TokenToString(doc.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    public static string TokenToString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            JsonValueKind.Object => FlattenObject(element),
            JsonValueKind.Array => string.Join("; ", element.EnumerateArray().Select(TokenToString).Where(s => s.Length > 0)),
            _ => element.GetRawText()
        };

    private static string FlattenObject(JsonElement obj)
    {
        // Prefer common text fields when the model wraps a string in an object.
        foreach (var key in new[] { "text", "value", "description", "summary", "content", "purpose", "name" })
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                {
                    return prop.Value.GetString()!;
                }
            }
        }

        var sb = new StringBuilder();
        foreach (var prop in obj.EnumerateObject())
        {
            var part = TokenToString(prop.Value);
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append("; ");
            }

            sb.Append(prop.Name).Append(": ").Append(part);
        }

        return sb.ToString();
    }
}

/// <summary>Deserializes JSON arrays/objects/strings into <see cref="List{String}"/>.</summary>
public sealed class FlexibleStringListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return TokenToList(doc.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }

    public static List<string> TokenToList(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
            {
                var list = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    var s = FlexibleStringConverter.TokenToString(item);
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        list.Add(s);
                    }
                }

                return list;
            }
            case JsonValueKind.String:
            {
                var s = element.GetString();
                return string.IsNullOrWhiteSpace(s) ? [] : [s];
            }
            case JsonValueKind.Object:
            {
                // Object map → "key: value" entries, or nested text field only.
                var asText = FlexibleStringConverter.TokenToString(element);
                return string.IsNullOrWhiteSpace(asText) ? [] : [asText];
            }
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return [];
            default:
            {
                var s = FlexibleStringConverter.TokenToString(element);
                return string.IsNullOrWhiteSpace(s) ? [] : [s];
            }
        }
    }
}
