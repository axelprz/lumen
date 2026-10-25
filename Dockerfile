# syntax=docker/dockerfile:1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
#
# Reproducible build + test environment (Linux container, .NET 10 SDK).
# The whole solution compiles here thanks to EnableWindowsTargeting; the WPF app itself only RUNS on Windows.
#
#   docker build --target test .                        # restore, build and run every test
#   docker build --target test-results --output TestResults .   # same, and copy the .trx files out
#   docker compose run --rm dev                          # interactive shell with the repo mounted

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS base
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip
WORKDIR /src

FROM base AS build
COPY . .
RUN --mount=type=cache,id=lumen-nuget,target=/root/.nuget/packages \
    dotnet restore \
 && dotnet build --no-restore -c Release

FROM build AS test
RUN --mount=type=cache,id=lumen-nuget,target=/root/.nuget/packages \
    dotnet test --no-build -c Release \
      --logger "console;verbosity=normal" \
      --logger trx \
      --results-directory /testresults

FROM scratch AS test-results
COPY --from=test /testresults /
