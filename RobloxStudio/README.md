# VZ NepTunnel Studio importer

Copy `VZNepTunnelImporter.lua` to:

`%LOCALAPPDATA%\Roblox\Plugins\VZNepTunnelImporter.lua`

Restart Roblox Studio. The Plugins ribbon will show **VZ NepTunnel > Import a local RBXM/RBXMX file**. The plugin uses StudioService:PromptImportFileAsync and File:GetTemporaryId, then game:GetObjects to insert the selected model into Workspace. Studio API reference: https://create.roblox.com/docs/reference/engine/classes/StudioService and https://create.roblox.com/docs/reference/engine/classes/File.

The app can install this plugin automatically in the next integrated build.
