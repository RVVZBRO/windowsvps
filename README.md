# VZ NepTunnel

Windows x64 desktop build of the VZ NepTunnel Python/CustomTkinter application.

## Build
GitHub Actions on branch `vz-neptunnel-build` builds with `windows-latest` and publishes `VZ-NepTunnel-Windows-x64` as a workflow artifact.

## Functionality status
This build verifies Python syntax, PyInstaller packaging, and the resulting Windows PE file. It does **not** certify live Roblox multiplayer networking. Portwarp API availability, the Portwarp CLI, Roblox Studio launch arguments, and the RBXM Studio plugin bridge require separate end-to-end tests with a real Portwarp account and Roblox Studio installation. The RBXM bridge is not implemented in this prototype.


Native .NET app branch: `vz-neptunnel-dotnet`. Build workflow: `.github/workflows/build-dotnet.yml`.

Build validation pass after fixing C# compile errors.


## Native VZ NepTunnel app additions
- Hetzner Cloud API provisioning UI creates an Ubuntu relay VM and configures a UDP relay on port 45000. A Hetzner API token with Read & Write permission is required; server charges apply.
- Relay room registration test supports host/join roles.
- Roblox Studio importer plugin can be installed from the RBXM Importer tab. It uses StudioService's local file picker and temporary asset IDs.
- The relay is currently a raw UDP room relay/probe, **not a complete transparent Roblox Studio port-forward/VPN**. Studio multiplayer traffic has not yet been wired through the relay.

The Studio importer plugin is embedded in the standalone executable so the EXE is self-contained.


## Host a local Roblox Studio client session
1. Close all running Roblox Studio windows before clicking Host Session (so the plugin can load).
2. In VZ NepTunnel, choose a local `.rbxl` or `.rbxlx` file and click **Host selected map in Studio**.
3. VZ installs `VZNepTunnelHost.lua` into `%LOCALAPPDATA%\\Roblox\\Plugins` and opens the selected local place using Studio's documented `--task EditFile --localPlaceFile` command-line options.
4. In Studio, click **Plugins > VZ NepTunnel > Start local client session**. The plugin calls `StudioTestService:ExecuteMultiplayerTestAsync(1, ...)`, which starts one local server and one playable client simulation. End it with Studio's End Session control.

This is a local Studio server/client test workflow. It does **not** yet tunnel the live Studio server traffic to remote friends over the Internet. The Hetzner UDP relay is only a separate relay/probe component at this stage.
