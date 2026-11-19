-- VZ NepTunnel: official Roblox Studio local multiplayer test plugin.
-- StudioTestService is a Studio-only testing API; it does not expose an Internet server.
local StudioTestService = game:GetService("StudioTestService")
local toolbar = plugin:CreateToolbar("VZ NepTunnel")
local active = false

local function createTestButton(clientCount)
    local button = toolbar:CreateButton(
        "VZTest" .. tostring(clientCount),
        "Run a local Studio server with " .. tostring(clientCount) .. " simulated client(s)",
        ""
    )
    button.ClickableWhenViewportHidden = true
    button.Click:Connect(function()
        if active then
            warn("[VZ NepTunnel] A multiplayer test is already running. End it in Studio before starting another.")
            return
        end
        active = true
        button.Enabled = false
        task.spawn(function()
            local ok, result = pcall(function()
                return StudioTestService:ExecuteMultiplayerTestAsync(clientCount, "VZ NepTunnel")
            end)
            if ok then
                print("[VZ NepTunnel] Local multiplayer test finished: " .. tostring(result))
            else
                warn("[VZ NepTunnel] Could not start/complete Studio test: " .. tostring(result))
            end
            active = false
            button.Enabled = true
        end)
    end)
end

createTestButton(1)
createTestButton(2)
createTestButton(4)
createTestButton(8)
print("[VZ NepTunnel] Ready. Open Plugins > VZ NepTunnel and choose VZTest1, VZTest2, VZTest4, or VZTest8.")
