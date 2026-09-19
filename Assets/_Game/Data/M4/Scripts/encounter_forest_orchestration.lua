-- Trusted, declarative encounter decision. C# validates the returned action and IDs.
local encounter = Emberfall.Encounter
local world = Emberfall.World
local presentation = Emberfall.Presentation

if encounter.defeatedCount == 0 and encounter.playerCount >= 1 and
   world.gateOpen == false and presentation.allowCue == true then
    return {
        action = "queue_wave",
        waveId = "wave:forest.guard-pair",
        presentationId = "presentation:forest.encounter-start"
    }
end

return {
    action = "none"
}
