-- Trusted, declarative quest condition. It can only read the copied Quest table.
local quest = Emberfall.Quest
local ready = quest.forestEnemiesDefeated == true and quest.sealActivated ~= true

if ready then
    return {
        eligible = true,
        reasonId = "text:quest.forest-seal.ready"
    }
end

return {
    eligible = false,
    reasonId = "text:quest.forest-seal.blocked"
}
