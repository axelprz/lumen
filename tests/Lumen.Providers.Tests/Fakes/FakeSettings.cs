// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Settings;

namespace Lumen.Providers.Tests.Fakes;

internal sealed class FakeSecretStore : ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> GetAsync(string name, CancellationToken ct) => Task.FromResult(Values.GetValueOrDefault(name));

    public Task SetAsync(string name, string value, CancellationToken ct)
    {
        Values[name] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string name, CancellationToken ct)
    {
        Values.Remove(name);
        return Task.CompletedTask;
    }
}

internal sealed class FakeLocalSettings : ILocalSettings
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> GetAsync(string key, CancellationToken ct) => Task.FromResult(Values.GetValueOrDefault(key));

    public Task SetAsync(string key, string value, CancellationToken ct)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        Values.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>Answers every request with a canned response and records what was asked. Never touches the network.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public StubHttpHandler RespondWith(HttpStatusCode status, string body = "")
    {
        Respond = _ => new HttpResponseMessage(status) { Content = new StringContent(body) };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(Respond(request));
    }
}
