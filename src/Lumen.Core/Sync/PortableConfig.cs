// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumen.Core.Library;
using Lumen.Core.Shell;

namespace Lumen.Core.Sync;

/// <summary>
/// The part of <c>config.json</c> (<c>docs/schemas/config.schema.json</c>) that Lumen reads and writes. Anything else
/// in the file (store switches, accents, artwork choices…) is kept as it is, never dropped.
/// A <c>null</c> value means "not set here": reading leaves this PC's value alone, writing leaves the file's value alone.
/// </summary>
public sealed record PortableConfig
{
    public string? Language { get; init; }

    public bool? MinimizeToTray { get; init; }

    public string? Hotkey { get; init; }

    public IReadOnlyList<ManualItem> ManualItems { get; init; } = [];

    public IReadOnlyDictionary<ItemRef, ItemOverride> Overrides { get; init; } = new Dictionary<ItemRef, ItemOverride>();

    public IReadOnlyList<ItemRef> IgnoredApps { get; init; } = [];

    /// <summary>"stable" or "beta" (<c>updates.channel</c>); null when this PC never chose one.</summary>
    public string? UpdateChannel { get; init; }

    /// <summary><c>updates.checkAutomatically</c>; null when this PC never chose.</summary>
    public bool? CheckUpdatesAutomatically { get; init; }

    /// <summary><c>ui.dynamicAccent</c>: the game's artwork tints the accent; null when this PC never chose.</summary>
    public bool? DynamicAccent { get; init; }

    /// <summary>True when there is something the user made (manual items, renames, hidden games, ignored apps), as opposed to plain preferences.</summary>
    public bool HasUserData => ManualItems.Count > 0 || Overrides.Count > 0 || IgnoredApps.Count > 0;

    /// <summary>Any setting is set here (language, window, shortcut, updates): with one, the config is worth sharing.</summary>
    public bool HasSettings => Language is not null || MinimizeToTray is not null || Hotkey is not null
        || UpdateChannel is not null || CheckUpdatesAutomatically is not null || DynamicAccent is not null;
}

/// <summary>The outcome of reading a <c>config.json</c>.</summary>
/// <param name="Config">The usable content; null when the file can't be used at all.</param>
/// <param name="Problems">Technical English notes: entries that were skipped, or why the file is unusable.</param>
/// <param name="NewerSchema">The file was written by a newer Lumen: it is left alone, never rewritten.</param>
public sealed record PortableConfigParse(PortableConfig? Config, IReadOnlyList<string> Problems, bool NewerSchema)
{
    public bool IsValid => Config is not null;
}

/// <summary>
/// Reads and writes <see cref="PortableConfig"/> as <c>config.json</c>. Reading is strict about what Lumen uses (a bad
/// manual item is skipped and reported, with the same rules as the "add item" dialog) and ignores the rest. Writing
/// keeps every key Lumen doesn't manage and is deterministic, so the same content always gives the same text.
/// </summary>
public static class PortableConfigJson
{
    private static readonly string[] Languages = ["system", "en", "es"];

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static PortableConfigParse Parse(string? json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json ?? string.Empty) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new PortableConfigParse(null, [$"Not valid JSON: {ex.Message}"], false);
        }

        if (root is null)
        {
            return new PortableConfigParse(null, ["The file must hold a JSON object."], false);
        }

        if (root["schema"] is not JsonValue schemaNode || !schemaNode.TryGetValue<int>(out var schema) || schema < 1)
        {
            return new PortableConfigParse(null, ["\"schema\" must be a whole number, 1 or more."], false);
        }

        if (schema > 1)
        {
            return new PortableConfigParse(null, [$"Written by a newer Lumen (schema {schema}); this version only reads schema 1."], true);
        }

        var problems = new List<string>();
        var config = new PortableConfig
        {
            Language = ReadLanguage(root, problems),
            MinimizeToTray = ReadBool(root["ui"], "minimizeToTray", problems),
            Hotkey = ReadHotkey(root, problems),
            ManualItems = ReadManualItems(root["manualItems"], problems),
            Overrides = ReadOverrides(root["overrides"], problems),
            IgnoredApps = ReadIgnoredApps(root["ignoredApps"], problems),
            UpdateChannel = ReadUpdateChannel(root, problems),
            CheckUpdatesAutomatically = ReadBool(root["updates"], "checkAutomatically", problems),
            DynamicAccent = ReadBool(root["ui"], "dynamicAccent", problems),
        };
        return new PortableConfigParse(config, problems, false);
    }

    /// <summary>
    /// The text of <c>config.json</c> for <paramref name="config"/>. Keys Lumen doesn't manage are copied from
    /// <paramref name="existingJson"/> (when it parses), so a round trip never loses what another version wrote.
    /// </summary>
    public static string Write(PortableConfig config, string? existingJson = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var existing = ParseObject(existingJson) ?? [];
        var root = new JsonObject { ["schema"] = 1 };

        SetOrKeep(root, existing, "language", config.Language is null ? null : JsonValue.Create(config.Language));

        var ui = existing["ui"] is JsonObject oldUi ? (JsonObject)oldUi.DeepClone() : [];
        if (config.MinimizeToTray is { } minimize)
        {
            ui["minimizeToTray"] = minimize;
        }

        if (config.DynamicAccent is { } accent)
        {
            ui["dynamicAccent"] = accent;
        }

        if (ui.Count > 0)
        {
            root["ui"] = ui;
        }

        var quick = existing["quickSearch"] is JsonObject oldQuick ? (JsonObject)oldQuick.DeepClone() : [];
        if (config.Hotkey is not null)
        {
            quick["hotkey"] = config.Hotkey;
        }

        if (quick.Count > 0)
        {
            root["quickSearch"] = quick;
        }

        var updates = existing["updates"] is JsonObject oldUpdates ? (JsonObject)oldUpdates.DeepClone() : [];
        if (config.UpdateChannel is not null)
        {
            updates["channel"] = config.UpdateChannel;
        }

        if (config.CheckUpdatesAutomatically is { } check)
        {
            updates["checkAutomatically"] = check;
        }

        if (updates.Count > 0)
        {
            root["updates"] = updates;
        }

        if (config.ManualItems.Count > 0)
        {
            root["manualItems"] = new JsonArray(config.ManualItems.OrderBy(i => i.Id).Select(ManualItemNode).ToArray<JsonNode?>());
        }

        var overrides = WriteOverrides(config.Overrides, existing["overrides"] as JsonObject);
        if (overrides.Count > 0)
        {
            root["overrides"] = overrides;
        }

        if (config.IgnoredApps.Count > 0)
        {
            root["ignoredApps"] = new JsonArray(config.IgnoredApps.Select(r => r.ToString()).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        }

        foreach (var (key, value) in existing)
        {
            if (!root.ContainsKey(key) && !Managed.Contains(key))
            {
                root[key] = value?.DeepClone();
            }
        }

        return root.ToJsonString(Pretty) + "\n";
    }

    /// <summary>SHA-256 of the managed content, so two configs that mean the same compare equal whatever the file's formatting.</summary>
    public static string Hash(PortableConfig config) => HashText(Write(config));

    internal static string HashText(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static readonly HashSet<string> Managed = new(StringComparer.Ordinal)
    {
        "schema", "language", "ui", "quickSearch", "manualItems", "overrides", "ignoredApps", "updates",
    };

    private static readonly string[] UpdateChannels = ["stable", "beta"];

    private static string? ReadUpdateChannel(JsonObject root, List<string> problems)
    {
        if (root["updates"] is not JsonObject updates || updates["channel"] is null)
        {
            return null;
        }

        if (updates["channel"] is JsonValue v && v.TryGetValue<string>(out var channel) && UpdateChannels.Contains(channel))
        {
            return channel;
        }

        problems.Add("\"updates.channel\" must be \"stable\" or \"beta\"; ignored.");
        return null;
    }

    private static void SetOrKeep(JsonObject root, JsonObject existing, string key, JsonNode? value)
    {
        if (value is not null)
        {
            root[key] = value;
        }
        else if (existing[key] is { } old)
        {
            root[key] = old.DeepClone();
        }
    }

    private static JsonObject ManualItemNode(ManualItem item)
    {
        var node = new JsonObject
        {
            ["id"] = $"{ManualItem.Source}:{item.Id:D}",
            ["kind"] = item.Kind == ItemKind.Game ? "game" : "app",
            ["title"] = item.Title,
        };
        if (item.Url is not null)
        {
            node["url"] = item.Url;
        }
        else if (item.Path is not null)
        {
            node["path"] = item.Path;
        }

        if (!string.IsNullOrWhiteSpace(item.Arguments))
        {
            node["args"] = item.Arguments;
        }

        return node;
    }

    // Title and hidden are Lumen's; "args" and "artwork" belong to the file and stay as they were.
    private static JsonObject WriteOverrides(IReadOnlyDictionary<ItemRef, ItemOverride> overrides, JsonObject? existing)
    {
        var merged = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        if (existing is not null)
        {
            foreach (var (key, value) in existing)
            {
                if (value is JsonObject entry)
                {
                    var copy = (JsonObject)entry.DeepClone();
                    copy.Remove("title");
                    copy.Remove("hidden");
                    if (key.StartsWith("steam:", StringComparison.Ordinal))
                    {
                        copy.Remove("args"); // Lumen manages the arguments of Steam games; other entries' stay as they were
                    }

                    merged[key] = copy;
                }
            }
        }

        foreach (var (itemRef, item) in overrides)
        {
            if (item.Title is null && !item.Hidden && item.Args is null)
            {
                continue;
            }

            var entry = merged.GetValueOrDefault(itemRef.ToString()) ?? [];
            var ordered = new JsonObject();
            if (item.Title is not null)
            {
                ordered["title"] = item.Title;
            }

            if (item.Hidden)
            {
                ordered["hidden"] = true;
            }

            if (item.Args is not null)
            {
                ordered["args"] = item.Args;
            }

            foreach (var (key, value) in entry)
            {
                ordered[key] = value?.DeepClone();
            }

            merged[itemRef.ToString()] = ordered;
        }

        var result = new JsonObject();
        foreach (var (key, entry) in merged.Where(kv => kv.Value.Count > 0))
        {
            result[key] = entry;
        }

        return result;
    }

    private static string? ReadLanguage(JsonObject root, List<string> problems)
    {
        if (root["language"] is null)
        {
            return null;
        }

        if (root["language"] is JsonValue v && v.TryGetValue<string>(out var language) && Languages.Contains(language))
        {
            return language;
        }

        problems.Add("\"language\" must be \"system\", \"en\" or \"es\"; ignored.");
        return null;
    }

    private static bool? ReadBool(JsonNode? parent, string key, List<string> problems)
    {
        if (parent is not JsonObject obj || obj[key] is null)
        {
            return null;
        }

        if (obj[key] is JsonValue v && v.TryGetValue<bool>(out var value))
        {
            return value;
        }

        problems.Add($"\"{key}\" must be true or false; ignored.");
        return null;
    }

    private static string? ReadHotkey(JsonObject root, List<string> problems)
    {
        if (root["quickSearch"] is not JsonObject quick || quick["hotkey"] is null)
        {
            return null;
        }

        if (quick["hotkey"] is JsonValue v && v.TryGetValue<string>(out var text) && Hotkey.TryParse(text, out _))
        {
            return text.Trim();
        }

        problems.Add("\"quickSearch.hotkey\" is not a shortcut Lumen accepts; ignored.");
        return null;
    }

    private static List<ManualItem> ReadManualItems(JsonNode? node, List<string> problems)
    {
        var items = new List<ManualItem>();
        if (node is null)
        {
            return items;
        }

        if (node is not JsonArray array)
        {
            problems.Add("\"manualItems\" must be a list; ignored.");
            return items;
        }

        var seen = new HashSet<Guid>();
        for (var i = 0; i < array.Count; i++)
        {
            var item = ReadManualItem(array[i] as JsonObject, out var problem);
            if (item is null)
            {
                problems.Add($"manualItems[{i}]: {problem}; skipped.");
            }
            else if (seen.Add(item.Id))
            {
                items.Add(item);
            }
        }

        return items;
    }

    // Same rules as the dialog (ManualItemValidator): a synced file must not be a way around them.
    private static ManualItem? ReadManualItem(JsonObject? node, out string problem)
    {
        problem = "not an object";
        if (node is null)
        {
            return null;
        }

        var id = Text(node["id"]);
        if (id is null || !ItemRef.TryParse(id, out var itemRef) || itemRef.Source != ManualItem.Source || !Guid.TryParseExact(itemRef.ExternalId, "D", out var guid))
        {
            problem = "\"id\" must look like manual:<guid>";
            return null;
        }

        var kind = Text(node["kind"]) switch { "game" => ItemKind.Game, "app" => ItemKind.App, _ => (ItemKind?)null };
        var title = Text(node["title"]);
        var path = Text(node["path"]);
        var url = Text(node["url"]);
        var args = Text(node["args"]);
        if (kind is null || title is null || (path is null) == (url is null))
        {
            problem = "needs kind, title and exactly one of path or url";
            return null;
        }

        var error = ManualItemValidator.Validate(title, path ?? url, args);
        if (error != ManualItemError.None)
        {
            problem = $"refused ({error})";
            return null;
        }

        return ManualItemValidator.Create(guid, kind.Value, title, (path ?? url)!, args);
    }

    private static Dictionary<ItemRef, ItemOverride> ReadOverrides(JsonNode? node, List<string> problems)
    {
        var result = new Dictionary<ItemRef, ItemOverride>();
        if (node is null)
        {
            return result;
        }

        if (node is not JsonObject obj)
        {
            problems.Add("\"overrides\" must be an object; ignored.");
            return result;
        }

        foreach (var (key, value) in obj)
        {
            if (!ItemRef.TryParse(key, out var itemRef) || value is not JsonObject entry)
            {
                problems.Add($"overrides[\"{key}\"] is not a reference with an object; skipped.");
                continue;
            }

            var title = ItemOverride.CleanTitle(Text(entry["title"]));
            var hidden = entry["hidden"] is JsonValue h && h.TryGetValue<bool>(out var flag) && flag;
            // Arguments reach a game through Steam only; the file is untrusted, so they must pass the same check as typed ones.
            string? args = null;
            if (itemRef.Source == "steam" && Text(entry["args"]) is { } rawArgs)
            {
                args = LaunchOptions.Clean(rawArgs);
                if (args is null)
                {
                    problems.Add($"overrides[\"{key}\"].args is not valid launch arguments; ignored.");
                }
            }

            if (title is not null || hidden || args is not null)
            {
                result[itemRef] = new ItemOverride(title, hidden, args);
            }
        }

        return result;
    }

    private static List<ItemRef> ReadIgnoredApps(JsonNode? node, List<string> problems)
    {
        var result = new List<ItemRef>();
        if (node is null)
        {
            return result;
        }

        if (node is not JsonArray array)
        {
            problems.Add("\"ignoredApps\" must be a list; ignored.");
            return result;
        }

        foreach (var entry in array)
        {
            if (Text(entry) is { } text && ItemRef.TryParse(text, out var itemRef) && AppCuration.IsCandidateSource(itemRef.Source))
            {
                if (!result.Contains(itemRef))
                {
                    result.Add(itemRef);
                }
            }
            else
            {
                problems.Add("ignoredApps has an entry that is not a startmenu: or appsfolder: reference; skipped.");
            }
        }

        return result;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
