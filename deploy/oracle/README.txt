VZ SELF-HOSTED RELAY — ORACLE ALWAYS FREE

1. Create an Oracle Cloud account:
   https://www.oracle.com/cloud/free/

2. Create an Ubuntu 22.04 or 24.04 VM in your tenancy's home region.
   Choose an Always Free eligible compute shape and assign a public IPv4.
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

7. Start a compatible local TCP/UDP service on your PC. Set the matching
   local port in VZ, choose TCP, UDP or both, and click START HOST TUNNEL.
   Share the VZ2_ invite, not the private porthole1_ relay code.

FREE-TIER REALITIES
- Oracle currently documents Always Free Ampere A1 compute up to 2 OCPUs /
  12 GB RAM and 10 TB/month outbound transfer. Eligibility, regional host
  capacity and quotas apply. Oracle can reclaim idle compute under its policy.
- Oracle may require a payment card at signup. Keep usage within Always Free
  limits and monitor the tenancy's resource usage.
- The relay is your VM, not a hosted service operated by VZ.

ROBLOX STUDIO LIMIT
The tunnel forwards raw TCP/UDP. It does not change Roblox Studio's network
protocol or turn Studio local test into a public Roblox server. Use it only
with a service/client that actually accepts the forwarded endpoint. VZ does
not publish your place.
