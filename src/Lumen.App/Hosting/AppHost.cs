// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.App.Localization;
using Lumen.App.ViewModels;
using Lumen.App.Views;
using Lumen.Core;
using Lumen.Core.Localization;
using Lumen.Data;
using Lumen.Data.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Lumen.App.Hosting;

/// <summary>Composition root: every service, view model and view is registered here.</summary>
public static class AppHost
{
    public static IHost Build(LumenPaths paths, LocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(localizer);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Lumen",
            ContentRootPath = AppContext.BaseDirectory,
            DisableDefaults = true,
        });

        // Serilog's static logger is configured in App.OnStartup so startup failures are logged too.
        builder.Services.AddSerilog();

        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(localizer);
        builder.Services.AddSingleton<ILocalizer>(localizer);

        builder.Services.AddSingleton<ISqliteConnectionFactory>(new SqliteConnectionFactory(paths.DatabasePath));
        builder.Services.AddSingleton<IReadOnlyList<Migration>>(_ => MigrationLoader.LoadEmbedded());
        builder.Services.AddSingleton<MigrationRunner>();
        builder.Services.AddHostedService<DatabaseInitializer>();

        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
