# Third-Party Notices

LumenLike for Unity retains several bundled or adapted third-party modules inside `Assets/ThirdParty`. These folders are outside the root repository MIT license unless their local licenses explicitly allow redistribution under their own terms.

## Bundled Components

- `Assets/ThirdParty/SEGI`
  - License context: MIT.
  - Local files: `Assets/ThirdParty/SEGI/LICENSE.txt`, `Assets/ThirdParty/SEGI/README.md`.
  - Role: bundled SEGI-derived runtime and shader resources.
- `Assets/ThirdParty/CommonTools/ScreenSpaceReflectionsURP`
  - License context: local MIT license file.
  - Local files: `Assets/ThirdParty/CommonTools/ScreenSpaceReflectionsURP/LICENSE.txt`.
  - Role: retained third-party screen-space reflection support code.
- `Assets/ThirdParty/CommonTools/GBufferExtract`
  - License context: MIT.
  - Local files: `Assets/ThirdParty/CommonTools/GBufferExtract/LICENSE MIT/LICENSE.txt`, `Assets/ThirdParty/CommonTools/GBufferExtract/LICENSE MIT/README.md.txt`.
  - Role: bundled G-buffer extraction utilities.
- `Assets/ThirdParty/TemporalAA/TEMPORAL 2`
  - License context: MIT.
  - Local files: `Assets/ThirdParty/TemporalAA/TEMPORAL 2/LICENSE.txt`, `Assets/ThirdParty/TemporalAA/TEMPORAL 2/README.md`.
  - Role: legacy TAA package retained as bundled third-party code.
- `Assets/ThirdParty/TemporalAA/Runtime`
  - License context: MIT under the root `Assets/ThirdParty/TemporalAA/LICENSE.txt`.
  - Local files: `Assets/ThirdParty/TemporalAA/LICENSE.txt`.
  - Role: bundled `Naiwen.TAA` runtime assembly.
- `Assets/ThirdParty/CavityFX`
  - License context: local license and readme files present.
  - Local files: `Assets/ThirdParty/CavityFX/LICENSE.txt`, `Assets/ThirdParty/CavityFX/README.md.txt`.
  - Role: bundled cavity-effect support code and shaders.
- `Assets/ThirdParty/URPVolumetricShafts`
  - License context: Apache 2.0.
  - Local files: `Assets/ThirdParty/URPVolumetricShafts/LICENSE.txt`, `Assets/ThirdParty/URPVolumetricShafts/README.md`.
  - Role: bundled volumetric shafts resources and helper scripts.
- `Assets/ThirdParty/RenderObjectsLumenLike`
  - License context: Unity Companion License-governed adapted URP source.
  - Local files: `Assets/ThirdParty/RenderObjectsLumenLike/PROVENANCE.md`, `Assets/ThirdParty/RenderObjectsLumenLike/UNITY_COMPANION_LICENSE.md`.
  - Role: adapted URP `RenderObjects` sources with LumenLike-specific behavior.

## Contributor Rules

- Keep each bundled folder's local license, readme, and provenance files with that folder.
- Do not move bundled code into `Assets/LumenLike_Main` without updating provenance and notices in the same change.
- Do not delete attribution files just because a bundled folder is currently inactive.
- If a bundled dependency is removed from the repository, update this file in the same change.
- If `RenderObjectsLumenLike` remains in the repo, keep its provenance note and Unity Companion License context with it.

## Release Checklist

Before a public release:

- verify each bundled module's upstream source and local license files
- confirm which bundled folders are required runtime dependencies versus optional sample support
- keep exact attribution text where required by the local license files
- keep Unity Companion License-governed code clearly separated from the MIT-core runtime
- remove unused archives, backups, and abandoned bundled folders from `Assets/ThirdParty`