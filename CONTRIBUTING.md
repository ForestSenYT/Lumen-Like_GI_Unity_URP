# Contributing

Thank you for contributing to LumenLike for Unity.

## Project Scope

This repository is maintained as a focused URP real-time GI project. Contributions should improve the supported runtime path, reduce maintenance cost, or make the project easier to understand and validate.

## Supported Areas

Priority work should stay close to the main path:

- `Assets/LumenLike_Main/Scripts/LumenLike.cs`
- `Assets/LumenLike_Main/Scripts/VolumetricLightedSEGI.cs`
- `Assets/ThirdParty/SEGI/Resources/*`
- `Assets/LumenLike_Main/Scripts/SurfaceCache/*`
- `Assets/Demo/Scenes/DemoScene.unity`

## Change Guidelines

- Prefer targeted fixes over broad renderer-asset churn.
- Keep user-facing settings clean and product-oriented.
- Keep bundled third-party code isolated and clearly attributed.
- Do not add new renderer variants unless they replace an older supported path.
- Avoid adding new demo scenes, backup archives, or large binary assets to the main repository.

## Validation Expectations

Before opening a pull request:

- Build `Assembly-CSharp.csproj`.
- Note any Unity-only validation that still needs to be performed manually.
- State whether the change affects runtime, editor tooling, sample content, or bundled third-party integrations.
- If shader or renderer behavior changed, verify the demo scene path in addition to compile health.

## Repository Hygiene

Do not commit:

- `Library`
- `Logs`
- `Temp`
- `UserSettings`
- generated `.csproj` / `.sln` files
- local editor caches

## Licensing

Do not remove third-party license or notice files when cleaning up the repository. If a bundled dependency is removed or moved, keep its attribution trail intact until the repository notices are updated as well.