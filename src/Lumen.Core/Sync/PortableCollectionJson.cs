// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lumen.Core.Library;

namespace Lumen.Core.Sync;

/// <summary>The outcome of reading one <c>collections/{id}.json</c>.</summary>
public sealed record PortableCollectionParse(Collection? Collection, IReadOnlyList<string> Problems, bool NewerSchema)
{
    public bool IsValid => Collection is not null;
}

/// <summary>
/// Reads and writes a collection file (<c>docs/schemas/collection.schema.json</c>). The file name is the id. A name
/// written per language is read as one name (English first), and rewritten only when it really changed. <c>icon</c>
/// and <c>sort</c> stay as the file had them.
/// </summary>
public static partial class PortableCollectionJson
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    public static PortableCollectionParse Parse(string? json, string fileId)
    {
        var root = PortableConfigJson.ParseObject(json);
        if (root is null)
        {
            return new PortableCollectionParse(null, ["Not a JSON object."], false);
        }

        if (root["schema"] is not JsonValue schemaNode || !schemaNode.TryGetValue<int>(out var schema) || schema < 1)
        {
            return new PortableCollectionParse(null, ["\"schema\" must be a whole number, 1 or more."], false);
        }

        if (schema > 1)
        {
            return new PortableCollectionParse(null, [$"Written by a newer Lumen (schema {schema})."], true);
        }

        var id = root["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var text) ? text : null;
        if (!IsValidId(id) || id != fileId)
        {
            return new PortableCollectionParse(null, [$"\"id\" must be a valid id equal to the file name (\"{fileId}\")."], false);
        }

        var name = Collection.CleanName(ResolveName(root["name"]));
        if (name is null)
        {
            return new PortableCollectionParse(null, ["\"name\" is missing or empty."], false);
        }

        var problems = new List<string>();
        var items = new List<ItemRef>();
        if (root["items"] is not JsonArray array)
        {
            return new PortableCollectionParse(null, ["\"items\" must be a list."], false);
        }

        foreach (var node in array)
        {
            if (node is JsonValue v && v.TryGetValue<string>(out var s) && ItemRef.TryParse(s, out var itemRef))
            {
                if (!items.Contains(itemRef))
                {
                    items.Add(itemRef);
                }
            }
            else
            {
                problems.Add("An item is not a source:id reference; skipped.");
            }
        }

        return new PortableCollectionParse(new Collection(id!, name, items), problems, false);
    }

    public static string Write(Collection collection, string? existingJson = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var existing = PortableConfigJson.ParseObject(existingJson) ?? [];
        var root = new JsonObject
        {
            ["schema"] = 1,
            ["id"] = collection.Id,
        };

        // Keep a name written per language unless it no longer says what Lumen has.
        root["name"] = existing["name"] is { } oldName && Collection.CleanName(ResolveName(oldName)) == collection.Name
            ? oldName.DeepClone()
            : collection.Name;

        foreach (var (key, value) in existing)
        {
            if (key is not ("schema" or "id" or "name" or "items"))
            {
                root[key] = value?.DeepClone();
            }
        }

        root["items"] = new JsonArray(collection.Items.Select(i => (JsonNode?)JsonValue.Create(i.ToString())).ToArray());
        return root.ToJsonString(Pretty) + "\n";
    }

    /// <summary>
    /// SHA-256 of what Lumen manages. Favorites ignore the name (it is only a label), so a favorites file named in
    /// another language is the same collection.
    /// </summary>
    public static string Hash(Collection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return PortableConfigJson.HashText(Write(collection.IsFavorites ? collection with { Name = "Favorites" } : collection));
    }

    private static string? ResolveName(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var text):
                return text;
            case JsonObject obj:
                var english = obj["en"] is JsonValue e && e.TryGetValue<string>(out var en) ? en : null;
                return english ?? obj.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => kv.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
                    .FirstOrDefault(s => s is not null);
            default:
                return null;
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex IdPattern();
}
