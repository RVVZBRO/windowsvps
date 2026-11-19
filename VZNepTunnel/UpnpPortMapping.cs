using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VZNepTunnel;

/// <summary>
/// Attempts a direct public UDP mapping on the host's router using UPnP IGD.
/// No cloud account, relay service, or third-party client is needed. This
/// cannot punch through ISP CGNAT or routers where UPnP is disabled.
/// </summary>
internal sealed class UpnpPortMapping : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private Uri? _controlUri;
    private string? _serviceType;
    private ushort _externalPort;
    private bool _disposed;

    public IPAddress PublicAddress { get; private set; } = IPAddress.None;
    public ushort ExternalPort => _externalPort;

    public static async Task<UpnpPortMapping> CreateAsync(ushort internalPort, CancellationToken cancellationToken = default)
    {
        var mapping = new UpnpPortMapping();
        try
        {
            var (controlUri, serviceType) = await DiscoverControlServiceAsync(cancellationToken);
            mapping._controlUri = controlUri;
            mapping._serviceType = serviceType;
            var localAddress = GetLocalIPv4Address();

            Exception? lastError = null;
            IEnumerable<ushort> candidates = new[] { internalPort }
                .Concat(Enumerable.Range(42000, 128).Select(p => (ushort)p).Where(p => p != internalPort));

            foreach (var externalPort in candidates)
            {
                try
                {
                    await mapping.SoapAsync("AddPortMapping", new Dictionary<string, string>
                    {
                        ["NewRemoteHost"] = "",
                        ["NewExternalPort"] = externalPort.ToString(),
                        ["NewProtocol"] = "UDP",
                        ["NewInternalPort"] = internalPort.ToString(),
                        ["NewInternalClient"] = localAddress.ToString(),
                        ["NewEnabled"] = "1",
                        ["NewPortMappingDescription"] = "VZ Studio Session",
                        ["NewLeaseDuration"] = "0"
                    }, cancellationToken);

                    mapping._externalPort = externalPort;
                    string addressText = await mapping.GetExternalAddressAsync(cancellationToken);
                    if (!IPAddress.TryParse(addressText, out var publicAddress) || !IsPublicIPv4(publicAddress))
                        throw new InvalidOperationException($"Your router reports {addressText} as its internet address. That is not a public IPv4 address; your ISP may be using CGNAT.");

                    mapping.PublicAddress = publicAddress;
                    return mapping;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (mapping._externalPort != 0) break;
                }
            }

            throw new InvalidOperationException(
                "VZ could not create a direct public UDP port mapping. Your router may have UPnP disabled, the port may be blocked, or your ISP may use CGNAT.",
                lastError);
        }
        catch
        {
            mapping.Dispose();
            throw;
        }
    }

    private static async Task<(Uri ControlUri, string ServiceType)> DiscoverControlServiceAsync(CancellationToken cancellationToken)
    {
        string[] serviceTypes =
        {
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1"
        };

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        udp.EnableBroadcast = true;
        var multicast = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

        foreach (var service in serviceTypes)
        {
            string search = "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: 239.255.255.250:1900\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 2\r\n" +
                $"ST: {service}\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(search);
            await udp.SendAsync(bytes, bytes.Length, multicast);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        while (!timeout.IsCancellationRequested)
        {
            UdpReceiveResult response;
            try { response = await udp.ReceiveAsync(timeout.Token); }
            catch (OperationCanceledException) { break; }

            string text = Encoding.ASCII.GetString(response.Buffer);
            var match = Regex.Match(text, @"(?im)^LOCATION\s*:\s*(.+?)\s*$");
            if (!match.Success || !Uri.TryCreate(match.Groups[1].Value.Trim(), UriKind.Absolute, out var location))
                continue;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                string xml = await http.GetStringAsync(location, cancellationToken);
                var document = XDocument.Parse(xml);
                foreach (var serviceNode in document.Descendants().Where(e => e.Name.LocalName == "service"))
                {
                    string type = serviceNode.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value.Trim() ?? "";
                    string control = serviceNode.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value.Trim() ?? "";
                    if (!(type.Contains("WANIPConnection", StringComparison.OrdinalIgnoreCase) ||
                          type.Contains("WANPPPConnection", StringComparison.OrdinalIgnoreCase)) || string.IsNullOrWhiteSpace(control))
                        continue;
                    return (new Uri(location, control), type);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
        }

        throw new TimeoutException("No UPnP internet gateway was found. Your router may have UPnP disabled.");
    }

    private async Task<string> GetExternalAddressAsync(CancellationToken cancellationToken)
    {
        string xml = await SoapAsync("GetExternalIPAddress", new Dictionary<string, string>(), cancellationToken);
        var doc = XDocument.Parse(xml);
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value.Trim()
            ?? throw new InvalidOperationException("The router did not return an external IPv4 address.");
    }

    private async Task<string> SoapAsync(string action, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        if (_controlUri == null || _serviceType == null) throw new InvalidOperationException("UPnP service not discovered.");
        var body = new StringBuilder();
        body.Append("<?xml version=\"1.0\"?>");
        body.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
        body.Append("<s:Body><u:").Append(action).Append(" xmlns:u=\"").Append(SecurityElement.Escape(_serviceType)).Append("\">");
        foreach (var item in values)
            body.Append('<').Append(item.Key).Append('>').Append(SecurityElement.Escape(item.Value)).Append("</").Append(item.Key).Append('>');
        body.Append("</u:").Append(action).Append("></s:Body></s:Envelope>");

        using var request = new HttpRequestMessage(HttpMethod.Post, _controlUri);
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{_serviceType}#{action}\"");
        request.Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml");
        using var response = await _http.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Router UPnP {action} failed: {response.StatusCode} {responseBody}");
        return responseBody;
    }

    private static IPAddress GetLocalIPv4Address()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
        return ((IPEndPoint)socket.LocalEndPoint!).Address;
    }

    private static bool IsPublicIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = address.GetAddressBytes();
        if (b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224) return false;
        if (b[0] == 100 && b[1] is >= 64 and <= 127) return false; // carrier-grade NAT
        if (b[0] == 169 && b[1] == 254) return false;
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return false;
        if (b[0] == 192 && b[1] == 168) return false;
        if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;
        if (b[0] == 198 && b[1] is 18 or 19) return false;
        if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_externalPort != 0 && _controlUri != null && _serviceType != null)
        {
            try
            {
                SoapAsync("DeletePortMapping", new Dictionary<string, string>
                {
                    ["NewRemoteHost"] = "",
                    ["NewExternalPort"] = _externalPort.ToString(),
                    ["NewProtocol"] = "UDP"
                }, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { }
        }
        _http.Dispose();
    }
}
