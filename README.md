# VZ Direct Multiplayer

VZ is a Windows x64 app for opening local Roblox place files and attempting
unpublished Roblox Studio test multiplayer with a simple host address and
session ID.

## Current design

The host clicks **HOST SESSION**. VZ starts the Studio test server, runs a
token-protected UDP bridge, asks the host router for a UDP port mapping through
UPnP, and displays the router's public IPv4 plus the session ID. Friends enter
those two values in **JOIN SESSION**. They do not install Porthole, create a
relay account, get an Oracle VPS, upload the place, or share a local IP.

## Free direct-hosting condition

Direct incoming connections are possible only when:
- the host router supports UPnP and allows the mapping;
- the host has a public IPv4 address (not CGNAT);
- the host's ISP/router firewall does not block the port.

If the network is behind CGNAT or disallows UPnP, a public relay is needed.
A relay must run on an internet-reachable machine and needs bandwidth. VZ
cannot create that public machine or bandwidth without an operator/hosting
provider. This build reports that limitation instead of presenting a fake
address.

## Files and source

- VZ Windows app: `VZNepTunnel/`
- Direct UPnP port mapping: `VZNepTunnel/UpnpPortMapping.cs`
- Token UDP bridge: `VZNepTunnel/StudioSessionUdpProxy.cs`
- Quick start: `DIRECT-HOSTING.txt`
- Roblox Studio Bridge protocol reference (MIT):
  https://github.com/skja67/robloxstudio-bridge

## Compatibility

VZ uses Studio's local StartServer and StartClient process flags and is
experimental. It is not official Roblox Player matchmaking. The place is not
published or uploaded.
