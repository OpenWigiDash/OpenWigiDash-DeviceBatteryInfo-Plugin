using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.GHub;

namespace DeviceBatteryInfo.Tests;

/// <summary>O cliente contra um G HUB de mentira (websocket local que responde com as mensagens capturadas).</summary>
public class GHubClientTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Connects_Subscribes_ListsAndReadsBattery()
    {
        await using var server = FakeGHub.Start();
        var client = new GHubClient(TimeProvider.System, server.Endpoint, [TimeSpan.FromMilliseconds(100)]);
        client.Acquire();
        try
        {
            await Until(() => client.Devices.Any(d => d.Key == Fixtures.KeyboardKey && d.Percentage == 55));
            Assert.Equal(SourceState.Connected, client.State);
            Assert.Contains(server.Received, m => m.Contains("\"SUBSCRIBE\"") && m.Contains("/battery/state/changed"));
            Assert.Contains(server.Received, m => m.Contains("\"SUBSCRIBE\"") && m.Contains("/devices/state/changed"));
            Assert.Contains(server.Received, m => m.Contains("/battery/dev00000007/state"));
            Assert.DoesNotContain(server.Received, m => m.Contains("/battery/dev00000008/state")); // webcam

            // Evento de bateria.
            await server.SendAsync(Fixtures.Read("battery-keyboard.json")
                .Replace("\"percentage\": 55", "\"percentage\": 40")
                .Replace("/battery/dev00000006/state", "/battery/state/changed"));
            await Until(() => client.Devices.Single(d => d.Key == Fixtures.KeyboardKey).Percentage == 40);
        }
        finally
        {
            await client.ReleaseAsync();
        }
    }

    [Fact]
    public async Task GHubClosing_MarksOffline_AndReconnectsAlone()
    {
        await using var server = FakeGHub.Start();
        var client = new GHubClient(TimeProvider.System, server.Endpoint, [TimeSpan.FromMilliseconds(100)]);
        client.Acquire();
        try
        {
            await Until(() => client.Devices.Any(d => d.Percentage == 55));
            var connections = server.Connections;

            await server.DropAsync();
            await Until(() => client.Devices.All(d => !d.Connected));

            // O laço tenta de novo sozinho e volta a ler.
            await Until(() => server.Connections > connections && client.Devices.Any(d => d.Connected && d.Percentage == 55));
            Assert.Equal(SourceState.Connected, client.State);
        }
        finally
        {
            await client.ReleaseAsync();
        }
    }

    [Fact]
    public void Shared_UsesTheRealEndpoint() =>
        // O Shared era criado antes dos padrões estáticos e ficava com endereço nulo.
        Assert.Equal(new Uri("ws://127.0.0.1:9010"), GHubClient.Shared.Endpoint);

    [Fact]
    public async Task NoGHub_IsUnavailable()
    {
        var port = FakeGHub.FreePort();
        var client = new GHubClient(TimeProvider.System, new Uri($"ws://127.0.0.1:{port}/"), [TimeSpan.FromMilliseconds(100)]);
        client.Acquire();
        try
        {
            await Until(() => client.State == SourceState.Unavailable);
            Assert.Empty(client.Devices);
        }
        finally
        {
            await client.ReleaseAsync();
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Tempo esgotado esperando a condição.");
            await Task.Delay(20);
        }
    }

    /// <summary>Servidor websocket que responde lista e bateria como o lghub_agent.</summary>
    private sealed class FakeGHub : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private WebSocket? _socket;
        private int _connections;

        public Uri Endpoint { get; private init; } = null!;
        public ConcurrentQueue<string> Received { get; } = new();
        public int Connections => Volatile.Read(ref _connections);

        public static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public static FakeGHub Start()
        {
            var port = FreePort();
            var server = new FakeGHub { Endpoint = new Uri($"ws://127.0.0.1:{port}/") };
            server._listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            server._listener.Start();
            _ = server.AcceptLoopAsync();
            return server;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }
                var ws = (await context.AcceptWebSocketAsync("json")).WebSocket;
                _socket = ws;
                Interlocked.Increment(ref _connections);
                _ = ServeAsync(ws);
            }
        }

        private async Task ServeAsync(WebSocket ws)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                await SendAsync("""{"msgId":"","verb":"OPTIONS","path":"/","origin":"backend"}""");
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buffer, _stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Received.Enqueue(text);
                    if (text.Contains("/devices/list"))
                        await SendAsync(Fixtures.Read("devices-list.json"));
                    else if (text.Contains("/battery/dev00000006/state"))
                        await SendAsync(Fixtures.Read("battery-keyboard.json"));
                    else if (text.Contains("/battery/dev00000007/state"))
                        await SendAsync(Fixtures.Read("battery-keyboard.json").Replace("dev00000006", "dev00000007"));
                    else if (text.Contains("/battery/"))
                        await SendAsync(Fixtures.Read("battery-no-such-path.json"));
                }
            }
            catch (Exception) when (ws.State != WebSocketState.Open || _stop.IsCancellationRequested)
            {
            }
        }

        public async Task SendAsync(string text)
        {
            if (_socket is { State: WebSocketState.Open } ws)
                await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>Derruba a conexão atual (como o G HUB fechando).</summary>
        public async Task DropAsync()
        {
            if (_socket is { } ws)
            {
                try
                {
                    await ws.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "bye", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }
                ws.Abort();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await DropAsync();
            _listener.Close();
        }
    }
}
