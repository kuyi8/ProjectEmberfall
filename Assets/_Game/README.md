# `_Game` asset boundary

All project-owned runtime assets live under this folder. Third-party packages remain isolated in `Assets/ThirdParty` and are adapted through Prefab Variants or copied project-owned materials.

```text
_Game/
  Art/             Project-owned or adapted visual assets
  Audio/           Mixers, project-owned clips, and audio settings
  Data/            ScriptableObject registries and built-in JSON/Lua content
  Prefabs/         Project-owned gameplay and presentation prefabs
  Scenes/          Numbered production and development scenes
  Settings/        URP, input, quality, and other project settings assets
  Scripts/         Runtime and editor code grouped by assembly boundary
  Tests/           EditMode and PlayMode tests
```

Rules:

- Static definitions are immutable at runtime; mutable state belongs to runtime objects.
- Every content category has one source of truth.
- Scene/prefab code depends on module APIs, not on third-party asset folder layouts.
- Development-only assets use the `90_`/`91_` scene prefix and do not enter Release Build Settings.

