-- VZ NepTunnel RBXM importer plugin
-- Install this file in %LOCALAPPDATA%\Roblox\Plugins and restart Studio.
local StudioService = game:GetService("StudioService")
local Selection = game:GetService("Selection")
local toolbar = plugin:CreateToolbar("VZ NepTunnel")
local button = toolbar:CreateButton("VZImportRBXM", "Import a local RBXM/RBXMX file into the open place", "")
button.ClickableWhenViewportHidden = true

button.Click:Connect(function()
	local ok, file = pcall(function()
		return StudioService:PromptImportFileAsync({"rbxm", "rbxmx"})
	end)
	if not ok then warn("[VZ NepTunnel] File picker failed: " .. tostring(file)); return end
	if not file then return end
	local success, objects = pcall(function()
		return game:GetObjects(file:GetTemporaryId())
	end)
	if not success or not objects or #objects == 0 then
		warn("[VZ NepTunnel] Studio could not decode this model file. Try Explorer > Insert from File.")
		return
	end
	local parent = game:GetService("Workspace")
	for _, object in ipairs(objects) do object.Parent = parent end
	Selection:Set(objects)
	print("[VZ NepTunnel] Imported " .. file.Name .. " into Workspace.")
end)
