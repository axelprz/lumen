// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lumen.Core.Routines;

/// <summary>
/// Writes routines as JSON (the editor, export) and hashes them for the trust check (ARCHITECTURE §7): the JSON is
/// re-written canonically (keys sorted, no whitespace) and hashed with SHA-256, so re-formatting a file or
/// reordering its keys doesn't make it untrusted, but any change of meaning does.
/// </summary>
public static class RoutineJson
{
    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    /// <summary>SHA-256 (lowercase hex) of the canonical form.</summary>
    public static string Hash(JsonElement root)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(writer, root);
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>The routine as JSON in the schema's shape (defaults left out).</summary>
    public static string Serialize(Routine routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, Indented))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", Routine.SchemaVersion);
            w.WriteString("id", routine.Id);
            if (routine.Name.Text is { } text)
            {
                w.WriteString("name", text);
            }
            else
            {
                w.WriteStartObject("name");
                foreach (var (culture, name) in routine.Name.ByCulture!.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    w.WriteString(culture, name);
                }

                w.WriteEndObject();
            }

            WriteIf(w, "description", routine.Description);
            WriteIf(w, "icon", routine.Icon);
            if (routine.Pinned)
            {
                w.WriteBoolean("pinned", true);
            }

            if (routine.StopOnError)
            {
                w.WriteBoolean("stopOnError", true);
            }

            if (routine.Variables.Count > 0)
            {
                w.WriteStartObject("variables");
                foreach (var (name, value) in routine.Variables.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    w.WriteString(name, value);
                }

                w.WriteEndObject();
            }

            w.WriteStartArray("actions");
            foreach (var action in routine.Actions)
            {
                WriteAction(w, action);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteAction(Utf8JsonWriter w, RoutineAction action)
    {
        w.WriteStartObject();
        switch (action)
        {
            case LaunchAction launch:
                w.WriteString("type", "launch");
                WriteIf(w, "ref", launch.Ref?.ToString());
                WriteIf(w, "path", launch.Path);
                WriteIf(w, "args", launch.Arguments);
                WriteIf(w, "workingDirectory", launch.WorkingDirectory);
                if (!launch.SkipIfRunning)
                {
                    w.WriteBoolean("skipIfRunning", false);
                }

                break;
            case UrlAction url:
                w.WriteString("type", "url");
                w.WriteString("url", url.Url);
                break;
            case FolderAction folder:
                w.WriteString("type", "folder");
                w.WriteString("path", folder.Path);
                break;
            case WaitAction wait:
                w.WriteString("type", "wait");
                w.WriteNumber("ms", wait.Milliseconds);
                break;
            case CloseAction close:
                w.WriteString("type", "close");
                if (close.Refs.Count > 0)
                {
                    w.WriteStartArray("refs");
                    foreach (var r in close.Refs)
                    {
                        w.WriteStringValue(r.ToString());
                    }

                    w.WriteEndArray();
                }

                if (close.Processes.Count > 0)
                {
                    w.WriteStartArray("processes");
                    foreach (var p in close.Processes)
                    {
                        w.WriteStringValue(p);
                    }

                    w.WriteEndArray();
                }

                if (close.GraceMs != CloseAction.DefaultGraceMs)
                {
                    w.WriteNumber("graceMs", close.GraceMs);
                }

                if (close.Force)
                {
                    w.WriteBoolean("force", true);
                }

                break;
            default:
                throw new ArgumentException($"Unknown action {action.GetType().Name}.", nameof(action));
        }

        WriteIf(w, "label", action.Label);
        if (action.TimeoutMs is { } timeout)
        {
            w.WriteNumber("timeoutMs", timeout);
        }

        w.WriteEndObject();
    }

    private static void WriteIf(Utf8JsonWriter w, string name, string? value)
    {
        if (value is not null)
        {
            w.WriteString(name, value);
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer); // strings, numbers (as written), true/false/null
                break;
        }
    }
}
