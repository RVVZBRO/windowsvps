# VZ — Shared Relay Multiplayer

VZ is a Windows x64 desktop app for starting an experimental unpublished
Roblox Studio test session and letting friends join through one shared UDP
relay. Players use only VZ and Roblox Studio; they do not install a tunnel
program or create a cloud account.

## Player experience

**Host:** choose a local `.rbxl` / `.rbxlx` file, click **HOST SESSION**, and
share the generated `VZ4_` invite.

**Join:** paste the invite in **JOIN SESSION** and click **JOIN SESSION**.

The host's map is not uploaded or published. Friends do not need their own
copy of the map.

## Architecture

- One owner-operated public UDP relay listens on UDP 45000.
- VZ discovers the public relay endpoint from `relay/endpoint.txt`.
- Hosts register rooms using a random room ID and a separate host secret.
- Each joiner registers a random client ID and client key.
- Session UDP payloads are protected using AES-256-GCM between the host and
  joiner; the relay only routes the encrypted datagrams.
- Rooms expire after the host stops heartbeats. Inactive joiners expire too.
- The relay limits simultaneous rooms and joiners per room.

## One-time owner deployment

See `deploy/oracle/README.txt`. Recommended free-tier machine is Oracle
Cloud `VM.Standard.A1.Flex` (up to 2 OCPUs and 12 GB RAM in Always Free
quota, subject to availability). The Go relay compiles natively on ARM64.
The smaller AMD `VM.Standard.E2.1.Micro` is also supported.

After deploying the VM and opening UDP 45000 in the Oracle VCN/NSG firewall,
set `relay/endpoint.txt` to `PUBLIC_IPV4:45000`. VZ clients fetch that value
automatically; players never configure a relay address.

## Source

- Windows app + UDP client: `VZNepTunnel/Program.cs`
- Go relay: `relay/main.go`
- Oracle installer: `deploy/oracle/setup-vz-relay.sh`
- Owner instructions: `deploy/oracle/README.txt`

## Compatibility notes

The multiplayer startup uses Roblox Studio's unpublished `StartServer` and
`StartClient` process flags, based on community reference projects. This is
not official Roblox Player matchmaking. Internal flags and network behavior
may change between Studio versions. Build success does not prove that a
two-PC remote session works; that still requires a live test with two PCs.
