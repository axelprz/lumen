// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Lumen.Core;
using Lumen.Core.Localization;
using Lumen.Core.Settings;
using Lumen.Providers.Steam;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Lumen.App.Services;

/// <summary>The account signed in on Steam's website, for Settings.</summary>
public sealed record SteamWebAccount(string SteamId, string Name, DateTimeOffset Since);

/// <summary>
/// Steam sign-in on Steam's own website in a built-in browser (WebView2), as Playnite does (owner decision,
/// 2026-10-29). Lumen is a real browser here, and never claims to be the Steam app or another device (CLAUDE.md
/// rule 8). Hardening:
/// <list type="bullet">
/// <item>The browser profile is Lumen's own, only for Steam (<c>%LOCALAPPDATA%\Lumen\webview\steam</c>). Its cookies
/// are protected by the browser with the Windows account's keys.</item>
/// <item>Top-level navigation is limited to Steam's https sites; pop-ups are blocked.</item>
/// <item>There are no dev tools, no password saving or autofill, no host objects and no web messages.</item>
/// <item>The only script Lumen runs reads two attributes of the store page (<see cref="SteamStorePage"/>), never on a
/// sign-in page. What the user types into Steam's page is never read.</item>
/// <item>The token is kept in memory only.</item>
/// <item>Disconnect signs out on Steam's page, then deletes the browser's cookies and data.</item>
/// </list>
/// </summary>
public sealed partial class SteamWebSession(LumenPaths paths, ILocalSettings settings, ILocalizer localizer, ILogger<SteamWebSession> logger) : ISteamWebSession
{
    private const string StoreHost = "store.steampowered.com";
    private const string StoreUrl = "https://store.steampowered.com/explore/";
    private const string SignInUrl = "https://store.steampowered.com/login/?redir=explore%2F";
    private const string PersonaScript = "(() => { const a = document.getElementById('account_pulldown'); return a ? a.textContent.trim() : null; })()";
    private const string SignOutScript = "(() => { if (typeof Logout === 'function') { Logout(); return true; } return false; })()";
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] AllowedHosts =
        ["store.steampowered.com", "login.steampowered.com", "help.steampowered.com", "steamcommunity.com", "checkout.steampowered.com"];

    private CoreWebView2Environment? _environment;

    private string UserDataFolder => Path.Combine(paths.LocalDataDirectory, "webview", "steam");

    public async Task<bool> IsSignedInAsync(CancellationToken ct) =>
        await settings.GetAsync(LocalSettingKeys.SteamWebAccountId, ct).ConfigureAwait(false) is not null;

    public async Task<SteamWebAccount?> GetAccountAsync(CancellationToken ct)
    {
        var id = await settings.GetAsync(LocalSettingKeys.SteamWebAccountId, ct);
        if (id is null)
        {
            return null;
        }

        var name = await settings.GetAsync(LocalSettingKeys.SteamWebAccountName, ct);
        var since = await settings.GetAsync(LocalSettingKeys.SteamWebAccountSince, ct);
        return new SteamWebAccount(
            id,
            name ?? id,
            DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : DateTimeOffset.UtcNow);
    }

    public Task<SteamWebToken?> GetTokenAsync(CancellationToken ct) =>
        Application.Current.Dispatcher.InvokeAsync(() => ReadTokenAsync(ct)).Task.Unwrap();

    /// <summary>Shows Steam's sign-in page; returns the account once the user is signed in, or null if they close it.</summary>
    public async Task<SteamWebAccount?> SignInAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var environment = await EnvironmentAsync();
        var view = new WebView2();
        var window = new Window
        {
            Owner = owner,
            Title = localizer["SteamWeb_WindowTitle"],
            Width = 540,
            Height = 780,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = view,
        };

        SteamWebAccount? account = null;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                Harden(core);
                core.NavigationCompleted += async (_, _) =>
                {
                    if (account is not null || !IsStorePage(core.Source))
                    {
                        return; // never run anything on the sign-in pages
                    }

                    var token = SteamStorePage.ParseConfig(await core.ExecuteScriptAsync(SteamStorePage.ReadConfigScript));
                    if (token is null)
                    {
                        return;
                    }

                    var name = ReadString(await core.ExecuteScriptAsync(PersonaScript));
                    account = new SteamWebAccount(token.SteamId, string.IsNullOrWhiteSpace(name) ? token.SteamId : name, DateTimeOffset.UtcNow);
                    window.Close();
                };
                core.Navigate(SignInUrl);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or WebView2RuntimeNotFoundException)
            {
                LogBrowserFailed(ex);
                window.Close();
            }
        };

        window.ShowDialog();
        if (account is null)
        {
            return null;
        }

        await settings.SetAsync(LocalSettingKeys.SteamWebAccountId, account.SteamId, CancellationToken.None);
        await settings.SetAsync(LocalSettingKeys.SteamWebAccountName, account.Name, CancellationToken.None);
        await settings.SetAsync(LocalSettingKeys.SteamWebAccountSince, account.Since.ToString("O", CultureInfo.InvariantCulture), CancellationToken.None);
        LogSignedIn();
        return account;
    }

    /// <summary>Signs out on Steam's page (ends the session at Steam), then deletes the browser's cookies and data.</summary>
    /// <returns>True when Steam's own sign-out ran.</returns>
    public async Task<bool> SignOutAsync()
    {
        var signedOut = false;
        try
        {
            signedOut = await WithBackgroundPageAsync(async core =>
            {
                var ran = await core.ExecuteScriptAsync(SignOutScript) == "true";
                if (ran)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3)); // the page posts the sign-out, then reloads
                }

                core.CookieManager.DeleteAllCookies();
                await core.Profile.ClearBrowsingDataAsync();
                return ran;
            });
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or WebView2RuntimeNotFoundException or TimeoutException)
        {
            LogBrowserFailed(ex);
        }

        foreach (var key in new[] { LocalSettingKeys.SteamWebAccountId, LocalSettingKeys.SteamWebAccountName, LocalSettingKeys.SteamWebAccountSince })
        {
            await settings.RemoveAsync(key, CancellationToken.None);
        }

        LogSignedOut(signedOut);
        return signedOut;
    }

    private async Task<SteamWebToken?> ReadTokenAsync(CancellationToken ct)
    {
        try
        {
            return await WithBackgroundPageAsync(async core =>
                IsStorePage(core.Source) ? SteamStorePage.ParseConfig(await core.ExecuteScriptAsync(SteamStorePage.ReadConfigScript)) : null, ct);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or WebView2RuntimeNotFoundException or TimeoutException)
        {
            LogBrowserFailed(ex);
            return null;
        }
    }

    // The store page in an invisible browser (a hidden window that is never shown), loaded once and closed after use.
    private async Task<T> WithBackgroundPageAsync<T>(Func<CoreWebView2, Task<T>> use, CancellationToken ct = default)
    {
        var environment = await EnvironmentAsync();
        using var host = new HwndSource(new HwndSourceParameters("Lumen Steam") { Width = 1024, Height = 768, WindowStyle = unchecked((int)0x80000000) }); // WS_POPUP, not visible
        var controller = await environment.CreateCoreWebView2ControllerAsync(host.Handle);
        try
        {
            controller.IsVisible = false;
            var core = controller.CoreWebView2;
            Harden(core);
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            core.Navigate(StoreUrl);
            await loaded.Task.WaitAsync(PageTimeout, ct);
            return await use(core);
        }
        finally
        {
            controller.Close();
        }
    }

    private async Task<CoreWebView2Environment> EnvironmentAsync()
    {
        if (_environment is not null)
        {
            return _environment;
        }

        Directory.CreateDirectory(UserDataFolder);
        var options = new CoreWebView2EnvironmentOptions { Language = CultureInfo.CurrentUICulture.Name };
        _environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, UserDataFolder, options);
        return _environment;
    }

    private static void Harden(CoreWebView2 core)
    {
        var s = core.Settings;
        s.AreDevToolsEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsStatusBarEnabled = false;
        s.AreDefaultContextMenusEnabled = false;

        core.NavigationStarting += (_, e) =>
        {
            if (!IsAllowed(e.Uri))
            {
                e.Cancel = true; // only Steam's own sites, over https
            }
        };
        core.NewWindowRequested += (_, e) => e.Handled = true; // no pop-ups
    }

    private static bool IsAllowed(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && AllowedHosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase);

    // A store page that isn't a sign-in page: the only place Lumen runs its read-only script.
    private static bool IsStorePage(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && string.Equals(u.Host, StoreHost, StringComparison.OrdinalIgnoreCase)
        && !u.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(string scriptResult)
    {
        try
        {
            return JsonSerializer.Deserialize<string?>(scriptResult);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Steam: signed in on Steam's website")]
    private partial void LogSignedIn();

    [LoggerMessage(Level = LogLevel.Information, Message = "Steam: signed out (Steam's own sign-out ran: {Ran}); browser data deleted")]
    private partial void LogSignedOut(bool ran);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam: the built-in browser failed")]
    private partial void LogBrowserFailed(Exception exception);
}
