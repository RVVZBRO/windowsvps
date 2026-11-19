# VZ — Roblox Studio Launcher + Self-Hosted Tunnel

VZ is a Windows x64 desktop app that opens local Roblox .rbxl/.rbxlx files in
Studio and provides a UI for a self-hosted TCP/UDP relay powered by the
[Porthole project](https://github.com/sudo-ds/porthole) (MIT).

## Included in the Windows ZIP

- VZ.exe — self-contained Windows desktop UI.
- tools/porthole.exe — Porthole Windows tunnel engine.
- deploy/oracle/setup-vz-relay.sh — Ubuntu/Oracle Always Free relay bootstrap.
- deploy/oracle/README.txt — setup and cloud firewall instructions.

## One-time relay setup

The free/open-source tunnel software is not itself a free hosted relay. To
get a public address and port without paying a VPS bill, run your own relay
on Oracle Cloud Always Free, subject to the provider's quotas and capacity:

1. Sign up at https://www.oracle.com/cloud/free/ and create an Ubuntu VM
   in your home region using an Always Free eligible shape.
2. Assign it a public IPv4 address.
3. Run deploy/oracle/setup-vz-relay.sh on the VM as root.
4. In Oracle's VCN Security List or Network Security Group, separately open
   TCP 7835, TCP 10000-20000 and UDP 10000-20000. UFW alone does not open
   Oracle's cloud firewall.
5. Run: cd /opt/vz-porthole && sudo docker compose run --rm porthole invite
6. Paste the generated porthole1_... code into VZ's Host Session tab.

Oracle lists Always Free compute resources and up to 10 TB/month outbound
transfer. A card may be required at signup; regional capacity is not
guaranteed. Review current limits:
https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm

## Host a tunnel

1. Start a compatible local server/service on your PC.
2. In VZ, select TCP, UDP or both and enter that server's local port.
3. Paste the private Porthole relay code.
4. Click START HOST TUNNEL and wait for the live public endpoint.
5. Share the VZ2_ invite with friends. Never share the porthole1_ code.

Porthole assigns a public port from the relay's configured allocation range.
VZ displays it only after the Porthole client confirms the tunnel is live.

## Join

Paste a VZ2_ invite in Join Session to decode and display its public endpoint.
The remote game client must support connecting directly to that hostname and
port. VZ does not proxy or patch an arbitrary game client's network stack.

## Roblox compatibility

VZ opens the local place in Studio and can tunnel a raw TCP/UDP listener. It
does not upload or publish the place. A tunnel cannot make an unsupported
Roblox Studio local-test protocol accept remote Roblox Player connections.
This build does not claim otherwise and does not install a Studio plugin.

## Security

- Keep the porthole1_ relay code private; it includes a shared secret and TLS
  certificate fingerprint.
- Share only the VZ2_ public endpoint invite.
- The VZ invite is an endpoint descriptor, not per-player authentication.
  Configure access control in the game/server itself.
- Tunnel data encryption can be enabled in the Host Session tab.
- Keep Porthole's local status API bound to loopback.

## Source and build

Porthole source/license: https://github.com/sudo-ds/porthole
VZ source: https://github.com/RVVZBRO/windowsvps/tree/vz-neptunnel-dotnet
GitHub Actions builds the Windows ZIP on pushes to vz-neptunnel-dotnet.
