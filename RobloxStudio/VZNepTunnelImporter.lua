-- VZ NepTunnel Studio host plugin
-- Starts a real Studio server + client test for the currently open local place.
-- Install at %LOCALAPPDATA%\Roblox\Plugins\VZNepTunnelHost.lua
local StudioTestService = game:GetService("StudioTestService")

local toolbar = plugin:CreateToolbar("VZ NepTunnel")
local startButton = toolbar:CreateButton(
    "VZHostSession",
    "Start a local server and a playable client window for this place",
    ""
)
startButton.ClickableWhenViewportHidden = true

local running = false
startButton.Click:Connect(function()
    if running then
        warn("[VZ NepTunnel] A Studio test session is already running. Use Studio's End Session button to stop it.")
        return
    end

    running = true
    startButton.Enabled = false
    task.spawn(function()
        local ok, result = pcall(function()
            -- One local server and one client. Studio opens the client play window.
            return StudioTestService:ExecuteMultiplayerTestAsync(1, "VZ NepTunnel local host")
        end)

        if not ok then
            warn("[VZ NepTunnel] Could not start Studio client/server test: " .. tostring(result))
        else
            print("[VZ NepTunnel] Studio test session ended: " .. tostring(result))
        end

        running = false
        startButton.Enabled = true
    end)
end)

print("[VZ NepTunnel] Ready. Open the Plugins tab and click VZ NepTunnel > Start local client session.")
