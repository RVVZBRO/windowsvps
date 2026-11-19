VZ SHARED RELAY — ORACLE CLOUD ALWAYS FREE

This deploys one shared VZ UDP relay. Players do not create VPS accounts,
install Porthole, configure UPnP, or expose their home IP.

ONE-TIME OWNER SETUP

1. Create the Oracle Cloud Free Tier account:
   https://www.oracle.com/cloud/free/

2. Create a Linux Ubuntu VM in the tenancy's home region.
   Recommended: VM.Standard.A1.Flex with 2 OCPUs and 12 GB RAM total
   (Always Free quota, subject to region capacity). It is ARM64; the setup
   script compiles the VZ Go relay natively for ARM. An AMD E2.1.Micro
   x86-64 VM can also run the relay but has much less RAM/CPU.

   Assign a public IPv4 and add your SSH public key. Do not allocate paid
   shapes or paid boot-volume capacity.

3. Connect to the VM over SSH and run:
   curl -fsSL https://raw.githubusercontent.com/RVVZBRO/windowsvps/vz-neptunnel-dotnet/deploy/oracle/setup-vz-relay.sh | sudo bash

   The installer builds a small Go UDP relay, installs a systemd service,
   and opens UDP port 45000 in the Ubuntu firewall.

4. In the Oracle Cloud console, open the VM's VCN Security List or Network
   Security Group and add an INGRESS rule:
   - Source CIDR: 0.0.0.0/0
   - IP Protocol: UDP
   - Destination port: 45000

   Oracle's cloud firewall is separate from the Ubuntu firewall.

5. The installer prints the VM's public IPv4. In the RVVZBRO/windowsvps
   repository, edit relay/endpoint.txt to contain exactly:
   YOUR_PUBLIC_IPV4:45000

   Commit that one-line endpoint. VZ downloads this public endpoint
   configuration automatically; players do not enter it themselves.

6. Download the latest VZ Windows build from the GitHub Actions artifact
   named VZ-Shared-Relay-Windows-x64. Run VZ.exe.

PLAYER FLOW

Host:
- Choose a local .rbxl/.rbxlx file.
- Click HOST SESSION.
- Copy the VZ4_ invite and send it to friends.

Join:
- Open VZ, paste the VZ4_ invite, click JOIN SESSION.

The host keeps VZ and Roblox Studio running for the session. Friends need
the VZ application and a compatible Roblox Studio installation; they do
not need a copy of the map. VZ does not publish the map to Roblox.

SECURITY / LIMITS

- The host key is generated per room and is not included in the invite.
- Each joining PC receives a random client ID and per-client key.
- Studio UDP payloads are encrypted with AES-256-GCM before relay transport.
- The invite is a bearer credential: share it only with intended players.
- The service currently caps rooms and players per room; monitor bandwidth
  and logs before inviting a large public audience.
- This is an experimental unpublished Roblox Studio StartServer/StartClient
  pathway, not official Roblox Player matchmaking. Studio can change its
  internal flags or networking protocol.
- Oracle Always Free capacity and policies can change. Always check the
  Oracle console for resource usage; this project cannot guarantee that
  Oracle will keep a free VM available in every home region.
