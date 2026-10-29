// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.RegularExpressions;

namespace Lumen.Core.Routines;

/// <summary>Why a step can never run.</summary>
public enum BlockedReason
{
    None,

    /// <summary>The path isn't a full path like <c>C:\Games\game.exe</c>.</summary>
    NotAbsolute,

    /// <summary>Only <c>.exe</c> programs can be opened (no .bat, .ps1, .lnk, .vbs, …).</summary>
    NotAProgram,

    /// <summary>A command line, script host or system tool (CLAUDE.md rule 5: routines never run scripts).</summary>
    ScriptHost,

    /// <summary>Short 8.3 names (<c>POWERS~1.EXE</c>) could hide a blocked program, so they're refused.</summary>
    ShortName,

    /// <summary>A Windows process, or Lumen itself, that a routine must never close.</summary>
    ProtectedProcess,

    /// <summary>Not an http/https address.</summary>
    NotAWebPage,
}

/// <summary>
/// The owner-approved limits for routines (2026-10-28):
/// <list type="bullet">
/// <item>They may open library items or any <c>.exe</c>, except command lines, script hosts and system tools that
/// run other code.</item>
/// <item>They may close any program by name, except Windows' own processes and Lumen.</item>
/// </list>
/// Checked when reviewing (the step is shown as blocked and the routine can't be approved) and again right before
/// running.
/// </summary>
public static partial class RoutineSafety
{
    /// <summary>Programs a routine can never open: shells, script engines and tools that run other code or change the system.</summary>
    public static readonly IReadOnlySet<string> BlockedPrograms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Command lines and terminals
        "cmd.exe", "powershell.exe", "powershell_ise.exe", "pwsh.exe", "wt.exe", "conhost.exe", "bash.exe", "wsl.exe", "wslhost.exe", "sh.exe",
        // Script hosts and interpreters
        "wscript.exe", "cscript.exe", "mshta.exe", "python.exe", "pythonw.exe", "py.exe", "pyw.exe", "node.exe", "perl.exe", "ruby.exe",
        "php.exe", "java.exe", "javaw.exe", "deno.exe", "bun.exe", "lua.exe", "autohotkey.exe", "autohotkey64.exe", "autoit3.exe",
        // Windows tools that run other code, install things, elevate or change the system
        "rundll32.exe", "regsvr32.exe", "msiexec.exe", "runas.exe", "explorer.exe", "control.exe", "mmc.exe", "msdt.exe", "hh.exe",
        "schtasks.exe", "at.exe", "reg.exe", "regedit.exe", "regini.exe", "sc.exe", "net.exe", "net1.exe", "netsh.exe", "wmic.exe",
        "bitsadmin.exe", "certutil.exe", "forfiles.exe", "pcalua.exe", "installutil.exe", "msbuild.exe", "csc.exe", "vbc.exe",
        "cmstp.exe", "odbcconf.exe", "regasm.exe", "regsvcs.exe", "msxsl.exe", "presentationhost.exe", "dotnet.exe", "winget.exe",
        "shutdown.exe", "taskkill.exe", "takeown.exe", "icacls.exe", "cipher.exe", "format.com", "diskpart.exe", "bcdedit.exe",
        "vssadmin.exe", "wevtutil.exe", "sdclt.exe", "fodhelper.exe", "computerdefaults.exe", "eventvwr.exe", "ftp.exe", "curl.exe",
    };

    /// <summary>Processes a routine can never close: Windows' own and Lumen.</summary>
    public static readonly IReadOnlySet<string> ProtectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "lumen.exe", "explorer.exe", "dwm.exe", "winlogon.exe", "csrss.exe", "lsass.exe", "services.exe", "smss.exe", "svchost.exe",
        "wininit.exe", "sihost.exe", "fontdrvhost.exe", "ctfmon.exe", "taskhostw.exe", "startmenuexperiencehost.exe", "searchhost.exe",
        "searchapp.exe", "shellexperiencehost.exe", "runtimebroker.exe", "textinputhost.exe", "applicationframehost.exe", "audiodg.exe",
        "spoolsv.exe", "lsaiso.exe", "msmpeng.exe", "securityhealthservice.exe", "securityhealthsystray.exe", "smartscreen.exe",
        "system", "registry", "memory compression", "conhost.exe", "dllhost.exe", "wudfhost.exe", "taskmgr.exe", "logonui.exe",
        "lockapp.exe", "userinit.exe", "secure system",
    };

    /// <summary>Replaces <c>{name}</c> with the routine's variables, then <c>%ENV%</c> variables.</summary>
    public static string Expand(string text, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(variables);
        var withVariables = VariableReference().Replace(text, m => variables.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
        return Environment.ExpandEnvironmentVariables(withVariables);
    }

    /// <summary>Whether an (expanded) program path may be opened by a routine.</summary>
    public static BlockedReason CheckProgram(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return BlockedReason.NotAbsolute;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path); // resolves "..\" so it can't hide the real file name
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return BlockedReason.NotAbsolute;
        }

        if (ShortNameSegment().IsMatch(full))
        {
            return BlockedReason.ShortName;
        }

        var name = Path.GetFileName(full);
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.Contains(':', StringComparison.Ordinal))
        {
            return BlockedReason.NotAProgram;
        }

        return IsBlockedProgramName(name) ? BlockedReason.ScriptHost : BlockedReason.None;
    }

    /// <summary>A program name on the block list ("cmd.exe"), also matching an exe's original name from its version info.</summary>
    public static bool IsBlockedProgramName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && BlockedPrograms.Contains(Normalize(fileName));

    public static bool IsProtectedProcess(string name) =>
        !string.IsNullOrWhiteSpace(name) && ProtectedProcesses.Contains(name.Trim());

    // Version info sometimes says "Cmd.Exe.MUI" or "PowerShell.EXE ".
    private static string Normalize(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        return name.EndsWith(".mui", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    [GeneratedRegex(@"\{([a-zA-Z][a-zA-Z0-9_]{0,31})\}")]
    private static partial Regex VariableReference();

    // "PROGRA~1", "POWERS~1.EXE": a tilde followed by digits inside a path segment.
    [GeneratedRegex(@"~\d+(\.|\\|$)")]
    private static partial Regex ShortNameSegment();
}
