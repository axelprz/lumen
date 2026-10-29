// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;
using System.Text.RegularExpressions;
using Lumen.Core.Library;

namespace Lumen.Core.Routines;

/// <summary>The result of reading a routine file: the routine, or why it can't be used.</summary>
public sealed record RoutineParseResult(Routine? Routine, IReadOnlyList<string> Errors, string? Hash)
{
    public bool IsValid => Routine is not null;
}

/// <summary>
/// Reads and validates routine JSON against <c>docs/schemas/routine.schema.json</c>, by hand (no schema library):
/// unknown properties, duplicate keys, wrong types and out-of-range values are errors, so what the trust preview
/// shows is exactly what would run. A routine with any error is never runnable. Errors are technical English (for
/// logs and the details box); the UI shows its own translated summary.
/// </summary>
public static partial class RoutineParser
{
    /// <summary>Larger files are refused before parsing.</summary>
    public const int MaxBytes = 256 * 1024;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    public static RoutineParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaxBytes)
        {
            return Fail("The file is larger than 256 KB.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            return Fail($"Not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var errors = new List<string>();
            var routine = ReadRoutine(document.RootElement, errors);
            return errors.Count > 0 || routine is null
                ? new RoutineParseResult(null, errors, null)
                : new RoutineParseResult(routine, [], RoutineJson.Hash(document.RootElement));
        }
    }

    private static RoutineParseResult Fail(string error) => new(null, [error], null);

    private static Routine? ReadRoutine(JsonElement root, List<string> errors)
    {
        // Unknown or repeated top-level keys are errors, but keep checking so every problem is listed at once.
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("$: must be an object.");
            return null;
        }

        ObjectWith(root, "$", ["schema", "id", "name", "description", "icon", "pinned", "stopOnError", "variables", "actions"], errors);

        if (!Has(root, "schema", "$", errors) | !Has(root, "id", "$", errors) | !Has(root, "name", "$", errors) | !Has(root, "actions", "$", errors))
        {
            return null;
        }

        if (root.GetProperty("schema") is not { ValueKind: JsonValueKind.Number } schema || !schema.TryGetInt32(out var version) || version != Routine.SchemaVersion)
        {
            errors.Add("$.schema: must be 1.");
        }

        var id = String(root, "id", "$", errors);
        if (id is not null && !Routine.IsValidId(id))
        {
            errors.Add("$.id: use 1–64 lowercase letters, digits or '-', starting with a letter or digit.");
        }

        var name = ReadName(root.GetProperty("name"), errors);
        var description = OptionalString(root, "description", "$", 500, errors);
        var icon = OptionalString(root, "icon", "$", 40, errors);
        var pinned = OptionalBool(root, "pinned", "$", false, errors);
        var stopOnError = OptionalBool(root, "stopOnError", "$", false, errors);
        var variables = ReadVariables(root, errors);

        var actions = new List<RoutineAction>();
        var list = root.GetProperty("actions");
        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add("$.actions: must be a list.");
        }
        else if (list.GetArrayLength() is < 1 or > Routine.MaxActions)
        {
            errors.Add($"$.actions: must have 1–{Routine.MaxActions} steps.");
        }
        else
        {
            var index = 0;
            foreach (var element in list.EnumerateArray())
            {
                if (ReadAction(element, $"$.actions[{index++}]", errors) is { } action)
                {
                    actions.Add(action);
                }
            }
        }

        if (errors.Count > 0 || id is null || name is null)
        {
            return null;
        }

        return new Routine(id, name, actions)
        {
            Description = description,
            Icon = icon,
            Pinned = pinned,
            StopOnError = stopOnError,
            Variables = variables,
        };
    }

    private static RoutineName? ReadName(JsonElement value, List<string> errors)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (text.Length is < 1 or > RoutineName.MaxLength)
            {
                errors.Add($"$.name: 1–{RoutineName.MaxLength} characters.");
                return null;
            }

            return new RoutineName(text);
        }

        if (value.ValueKind != JsonValueKind.Object || !Unique(value, "$.name", errors))
        {
            errors.Add("$.name: a text, or an object of language → text.");
            return null;
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!CulturePattern().IsMatch(property.Name))
            {
                errors.Add($"$.name: '{property.Name}' is not a language code like 'es' or 'en-US'.");
            }
            else if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString()!.Length is < 1 or > RoutineName.MaxLength)
            {
                errors.Add($"$.name.{property.Name}: 1–{RoutineName.MaxLength} characters.");
            }
            else
            {
                names[property.Name] = property.Value.GetString()!;
            }
        }

        if (names.Count == 0)
        {
            errors.Add("$.name: at least one language.");
            return null;
        }

        return new RoutineName(null, names);
    }

    private static Dictionary<string, string> ReadVariables(JsonElement root, List<string> errors)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("variables", out var value))
        {
            return result;
        }

        if (value.ValueKind != JsonValueKind.Object || !Unique(value, "$.variables", errors))
        {
            errors.Add("$.variables: must be an object of name → text.");
            return result;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (!VariablePattern().IsMatch(property.Name))
            {
                errors.Add($"$.variables: '{property.Name}' is not a valid name (a letter, then letters, digits or '_', up to 32).");
            }
            else if (property.Value.ValueKind != JsonValueKind.String)
            {
                errors.Add($"$.variables.{property.Name}: must be text.");
            }
            else
            {
                result[property.Name] = property.Value.GetString()!;
            }
        }

        return result;
    }

    private static RoutineAction? ReadAction(JsonElement element, string at, List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{at}: must be an object with a \"type\".");
            return null;
        }

        var before = errors.Count;
        RoutineAction? action = typeValue.GetString() switch
        {
            "launch" => ReadLaunch(element, at, errors),
            "url" => ReadUrl(element, at, errors),
            "folder" => ReadFolder(element, at, errors),
            "wait" => ReadWait(element, at, errors),
            "close" => ReadClose(element, at, errors),
            var other => Error<RoutineAction>(errors, $"{at}.type: unknown action '{other}' (launch, url, folder, wait, close)."),
        };

        if (action is null || errors.Count > before)
        {
            return null;
        }

        var label = OptionalString(element, "label", at, 80, errors);
        int? timeout = element.TryGetProperty("timeoutMs", out _) ? OptionalInt(element, "timeoutMs", at, 100, 600_000, 0, errors) : null;
        return action with { Label = label, TimeoutMs = timeout };
    }

    private static readonly string[] Common = ["type", "label", "timeoutMs"];

    private static LaunchAction? ReadLaunch(JsonElement e, string at, List<string> errors)
    {
        if (!ObjectWith(e, at, [.. Common, "ref", "path", "args", "workingDirectory", "skipIfRunning"], errors))
        {
            return null;
        }

        var hasRef = e.TryGetProperty("ref", out _);
        var hasPath = e.TryGetProperty("path", out _);
        if (hasRef == hasPath)
        {
            return Error<LaunchAction>(errors, $"{at}: needs exactly one of \"ref\" (a library item) or \"path\" (a program).");
        }

        // A library item opens the way its store does; extra arguments only make sense for a program path.
        if (hasRef && (e.TryGetProperty("args", out _) || e.TryGetProperty("workingDirectory", out _)))
        {
            return Error<LaunchAction>(errors, $"{at}: \"args\" and \"workingDirectory\" only go with \"path\".");
        }

        ItemRef? itemRef = hasRef ? Ref(e, "ref", at, errors) : null;
        var path = hasPath ? PathValue(e, "path", at, errors) : null;
        return new LaunchAction(itemRef, path)
        {
            Arguments = OptionalString(e, "args", at, 2000, errors),
            WorkingDirectory = e.TryGetProperty("workingDirectory", out _) ? PathValue(e, "workingDirectory", at, errors) : null,
            SkipIfRunning = OptionalBool(e, "skipIfRunning", at, true, errors),
        };
    }

    private static UrlAction? ReadUrl(JsonElement e, string at, List<string> errors)
    {
        if (!ObjectWith(e, at, [.. Common, "url"], errors) || !Has(e, "url", at, errors))
        {
            return null;
        }

        var url = String(e, "url", at, errors);
        if (url is null)
        {
            return null;
        }

        if (url.Length > 2000 || !IsWebUrl(url))
        {
            return Error<UrlAction>(errors, $"{at}.url: must be an http:// or https:// address (up to 2000 characters).");
        }

        return new UrlAction(url);
    }

    private static FolderAction? ReadFolder(JsonElement e, string at, List<string> errors) =>
        ObjectWith(e, at, [.. Common, "path"], errors) && Has(e, "path", at, errors) && PathValue(e, "path", at, errors) is { } path
            ? new FolderAction(path)
            : null;

    private static WaitAction? ReadWait(JsonElement e, string at, List<string> errors) =>
        ObjectWith(e, at, [.. Common, "ms"], errors) && Has(e, "ms", at, errors)
            ? new WaitAction(OptionalInt(e, "ms", at, 0, 300_000, 0, errors))
            : null;

    private static CloseAction? ReadClose(JsonElement e, string at, List<string> errors)
    {
        if (!ObjectWith(e, at, [.. Common, "refs", "processes", "graceMs", "force"], errors))
        {
            return null;
        }

        var refs = new List<ItemRef>();
        if (e.TryGetProperty("refs", out var refList))
        {
            if (refList.ValueKind != JsonValueKind.Array || refList.GetArrayLength() == 0)
            {
                errors.Add($"{at}.refs: a non-empty list of library items.");
            }
            else
            {
                foreach (var value in refList.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String && RefPattern().IsMatch(value.GetString()!) && ItemRef.TryParse(value.GetString(), out var r))
                    {
                        refs.Add(r);
                    }
                    else
                    {
                        errors.Add($"{at}.refs: '{value}' is not a library item reference like 'steam:570'.");
                    }
                }
            }
        }

        var processes = new List<string>();
        if (e.TryGetProperty("processes", out var names))
        {
            if (names.ValueKind != JsonValueKind.Array || names.GetArrayLength() == 0)
            {
                errors.Add($"{at}.processes: a non-empty list of program names like 'discord.exe'.");
            }
            else
            {
                foreach (var value in names.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String && ProcessNamePattern().IsMatch(value.GetString()!))
                    {
                        processes.Add(value.GetString()!);
                    }
                    else
                    {
                        errors.Add($"{at}.processes: '{value}' is not a program name like 'discord.exe'.");
                    }
                }
            }
        }

        if (!e.TryGetProperty("refs", out _) && !e.TryGetProperty("processes", out _))
        {
            errors.Add($"{at}: needs \"refs\" and/or \"processes\".");
        }

        return new CloseAction(refs, processes)
        {
            GraceMs = e.TryGetProperty("graceMs", out _) ? OptionalInt(e, "graceMs", at, 0, 60_000, CloseAction.DefaultGraceMs, errors) : CloseAction.DefaultGraceMs,
            Force = OptionalBool(e, "force", at, false, errors),
        };
    }

    internal static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && url.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(uri.Host);

    // --- helpers -------------------------------------------------------------------------------------------------

    private static T? Error<T>(List<string> errors, string message)
        where T : class
    {
        errors.Add(message);
        return null;
    }

    // An object with only known, non-repeated properties.
    private static bool ObjectWith(JsonElement element, string at, string[] allowed, List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{at}: must be an object.");
            return false;
        }

        var ok = Unique(element, at, errors);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                errors.Add($"{at}: unknown property '{property.Name}'.");
                ok = false;
            }
        }

        return ok;
    }

    // JSON allows a key twice; the preview and the runner could then disagree, so it's refused.
    private static bool Unique(JsonElement element, string at, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                errors.Add($"{at}: '{property.Name}' appears twice.");
                return false;
            }
        }

        return true;
    }

    private static bool Has(JsonElement element, string name, string at, List<string> errors)
    {
        if (element.TryGetProperty(name, out _))
        {
            return true;
        }

        errors.Add($"{at}: \"{name}\" is required.");
        return false;
    }

    private static string? String(JsonElement element, string name, string at, List<string> errors)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        errors.Add($"{at}.{name}: must be text.");
        return null;
    }

    private static string? OptionalString(JsonElement element, string name, string at, int maxLength, List<string> errors)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > maxLength)
        {
            errors.Add($"{at}.{name}: text up to {maxLength} characters.");
            return null;
        }

        return value.GetString();
    }

    private static string? PathValue(JsonElement element, string name, string at, List<string> errors)
    {
        var value = String(element, name, at, errors);
        if (value is { Length: 0 } || value?.Length > 1000 || value?.Contains('\0', StringComparison.Ordinal) == true)
        {
            errors.Add($"{at}.{name}: a path of 1–1000 characters.");
            return null;
        }

        return value;
    }

    private static bool OptionalBool(JsonElement element, string name, string at, bool fallback, List<string> errors)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        errors.Add($"{at}.{name}: must be true or false.");
        return fallback;
    }

    private static int OptionalInt(JsonElement element, string name, string at, int min, int max, int fallback, List<string> errors)
    {
        if (element.GetProperty(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) && number >= min && number <= max)
        {
            return number;
        }

        errors.Add($"{at}.{name}: a whole number from {min} to {max}.");
        return fallback;
    }

    private static ItemRef? Ref(JsonElement element, string name, string at, List<string> errors)
    {
        var text = String(element, name, at, errors);
        if (text is not null && RefPattern().IsMatch(text) && ItemRef.TryParse(text, out var itemRef))
        {
            return itemRef;
        }

        errors.Add($"{at}.{name}: not a library item reference like 'steam:570'.");
        return null;
    }

    [GeneratedRegex("^[a-z]{2}(-[A-Z]{2})?$")]
    private static partial Regex CulturePattern();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_]{0,31}$")]
    private static partial Regex VariablePattern();

    [GeneratedRegex("^[a-z]+:.+$")]
    private static partial Regex RefPattern();

    [GeneratedRegex("^[^\\\\/:*?\"<>|]+\\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex ProcessNamePattern();
}
