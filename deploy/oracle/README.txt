VZ SELF-HOSTED RELAY — ORACLE ALWAYS FREE

1. Create an Oracle Cloud account:
   https://www.oracle.com/cloud/free/

2. Create an Ubuntu 22.04 or 24.04 VM in your tenancy's home region.
   Choose Oracle VM.Standard.E2.1.Micro (AMD/x86-64) and assign a public IPv4. Do not use Ampere A1 ARM for this image: the upstream Porthole container build is x86-64.
   Add an SSH public key.

3. Copy setup-vz-relay.sh to the VM and run:
   sudo bash setup-vz-relay.sh YOUR_PUBLIC_IP

   The script installs Docker, deploys the open-source Porthole relay,
   creates a random relay secret, and configures UFW.

4. IMPORTANT: Oracle's VCN Security List or Network Security Group is a
   separate cloud firewall. Add inbound IPv4 rules:
   - TCP 7835
   - TCP 10000-20000
   - UDP 10000-20000
   Source 0.0.0.0/0 for tunnel ports. Restrict SSH and, where practical,
   restrict TCP 7835 to your own/friends' IPs.

5. Print the relay connection code:
   cd /opt/vz-porthole
   sudo docker compose run --rm porthole invite

6. Paste the porthole1_... code into VZ > Host Session > Relay Connection Code.
   Keep it private: it contains the relay authentication secret.

7. On the host PC, select your local .rbxl/.rbxlx file in VZ and click
   START HOST SESSION. VZ launches Studio StartServer on UDP 55555, starts
   a token-protected bridge on UDP 55556, and creates a public UDP tunnel.
   Share the generated VZ3_ invite with friends, never the porthole1_ code.

8. Friends run VZ, paste the VZ3_ invite into Join Session, and click
   CONNECT & START CLIENT. They do not need Porthole or a local map copy.

FREE-TIER REALITIES
- Oracle currently documents up to two Always Free AMD VM.Standard.E2.1.Micro
  instances (each 1/8 OCPU, 1 GB RAM and up to 50 Mbps) plus 10 TB/month
  outbound transfer. Regional capacity and quotas apply.
- Oracle may require a payment card at signup. Keep usage within Always Free
  limits and monitor the tenancy's resource usage.
- The relay is your VM, not a hosted service operated by VZ.

ROBLOX STUDIO COMPATIBILITY
VZ follows the unpublished Studio StartServer / StartClient test-session flow
used by the MIT Studio Bridge reference project. The session uses local UDP
55555, a token-gated UDP bridge on 55556, and Porthole for the public relay.
This is not public Roblox Player matchmaking. Studio may change its internal
startup flags; both PCs need a compatible Studio version. VZ does not upload
or publish your place.
