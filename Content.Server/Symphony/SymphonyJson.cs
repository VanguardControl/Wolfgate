using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server.Symphony;

/// <summary>
/// Turns the game's serialized data (YAML data nodes) into JSON for SSymphony and back. Scalars stay strings, as in
/// YAML, and a node's type tag rides along as a "!type" key, so the round trip loses nothing.
/// </summary>
public static class SymphonyJson
{
    public const string TagKey = "!type";

    public static JsonNode? ToJson(DataNode node)
    {
        switch (node)
        {
            case MappingDataNode mapping:
            {
                var obj = new JsonObject();
                if (mapping.Tag != null)
                    obj[TagKey] = mapping.Tag;

                foreach (var (key, value) in mapping)
                {
                    obj[key] = ToJson(value);
                }

                return obj;
            }
            case SequenceDataNode sequence:
            {
                var array = new JsonArray();
                foreach (var item in sequence.Sequence)
                {
                    array.Add(ToJson(item));
                }

                return array;
            }
            case ValueDataNode value:
            {
                if (value.Tag != null)
                    return new JsonObject { [TagKey] = value.Tag, ["value"] = value.Value };

                return value.IsNull ? null : JsonValue.Create(value.Value);
            }
            default:
                return null;
        }
    }

    /// <summary>The reverse of <see cref="ToJson"/>. Numbers and booleans sent as JSON are taken as their text.</summary>
    public static DataNode ToNode(JsonNode? json)
    {
        switch (json)
        {
            case null:
                return ValueDataNode.Null();
            case JsonObject obj:
            {
                if (obj.Count == 2 && obj.TryGetPropertyValue(TagKey, out var tag) && obj.TryGetPropertyValue("value", out var tagged))
                    return new ValueDataNode(Scalar(tagged)) { Tag = Scalar(tag) };

                var mapping = new MappingDataNode();
                foreach (var (key, value) in obj)
                {
                    if (key == TagKey)
                    {
                        mapping.Tag = Scalar(value);
                        continue;
                    }

                    mapping.Add(key, ToNode(value));
                }

                return mapping;
            }
            case JsonArray array:
            {
                var sequence = new SequenceDataNode();
                foreach (var item in array)
                {
                    sequence.Add(ToNode(item));
                }

                return sequence;
            }
            default:
                return new ValueDataNode(Scalar(json));
        }
    }

    private static string? Scalar(JsonNode? json)
    {
        if (json is not JsonValue value)
            return json?.ToJsonString();

        return value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
    }

    /// <summary>A short, stable fingerprint of some JSON, used as an ETag.</summary>
    public static string Fingerprint(JsonNode? json)
    {
        return Fingerprint(Encoding.UTF8.GetBytes(json?.ToJsonString() ?? "null"));
    }

    public static string Fingerprint(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
    }

    /// <summary>A JSON error body: {"error": code, "detail": detail}.</summary>
    public static JsonObject Error(string code, string? detail = null)
    {
        var body = new JsonObject { ["error"] = code };
        if (detail != null)
            body["detail"] = detail;

        return body;
    }
}
