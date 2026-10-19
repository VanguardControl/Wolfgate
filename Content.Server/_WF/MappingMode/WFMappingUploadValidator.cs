using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Checks an uploaded map file before the map loader sees it: UTF-8 text, one YAML document without anchors or
/// aliases and not nested too deep, a map file's root, an entity count within the cap, and only known prototypes.
/// </summary>
public static class WFMappingUploadValidator
{
    /// <summary>Deepest YAML nesting accepted; real map files stay far below it and the parser recurses per level.</summary>
    public const int MaxDepth = 64;

    /// <summary>Unknown prototypes named in a refusal.</summary>
    private const int MaxListed = 5;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Validates a file and parses it. <paramref name="renamed"/> and <paramref name="deleted"/> are the map
    /// migrations the loader applies, so a renamed prototype passes as its new id. False with a Fluent id and its
    /// arguments when it is refused.
    /// </summary>
    public static bool TryValidate(byte[] bytes, IPrototypeManager prototypes,
        IReadOnlyDictionary<string, string> renamed, IReadOnlySet<string> deleted, int maxEntities,
        [NotNullWhen(true)] out MappingDataNode? root, out string error, out (string, object)[] args)
    {
        root = null;
        args = Array.Empty<(string, object)>();

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (ArgumentException)
        {
            error = "wf-mapping-mode-upload-not-text";
            return false;
        }

        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];

        try
        {
            if (!TryScan(text, out error))
                return false;

            var documents = DataNodeParser.ParseYamlStream(new StringReader(text)).ToArray();
            if (documents.Length != 1 ||
                documents[0].Root is not MappingDataNode mapping ||
                !mapping.TryGet<MappingDataNode>("meta", out var meta) ||
                !meta.TryGet<ValueDataNode>("format", out var format) ||
                !int.TryParse(format.Value, out var version) ||
                !mapping.TryGet<SequenceDataNode>("entities", out var entities))
            {
                error = "wf-mapping-mode-upload-not-map";
                return false;
            }

            if (!CheckEntities(entities, version, prototypes, renamed, deleted, maxEntities, out error, out args))
                return false;

            root = mapping;
            return true;
        }
        catch (Exception e) when (e is YamlException or InvalidCastException or NotSupportedException
                                      or InvalidOperationException or ArgumentException)
        {
            error = "wf-mapping-mode-upload-bad-yaml";
            return false;
        }
    }

    /// <summary>Walks the YAML events: one document, no anchor or alias, nesting at most <see cref="MaxDepth"/>.</summary>
    private static bool TryScan(string text, out string error)
    {
        var parser = new Parser(new StringReader(text));
        var documents = 0;
        var depth = 0;
        while (parser.MoveNext())
        {
            switch (parser.Current)
            {
                case AnchorAlias:
                    error = "wf-mapping-mode-upload-anchors";
                    return false;
                case NodeEvent node when !node.Anchor.IsEmpty:
                    error = "wf-mapping-mode-upload-anchors";
                    return false;
                case DocumentStart:
                    if (++documents > 1)
                    {
                        error = "wf-mapping-mode-upload-not-map";
                        return false;
                    }

                    break;
                case MappingStart:
                case SequenceStart:
                    if (++depth > MaxDepth)
                    {
                        error = "wf-mapping-mode-upload-too-deep";
                        return false;
                    }

                    break;
                case MappingEnd:
                case SequenceEnd:
                    depth--;
                    break;
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Counts the entities and refuses unknown or abstract prototypes, naming up to <see cref="MaxListed"/>.</summary>
    private static bool CheckEntities(SequenceDataNode entities, int version, IPrototypeManager prototypes,
        IReadOnlyDictionary<string, string> renamed, IReadOnlySet<string> deleted, int maxEntities, out string error,
        out (string, object)[] args)
    {
        args = Array.Empty<(string, object)>();
        var key = version >= 4 ? "proto" : "type";
        var count = 0;
        var unknown = new List<string>();
        foreach (var node in entities)
        {
            if (node is not MappingDataNode group)
            {
                error = "wf-mapping-mode-upload-not-map";
                return false;
            }

            count += group.TryGet<SequenceDataNode>("entities", out var members) ? members.Count : 1;
            if (!group.TryGet<ValueDataNode>(key, out var protoNode) || string.IsNullOrWhiteSpace(protoNode.Value))
                continue;

            var id = renamed.TryGetValue(protoNode.Value, out var newId) ? newId : protoNode.Value;
            if (deleted.Contains(id))
                continue;

            if ((!prototypes.TryIndex<EntityPrototype>(id, out var prototype) || prototype.Abstract) &&
                !unknown.Contains(protoNode.Value))
            {
                unknown.Add(protoNode.Value);
            }
        }

        if (count > maxEntities)
        {
            error = "wf-mapping-mode-limit-entities";
            args = new (string, object)[] { ("max", maxEntities) };
            return false;
        }

        if (unknown.Count > 0)
        {
            error = "wf-mapping-mode-upload-unknown-prototypes";
            args = new (string, object)[]
            {
                ("count", unknown.Count),
                ("ids", string.Join(", ", unknown.Take(MaxListed))),
            };
            return false;
        }

        error = string.Empty;
        return true;
    }
}
