# VZ Playable Client (prototype)

A VZ-native 3D multiplayer prototype, not a Roblox engine.

## Controls
- WASD: move
- Space: jump
- Click the world: capture mouse
- Escape: release mouse

## Local multiplayer test
1. Start VZClient.exe on one PC and press HOST LOCAL SESSION.
2. On another PC, enter the host's reachable address and UDP port (default 7777), then press JOIN SESSION.
3. Players synchronize through the VZ server using Godot ENet.

For Internet play, configure a UDP Portwarp tunnel whose local port matches the VZ port. Select pwrp.exe, enter the exact tunnel name, and connect it. Enter the public hostname in the join address field on the other PC. Portwarp account sign-in and dashboard tunnel creation are required first.

## Map support
VZ can load external .glb and .gltf scenes. Roblox .rbxl/.rbxlx files are not directly executable. They require conversion of supported geometry and assets to glTF; Roblox Luau scripts, physics behavior, and services are not converted into a Roblox-compatible runtime by this prototype.

## Security / status
Early test build. No production authentication, moderation, persistence, anti-cheat, or robust authoritative movement validation. Do not expose it to untrusted public users yet.
