// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Data.Migrations;
using Microsoft.Extensions.Hosting;

namespace Lumen.App.Hosting;

/// <summary>Runs database migrations when the host starts, before the main window is shown.</summary>
public sealed class DatabaseInitializer(MigrationRunner runner) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => runner.MigrateAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
