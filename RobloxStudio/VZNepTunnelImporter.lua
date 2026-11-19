-- VZ NepTunnel: Roblox Studio local multiplayer test plugin.
-- StudioTestService is Studio-only and does not create a public Internet server.
local StudioTestService = game:GetService("StudioTestService")
local toolbar = plugin:CreateToolbar("VZ NepTunnel")
local active = false

local function addButton(clientCount)
    local button = toolbar:CreateButton(
        "VZTest" .. tostring(clientCount),
        "Start a local Studio server with " .. tostring(clientCount) .. " simulated clients",
        ""
    )
    button.ClickableWhenViewportHidden = true
    button.Click:Connect(function()
        if active then
            warn("[VZ NepTunnel] A Studio test is already running. End it before starting another.")
            return
        end
        active = true
        button.Enabled = false
        task.spawn(function()
            local ok, result = pcall(function()
                return StudioTestService:ExecuteMultiplayerTestAsync(clientCount, "VZ NepTunnel")
            end)
            if ok then
                print("[VZ NepTunnel] Studio multiplayer test ended: " .. tostring(result))
            else
                warn("[VZ NepTunnel] Studio test failed: " .. tostring(result))
            end
            active = false
            button.Enabled = true
        end)
    end)
end

addButton(1)
addButton(2)
addButton(4)
addButton(8)
print("[VZ NepTunnel] Ready: Plugins > VZ NepTunnel > VZTest1 / 2 / 4 / 8.")
