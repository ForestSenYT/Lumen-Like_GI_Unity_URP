# LumenLike for Unity

LumenLike for Unity is an experimental open-source real-time GI project for URP. The repository is split into a core runtime tree, an isolated demo tree, and an explicit bundled third-party tree so the public surface area is easier to review.

## Current Status

- The active generated Unity project chain is clean when regenerated locally: `TAA.Runtime.csproj`, `Assembly-CSharp.csproj`, and `Assembly-CSharp-Editor.csproj` all build successfully.
- Core runtime, demo content, and bundled third-party code are separated structurally.
- The core GI runtime is currently kept on the last known-good rendering path; clean-room replacement work is still required before it should be treated as low-risk public runtime code.

## Repository Layout

- `Assets/LumenLike_Main`: primary LumenLike runtime code and runtime-owned shaders
- `Assets/Demo`: sample scene, demo-only scripts, project settings, and URP renderer assets
- `Assets/ThirdParty`: bundled third-party or upstream-derived modules retained with attribution

## Recommended Focus

Keep maintenance centered on these areas:

- `Assets/LumenLike_Main/Scripts/LumenLike.cs`
- `Assets/LumenLike_Main/Scripts/VolumetricLightedSEGI.cs`
- `Assets/ThirdParty/SEGI/Resources/SEGI.shader`
- `Assets/LumenLike_Main/Scripts/SurfaceCache/*`
- `Assets/Demo/Scenes/DemoScene.unity`

Treat demo assets and bundled third-party code as separate from the core runtime unless a change explicitly targets them.

## Known Cleanup Themes

- The core runtime still carries historical lineage risk in the two primary GI files.
- Some runtime behavior remains scene-coupled and should be treated as rewrite targets rather than incremental cleanup candidates.
- Demo project settings and renderer assets are isolated, but they still reflect the current runtime stack and should be treated as sample content.

## Third-Party Handling

- Bundled modules live under `Assets/ThirdParty` and keep their own local license or provenance files.
- `Assets/ThirdParty/RenderObjectsLumenLike` remains Unity Companion License-governed unless it is rewritten.
- Check `THIRD_PARTY_NOTICES.md` before redistributing a release package.

## Public Release Preparation

See:

- `OPEN_SOURCE_BLOCKERS.md`
- `OPEN_SOURCE_CLEANUP.md`
- `CONTRIBUTING.md`
- `THIRD_PARTY_NOTICES.md`

## License

The root repository is MIT licensed. Bundled third-party components and Unity-companion-derived code keep their own license context; check `THIRD_PARTY_NOTICES.md` and the license files inside retained third-party folders before redistributing a release package.