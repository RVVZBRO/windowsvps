-- VZ NepTunnel Studio testing plugin
-- This drives Roblox Studio's built-in local multiplayer test harness.
-- It does not create a public Roblox server or an Internet join endpoint.
local StudioTestService = game:GetService("StudioTestService")
local toolbar = plugin:CreateToolbar("VZ NepTunnel")
local running = false

local function addTestButton(clientCount)
    local button = toolbar:CreateButton(
        "VZTest" .. tostring(clientCount),
        "Start a local Studio server with " .. tostring(clientCount) .. " simulated client(s)",
        ""
    )
    button.ClickableWhenViewportHidden = true
    button.Click:Connect(function()
        if running then
            warn("[VZ NepTunnel] A Studio multiplayer test is already running. End that test before starting another.")
            return
        end
        running = true
        button.Enabled = false
        task.spawn(function()
            local ok, result = pcall(function()
                return StudioTestService:ExecuteMultiplayerTestAsync(clientCount, {
                    source = "VZ NepTunnel",
                    clients = clientCount,
                })
            end)
            if not ok then
                warn("[VZ NepTunnel] Studio test failed: " .. tostring(result))
            else
                print("[VZ NepTunnel] Studio test ended: " .. tostring(result))
            end
            running = false
            button.Enabled = true
        end)
    end)
end

addTestButton(1)
addTestButton(2)
addTestButton(4)
addTestButton(8)

print("[VZ NepTunnel] Ready. Use Plugins > VZ NepTunnel and choose 1, 2, 4, or 8 local simulated clients.")
