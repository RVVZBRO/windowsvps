-- VZ NepTunnel Studio host plugin
-- Install at %LOCALAPPDATA%\Roblox\Plugins\VZNepTunnelHost.lua
local StudioTestService = game:GetService("StudioTestService")
local toolbar = plugin:CreateToolbar("VZ NepTunnel")
local startButton = toolbar:CreateButton("VZHostSession", "Start a local server and playable client window for this place", "")
startButton.ClickableWhenViewportHidden = true

local running = false
startButton.Click:Connect(function()
    if running then
        warn("[VZ NepTunnel] A Studio multiplayer test is already running. Use Studio's End Session button to stop it.")
        return
    end
    running = true
    task.spawn(function()
        local ok, result = pcall(function()
            -- Creates one Studio server and one playable client DataModel/window.
            return StudioTestService:ExecuteMultiplayerTestAsync(1, "VZ NepTunnel local host")
        end)
        if not ok then
            warn("[VZ NepTunnel] Could not start Studio client/server test: " .. tostring(result))
        else
            print("[VZ NepTunnel] Studio test session ended: " .. tostring(result))
        end
        running = false
    end)
end)

print("[VZ NepTunnel] Ready. Open Plugins > VZ NepTunnel > Start local client session.")
