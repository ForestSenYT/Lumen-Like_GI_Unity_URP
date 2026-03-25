# RenderObjectsLumenLike Provenance

This folder is not clean-room original code.

## Upstream Basis

The local files derive from the URP package sources shipped in this project under:

- `Library/PackageCache/com.unity.render-pipelines.universal@37e0d4fc2503/Runtime/RendererFeatures/RenderObjects.cs`
- `Library/PackageCache/com.unity.render-pipelines.universal@37e0d4fc2503/Runtime/Passes/RenderObjectsPass.cs`

The corresponding local files are:

- `Assets/ThirdParty/RenderObjectsLumenLike/RenderObjectsLumenLike.cs`
- `Assets/ThirdParty/RenderObjectsLumenLike/RenderObjectsPassLumenLike.cs`

## Local Adaptations

The local copies rename the feature into the `LumenLike` namespace and add project-specific behavior, including:

- cascade-related fields and settings
- override shader fallback through the main `LumenLike` component
- custom render-graph helper code paths
- camera matrix override helpers and related compatibility edits

## Release Guidance

If this folder remains in the public repository, treat it as bundled Unity URP-derived source, not original LumenLike runtime code.

Before release:

- keep explicit attribution to the upstream URP package sources
- carry the Unity Companion License context alongside this folder, including `UNITY_COMPANION_LICENSE.md`
- rewrite this folder instead if the goal is a purely MIT-core repository with no Unity-companion-derived source files