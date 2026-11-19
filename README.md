# VZ NepTunnel

VZ NepTunnel is a Windows x64 desktop session launcher with Portwarp tunnel controls.

## What this branch currently provides

- WinForms desktop UI for hosting and joining.
- Portwarp CLI setup (download from Portwarp's official downloads page and select the extracted `pwrp.exe`).
- Browser/device sign-in using `pwrp login`.
- List, connect, and stop configured Portwarp tunnels.
- Launch a user-selected dedicated game-server executable.
- Pass optional server arguments with `{map}` and `{port}` substitutions.
- Generate a VZ1 invite containing the public Portwarp hostname and port.
- Decode invites and launch a user-selected client with `{host}` and `{port}` substitutions.
- Activity log, appearance settings, and optional Discord Rich Presence.

## First-time Portwarp setup

1. Install/update Portwarp CLI from the Portwarp downloads page using the Portwarp tab in VZ.
2. Click **Sign in to Portwarp** and approve the device in your browser.
3. Open the Portwarp dashboard and create/enable a tunnel using the game server's local port and the correct protocol (TCP or UDP).
4. Copy the tunnel name into VZ's Host tab.
5. Keep the local server port and Portwarp tunnel's configured local port identical.

## Hosting

1. Select the local map if the game server accepts a map path.
2. Select the dedicated server executable.
3. Add its documented command-line arguments. Use `{map}` for the selected map path and `{port}` for the configured local port.
4. Enter the Portwarp tunnel name and click **HOST GAME**.
5. Copy the public hostname and port from the Portwarp dashboard into VZ's public address field.
6. Click **Create / refresh invite** and share the resulting VZ1 code with friends.
7. Click **STOP GAME** to stop the server and ask Portwarp to stop the tunnel.

## Joining

1. Paste the host's VZ1 invite code.
2. Select the matching game client executable.
3. Set its documented arguments, using `{host}` and `{port}` for the endpoint.
4. Click **JOIN GAME**.

## Important compatibility and security limits

- Portwarp is a network tunnel, not a game server, matchmaking service, or game engine.
- This app does not include a Roblox-compatible runtime or dedicated Roblox place server. A `.rbxl` / `.rbxlx` file cannot be executed by this app as a standalone multiplayer server. The existing Studio plugin can still be used for local Studio testing/import workflows, but it does not make remote Internet gameplay work.
- For Roblox multiplayer, a compatible server/runtime and network protocol integration are still required. The generic server/client launcher only works with a game that provides its own compatible dedicated server and client executables.
- VZ1 invites encode the public endpoint and a checksum. They are shareable convenience codes, **not authentication or access-control tokens**. Do not expose an unauthenticated game server; configure access control in the game server itself.
- The Portwarp endpoint is copied manually from the dashboard in this version. VZ does not yet discover public endpoints automatically.
- TCP/UDP protocol and local port must match the game server and the Portwarp dashboard tunnel. VZ does not transparently convert protocols.
- End-to-end tunnel and multiplayer tests require a real Portwarp account, a configured tunnel, and a compatible dedicated server/client.

## Build

GitHub Actions builds the self-contained Windows x64 executable on pushes to `vz-neptunnel-dotnet`. The artifact is named `VZ-NepTunnel-Windows-x64`.


---

# VZ Playable Client prototype

A separate Godot 4.5.1 native 3D game prototype now lives in [VZGame](VZGame/README.md). It includes a 3D test arena, WASD movement and jump, ENet host/join networking, GLB/GLTF map import, and an optional Portwarp CLI connect action.

**Build:** GitHub Actions workflow [Build VZ Windows Client](.github/workflows/build-vz-game.yml) exports `VZClient.exe` as the `VZ-Playable-Client-Windows-x64` artifact.

**Compatibility:** This is a new VZ-native runtime, not a Roblox-compatible engine. Roblox `.rbxl` places must be converted to supported glTF content; Luau scripts and Roblox services do not run in this prototype. The Windows export and real multi-PC/Portwarp session still need to pass CI and user testing before this should be called a release.
