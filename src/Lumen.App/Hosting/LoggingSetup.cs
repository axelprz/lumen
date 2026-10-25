// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.IO;
using Lumen.Core;
using Serilog;
using Serilog.Events;

namespace Lumen.App.Hosting;

public static class LoggingSetup
{
    /// <summary>
    /// Rolling file logs in <c>%LOCALAPPDATA%\Lumen\logs</c>.
    /// Never log secrets, tokens or full request URLs that may carry keys.
    /// </summary>
    public static Serilog.ILogger CreateLogger(LumenPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "lumen-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
