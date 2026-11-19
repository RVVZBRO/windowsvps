# VZ — Direct Roblox Studio Test Hosting

VZ is a Windows x64 desktop app for starting an unpublished Roblox Studio
test session from a local .rbxl or .rbxlx place file and letting friends
join with a VZ invite.

## Host

1. Select a local place file.
2. Click HOST SESSION.
3. VZ starts Studio's StartServer task using the selected -localPlaceFile.
4. VZ waits for the Studio server to bind UDP 55555.
5. VZ launches a separate StartClient process so the host can play too.
6. VZ starts an encrypted UDP gateway on port 55556 and requests a one-hour
   UPnP IGD mapping. The mapping is renewed while hosting.
7. Copy the generated VZ5_ invite and share it only with intended friends.

## Join

1. Open VZ and choose JOIN SESSION.
2. Paste the host's VZ5_ invite.
3. VZ validates the invitation, binds a local UDP proxy on 127.0.0.1:55555,
   confirms that the host gateway responds, and starts Studio's StartClient.

The host's map stays on the host PC. VZ does not publish or upload it.

## Network behaviour

- Direct internet hosting requires a router that allows UPnP IGD and an ISP
  public IPv4 address. VZ asks Windows Firewall for one UDP 55556 allow rule.
- Same-LAN fallback: if the host's router cannot publish a WAN mapping,
  VZ keeps the authenticated UDP gateway running and produces a LAN invite.
  Friends on the same Wi-Fi/LAN can join using that invite.
- CGNAT / restrictive NAT: direct remote hosting cannot bypass these
  conditions. Internet friends need a public relay or third-party tunnel
  service. VZ does not pretend that a local tunnel can create a public VM.

UPnP mappings use a one-hour lease and VZ renews them every 20 minutes while
the session is active. If VZ crashes, the router can remove the mapping after
the lease expires. A normal Stop removes it immediately.

## Security

- Each session gets random room and AES-256-GCM session keys.
- Datagrams are encrypted and authenticated before they are forwarded.
- The host gateway keeps a separate UDP source socket for each joining client
  so replies can return to the correct Studio test client.
- The invite is a bearer secret containing the public (or LAN) endpoint and
  session key. Share it privately. It also reveals the endpoint to recipients.
- The gateway caps concurrent joining clients at 16.

## Compatibility

VZ uses Roblox Studio's internal StartServer / StartClient process tasks.
-localPlaceFile is used to attach the selected local place to the server
process. This is an experimental Studio test pathway, not official Roblox
Player matchmaking. Internal flags and networking may change between Studio
releases.

A successful Windows build is not proof of a remote two-PC session. Real
validation still requires running two separate PCs on two separate networks.
