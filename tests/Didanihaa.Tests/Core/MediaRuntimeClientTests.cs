using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Didanihaa.Core;
using Didanihaa.Infrastructure;
using Didanihaa.Tests.Fixtures;
using Xunit;

namespace Didanihaa.Tests;

public sealed class MediaRuntimeClientTests
{
    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
    }

    private static HttpResponseMessage Json(RuntimeIdentity identity, bool ready = true) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new RuntimeStatus(identity, ready)) };

    [Fact]
    public async Task AReadyPeerMustMatchTheLocalDescriptorAndInstallation()
    {
        using var workspace = new TestWorkspace();
        var layout = new RuntimeLayout(workspace.Root, new() { HostExecutable = "apps/host.exe" });
        using var instance = RuntimeInstance.Acquire(layout, RuntimeRole.Media);
        // Model a host's image path while retaining this process's actual PID and
        // start time. The HTTP handler and descriptor are both fixture-owned.
        var identity = instance.Identity with { ExecutablePath = layout.HostExecutable };
        File.WriteAllText(RuntimeIdentity.DescriptorPath(workspace.Root, RuntimeRole.Media),
            JsonSerializer.Serialize(identity, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var handler = new ReplyHandler(request =>
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            Assert.Equal("/_internal/runtime", request.RequestUri.AbsolutePath);
            return Json(identity);
        });
        using var client = new HttpClient(handler);
        Assert.Equal(identity, (await new MediaRuntimeClient(layout, client).ReadReadyAsync(TestContext.Current.CancellationToken))!.Identity);
        var otherBinary = new RuntimeLayout(workspace.Root, layout.Configuration with { HostExecutable = "apps/other.exe" });
        Assert.Null(await new MediaRuntimeClient(otherBinary, client).ReadReadyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AForeignRootReplyIsRejectedEvenWhenItsProcessIsAlive()
    {
        using var first = new TestWorkspace(); using var second = new TestWorkspace();
        using var peer = RuntimeInstance.Acquire(new(first.Root, new()), RuntimeRole.Media);
        using var handler = new ReplyHandler(_ => Json(peer.Identity));
        using var client = new HttpClient(handler);
        Assert.Null(await new MediaRuntimeClient(new(second.Root, new()), client).ReadReadyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnreadyMalformedAndUnrelatedHttpRepliesDoNotBecomeOnline()
    {
        using var workspace = new TestWorkspace();
        var layout = new RuntimeLayout(workspace.Root, new());
        using var instance = RuntimeInstance.Acquire(layout, RuntimeRole.Media);
        using var handler = new ReplyHandler(_ => Json(instance.Identity, false));
        using var client = new HttpClient(handler);
        Assert.Null(await new MediaRuntimeClient(layout, client).ReadReadyAsync(TestContext.Current.CancellationToken));
        using var malformedHandler = new ReplyHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("not JSON") });
        using var malformedClient = new HttpClient(malformedHandler);
        Assert.Null(await new MediaRuntimeClient(layout, malformedClient).ReadReadyAsync(TestContext.Current.CancellationToken));
        using var missingHandler = new ReplyHandler(_ => new(HttpStatusCode.NotFound));
        using var missingClient = new HttpClient(missingHandler);
        Assert.Null(await new MediaRuntimeClient(layout, missingClient).ReadReadyAsync(TestContext.Current.CancellationToken));
    }
}
