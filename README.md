# VZ — Direct Studio Test Hosting

VZ is a Windows x64 application for starting an unpublished Roblox Studio
test session from a local .rbxl or .rbxlx file.

## Host flow

1. Select the local place file.
2. Click HOST SESSION.
3. VZ launches Roblox Studio's StartServer task with the selected file
   passed via -localPlaceFile.
4. VZ starts a separate StartClient process on the host PC so the host
   can play too.
5. VZ starts an authenticated UDP gateway on local port 55556.
6. VZ asks Windows Firewall to allow that UDP port (one-time UAC approval),
   then requests a temporary UDP port mapping from the router using UPnP IGD.
7. The host receives a VZ5_ invite containing the public IPv4/port, a
   random room ID and an AES-256 key.

## Join flow

A joining PC pastes the VZ5_ invite. VZ binds a local UDP proxy on
127.0.0.1:55555, sends an authenticated keepalive to the host and launches
Studio's StartClient process against that local proxy.

The host gateway allocates a separate UDP socket for each client so the
Studio server sees independent client endpoints and replies return to the
correct player. Tunnel payloads are authenticated and encrypted using
AES-256-GCM. Session keys are generated randomly for each room.

## Network requirements and limitations

This build uses automatic direct UDP port mapping, not a cloud relay.
The host router must support and allow UPnP IGD, and the ISP connection must
have a public IPv4 address. Carrier-grade NAT (CGNAT), double NAT or disabled
UPnP prevents direct remote hosting. The app keeps the local Studio
playtest running and reports the network limitation instead of closing it.

The VZ invite embeds the host's public IPv4 endpoint; only send it to people
you trust. The session key is a bearer secret. The router mapping and
gateway port are removed when the host stops VZ.

No Oracle VM, tunnel account, Porthole/OpenPort install, map upload or
publishing is required for this direct mode. A relay that works through
CGNAT still requires a public server someone operates or a third-party
tunnel provider; this package does not claim otherwise.

## Files

- VZNepTunnel/Program.cs: VZ interface, Studio process launch, encrypted
  multi-client UDP gateway and join proxy.
- VZNepTunnel/UpnpPortMapping.cs: UPnP IGD discovery, UDP mapping and
  cleanup.
- DIRECT-HOSTING.txt: quick-start instructions.

## Compatibility

VZ uses Roblox Studio's unpublished StartServer / StartClient process
tasks. This is an experimental Studio test pathway, not official Roblox
Player matchmaking. Internal flags and networking behavior may change
between Studio releases. A successful compile does not prove a remote
two-PC Studio session; this still needs a live test on two networks.
