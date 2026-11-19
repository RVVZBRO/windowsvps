# VZ Direct Multiplayer

VZ is a Windows x64 desktop app for opening a local Roblox place file and
starting an unpublished Roblox Studio test session that friends can join by
entering a public address/port and a session ID.

## How it works

1. Choose a local .rbxl or .rbxlx file and click **HOST SESSION**.
2. VZ opens the place in Studio and launches the Studio StartServer test process.
3. VZ creates a token-protected UDP bridge and asks the home router to expose it
   using UPnP. It then displays the public IPv4 address and UDP port.
4. Share that address and the generated 32-character session ID with friends.
5. Friends enter the address and session ID in **JOIN SESSION**. VZ starts a
   local UDP proxy and launches the Studio StartClient process.

Friends do not need the host's map file, Porthole, an Oracle account, or a
separate tunnel program. The host place is not uploaded or published.

## Free direct-hosting requirement

This build uses direct router port mapping. It only works when the host's
router supports and allows UPnP and the ISP provides a publicly reachable IPv4
address. Some ISPs use Carrier-Grade NAT (CGNAT), and some routers disable
UPnP. In those cases, a direct incoming connection cannot be established.

Bypassing CGNAT requires an internet-reachable relay server. Such a relay needs
a real machine and bandwidth somewhere; VZ cannot create free public compute
or bandwidth by itself. This build deliberately reports a direct-connect
failure instead of inventing a fake tunnel address.

## Source files

- Windows app: `VZNepTunnel/Program.cs`
- UPnP router mapping: `VZNepTunnel/UpnpPortMapping.cs`
- Token UDP bridge: `VZNepTunnel/StudioSessionUdpProxy.cs`
- Quick start: `DIRECT-HOSTING.txt`

The Studio launch flow is based on the open-source
[Roblox Studio Bridge](https://github.com/skja67/robloxstudio-bridge) project.

## Compatibility

This is an experimental unpublished Studio test path using Studio's
StartServer/StartClient process flags. It is not official Roblox Player
matchmaking. Roblox may change these internal startup flags or networking
behavior. The host must keep VZ and the Studio test server running.
