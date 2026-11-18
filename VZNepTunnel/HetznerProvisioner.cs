using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VZNepTunnel;

internal sealed class HetznerProvisioner
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.hetzner.cloud/v1/") };

    public async Task<(string ip, long id)> CreateRelayAsync(string token, string location, string serverType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Enter a Hetzner API token with Read & Write permission.");
        var cloudInit = @"#cloud-config
package_update: true
packages:
  - golang-go
  - ufw
write_files:
  - path: /opt/vz-relay/main.go
    permissions: '0644'
    content: |
      package main
      import (""log""; ""net""; ""strings""; ""sync""; ""time"")
      type peer struct { addr *net.UDPAddr; seen time.Time }
      var mu sync.Mutex
      var rooms = map[string]map[string]peer{}
      func main() {
        pc, err := net.ListenPacket(""udp"", "":45000""); if err != nil { log.Fatal(err) }
        log.Println(""VZ relay listening on UDP 45000"")
        buf := make([]byte, 65507)
        for { n, addr, err := pc.ReadFrom(buf); if err != nil { continue }; p := append([]byte(nil), buf[:n]...)
          parts := strings.SplitN(string(p), ""|"", 4)
          if len(parts)>=3 && parts[0]==""VZ1"" && (parts[2]==""host"" || parts[2]==""join"") {
            room, role := parts[1], parts[2]; if len(room)<8 || len(room)>64 { continue }
            mu.Lock(); if rooms[room]==nil { rooms[room]=map[string]peer{} }; rooms[room][role]=peer{addr:addr,seen:time.Now()}
            other:=""host""; if role==""host"" { other=""join"" }; target:=rooms[room][other].addr; mu.Unlock()
            if target!=nil { _,_=pc.WriteTo([]byte(""VZ1|""+room+""|READY""), addr) }; continue
          }
          if len(parts)>=3 && parts[0]==""DATA"" { room,role:=parts[1],parts[2]; if role!=""host"" && role!=""join"" { continue }
            other:=""host""; if role==""host"" { other=""join"" }; mu.Lock(); target:=rooms[room][other].addr; mu.Unlock()
            if target!=nil { _,_=pc.WriteTo(p,target) }
          }
        }
      }
runcmd:
  - mkdir -p /opt/vz-relay
  - cd /opt/vz-relay && go build -o /usr/local/bin/vz-relay main.go
  - printf '[Unit]\nDescription=VZ UDP relay\nAfter=network-online.target\n[Service]\nExecStart=/usr/local/bin/vz-relay\nRestart=always\n[Install]\nWantedBy=multi-user.target\n' > /etc/systemd/system/vz-relay.service
  - systemctl daemon-reload
  - systemctl enable --now vz-relay
  - ufw allow 45000/udp
  - ufw --force enable
";
        using var req = new HttpRequestMessage(HttpMethod.Post, "servers");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        var body = new { name = "vz-neptunnel-relay", server_type = serverType, image = "ubuntu-24.04", location, start_after_create = true, user_data = cloudInit, labels = new { app = "vz-neptunnel" } };
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Hetzner API {(int)response.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        var server = doc.RootElement.GetProperty("server");
        var id = server.GetProperty("id").GetInt64();
        var ip = server.GetProperty("public_net").GetProperty("ipv4").GetProperty("ip").GetString() ?? throw new InvalidOperationException("Hetzner did not return a public IPv4 address.");
        return (ip, id);
    }
}
