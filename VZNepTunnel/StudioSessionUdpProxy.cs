using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VZNepTunnel;

/// <summary>
/// Token-gated UDP bridge adapted for a Roblox Studio local test session.
/// Host listens on UDP 55556 and forwards authenticated datagrams to the
/// local Studio test server on UDP 55555.
/// </summary>
internal sealed class HostSessionUdpRelay : IDisposable
{
    public const int StudioServerPort = 55555;
    public const int HostRelayPort = 55556;

    private readonly byte[] _token;
    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, PeerRoute> _routes = new();

    private sealed record PeerRoute(IPEndPoint Remote, UdpClient Socket);

    public HostSessionUdpRelay(string token)
    {
        _token = Encoding.ASCII.GetBytes(token);
        if (_token.Length < 16) throw new ArgumentException("Session token must be at least 128 bits.", nameof(token));
        _listener = new UdpClient(new IPEndPoint(IPAddress.Any, HostRelayPort));
    }

    public void Start() => _ = Task.Run(ReceiveClientsAsync);

    private async Task ReceiveClientsAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var packet = await _listener.ReceiveAsync(_stop.Token);
                var data = packet.Buffer;
                if (data.Length <= _token.Length || !data.AsSpan(0, _token.Length).SequenceEqual(_token))
                    continue;

                string key = packet.RemoteEndPoint.ToString();
                var route = _routes.GetOrAdd(key, _ =>
                {
                    var socket = new UdpClient(AddressFamily.InterNetwork);
                    var created = new PeerRoute(packet.RemoteEndPoint, socket);
                    _ = Task.Run(() => SendStudioRepliesAsync(created));
                    return created;
                });

                await route.Socket.SendAsync(data.AsMemory(_token.Length), new IPEndPoint(IPAddress.Loopback, StudioServerPort), _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task SendStudioRepliesAsync(PeerRoute route)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var reply = await route.Socket.ReceiveAsync(_stop.Token);
                var outgoing = new byte[_token.Length + reply.Buffer.Length];
                Buffer.BlockCopy(_token, 0, outgoing, 0, _token.Length);
                Buffer.BlockCopy(reply.Buffer, 0, outgoing, _token.Length, reply.Buffer.Length);
                await _listener.SendAsync(outgoing, route.Remote, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        foreach (var route in _routes.Values) route.Socket.Dispose();
        _routes.Clear();
        _stop.Dispose();
    }
}

/// <summary>
/// Join-side UDP loopback proxy. Studio StartClient talks to 127.0.0.1:55555;
/// this proxy prefixes the session token and forwards to the host's public
/// Porthole UDP endpoint.
/// </summary>
internal sealed class JoinSessionUdpProxy : IDisposable
{
    private readonly byte[] _token;
    private readonly IPEndPoint _host;
    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, UdpClient> _routes = new();

    public const int LocalClientPort = HostSessionUdpRelay.StudioServerPort;

    public JoinSessionUdpProxy(string host, int port, string token)
    {
        IPAddress[] addresses = Dns.GetHostAddresses(host);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new SocketException((int)SocketError.HostNotFound);
        _host = new IPEndPoint(address, port);
        _token = Encoding.ASCII.GetBytes(token);
        if (_token.Length < 16) throw new ArgumentException("Session token must be at least 128 bits.", nameof(token));
        _listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, LocalClientPort));
    }

    public void Start()
    {
        _ = Task.Run(ReceiveStudioClientsAsync);
        _ = Task.Run(WarmupAsync);
    }

    private async Task WarmupAsync()
    {
        try
        {
            using var socket = new UdpClient(AddressFamily.InterNetwork);
            var probe = new byte[_token.Length + 64];
            Buffer.BlockCopy(_token, 0, probe, 0, _token.Length);
            for (int i = 0; i < 8 && !_stop.IsCancellationRequested; i++)
            {
                await socket.SendAsync(probe, _host, _stop.Token);
                await Task.Delay(70, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task ReceiveStudioClientsAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var packet = await _listener.ReceiveAsync(_stop.Token);
                string key = packet.RemoteEndPoint.ToString();
                var socket = _routes.GetOrAdd(key, _ =>
                {
                    var created = new UdpClient(AddressFamily.InterNetwork);
                    _ = Task.Run(() => ReceiveHostRepliesAsync(created, packet.RemoteEndPoint));
                    return created;
                });

                var outgoing = new byte[_token.Length + packet.Buffer.Length];
                Buffer.BlockCopy(_token, 0, outgoing, 0, _token.Length);
                Buffer.BlockCopy(packet.Buffer, 0, outgoing, _token.Length, packet.Buffer.Length);
                await socket.SendAsync(outgoing, _host, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ReceiveHostRepliesAsync(UdpClient socket, IPEndPoint studioClient)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var packet = await socket.ReceiveAsync(_stop.Token);
                var data = packet.Buffer;
                if (data.Length < _token.Length || !data.AsSpan(0, _token.Length).SequenceEqual(_token))
                    continue;
                await _listener.SendAsync(data.AsMemory(_token.Length), studioClient, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        foreach (var socket in _routes.Values) socket.Dispose();
        _routes.Clear();
        _stop.Dispose();
    }
}
