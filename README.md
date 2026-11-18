# VZ NepTunnel

Windows x64 desktop build of the VZ NepTunnel Python/CustomTkinter application.

## Build
GitHub Actions on branch `vz-neptunnel-build` builds with `windows-latest` and publishes `VZ-NepTunnel-Windows-x64` as a workflow artifact.

## Functionality status
This build verifies Python syntax, PyInstaller packaging, and the resulting Windows PE file. It does **not** certify live Roblox multiplayer networking. Portwarp API availability, the Portwarp CLI, Roblox Studio launch arguments, and the RBXM Studio plugin bridge require separate end-to-end tests with a real Portwarp account and Roblox Studio installation. The RBXM bridge is not implemented in this prototype.
