# VZ — Unpublished Roblox Studio Multiplayer

VZ is a Windows x64 app for opening a local Roblox place in Studio and creating
an unpublished Studio test session that friends can join using a VZ invite.
The session bridge follows the UDP host/client pattern used by the MIT-licensed
[Roblox Studio Bridge project](https://github.com/skja67/robloxstudio-bridge),
with [Porthole](https://github.com/sudo-ds/porthole) as the self-hosted UDP relay.

## Included in the Windows ZIP

- VZ.exe — self-contained Windows desktop application.
- tools/porthole.exe — official Porthole v0.5.12 Windows x64 tunnel client.
- deploy/oracle/setup-vz-relay.sh — Ubuntu/Oracle Always Free relay setup script.
- deploy/oracle/README.txt — relay setup and cloud firewall guide.

## One-time relay setup (host only)

Porthole is open-source software, but it needs a public relay server. To avoid
a monthly VPS bill, you can host that relay on Oracle Cloud Always Free:

1. Create an Oracle Cloud account at https://www.oracle.com/cloud/free/.
2. Create an Ubuntu 22.04/24.04 **VM.Standard.E2.1.Micro (AMD x86-64)** with a
   public IPv4 address. The published Porthole container used by this script
   is x86-64; do not pick the ARM A1 VM for this deployment.
3. Run deploy/oracle/setup-vz-relay.sh on the VM as root:
   sudo bash setup-vz-relay.sh YOUR_PUBLIC_IP
4. In Oracle's VCN Security List / Network Security Group, add inbound rules:
   - TCP 7835
   - TCP 10000-20000
   - UDP 10000-20000
   UFW on the guest VM does not replace the Oracle cloud firewall.
5. Print your private Porthole client code:
   cd /opt/vz-porthole && sudo docker compose run --rm porthole invite
6. Paste the entire porthole1_... code into VZ's Host Session tab.

Oracle currently lists up to two Always Free AMD VM.Standard.E2.1.Micro
instances (each 1/8 OCPU, 1 GB RAM, up to 50 Mbps) and 10 TB/month outbound
transfer, subject to availability, quotas and current terms. Signup may require
a card. Check current limits:
https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm

Only the host needs the Porthole relay code and Porthole client. Friends only
need the VZ Windows app.

## Host a Studio session

1. Start VZ and choose your local .rbxl or .rbxlx file at the top.
2. Open the Host Session tab and paste the private porthole1_ relay code.
3. Click START HOST SESSION.
4. VZ opens the local place in Studio, starts a Studio StartServer test process,
   creates a token-protected UDP bridge, then requests a public UDP port from
   your Porthole relay.
5. When the status says the session is live, share the VZ3_ invite with friends.
   Never share the private porthole1_ relay code.

The host uses local Studio UDP 55555 and a token-protected VZ UDP relay on
55556. Porthole exposes the relay's UDP port to the Internet.

## Join a Studio session

1. Friend opens VZ and locates their Roblox Studio if it is not detected.
2. Paste the host's VZ3_ invite in Join Session.
3. Click CONNECT & START CLIENT.

VZ creates the friend's local UDP proxy on 127.0.0.1:55555 and starts Studio's
StartClient test process with a fresh local test-session identity. Friends do
not need the Porthole client, the host's IP address, or a copy of the map file.

## Important compatibility notes

- This uses Roblox Studio's local StartServer / StartClient test flow. It is
  not Roblox Player internet matchmaking and it does not publish the place.
- Both sides need a Studio version that still supports the startup flags used
  by the upstream Studio Bridge pattern. Roblox may change internal behavior.
- A test session only lasts while the host's Studio server, VZ app and relay
  tunnel remain running.
- This is an experimental peer-to-peer-style bridge over a token-gated UDP
  tunnel. Internet routing, firewalls, NATs, Oracle capacity and Studio version
  can affect whether a friend can connect.
- The invite is a connection secret. Share it only with people you trust.

## Source and licensing

- VZ source: https://github.com/RVVZBRO/windowsvps/tree/vz-neptunnel-dotnet
- Porthole (MIT): https://github.com/sudo-ds/porthole
- Roblox Studio Bridge (MIT, protocol reference): https://github.com/skja67/robloxstudio-bridge

