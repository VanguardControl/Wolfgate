using System.Numerics;
using System.Text.Json;
using Content.Server.Administration.Logs.Converters;

namespace Content.Server._WG.LogInvestigator;

/// <summary>Writes a Vector2 in a log's JSON as x and y; without it the fields came out as {}.</summary>
[AdminLogConverter]
public sealed class WGVector2LogConverter : AdminLogConverter<Vector2>
{
    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", MathF.Round(value.X, 1));
        writer.WriteNumber("y", MathF.Round(value.Y, 1));
        writer.WriteEndObject();
    }
}
