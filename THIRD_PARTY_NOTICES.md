# 第三方资源与素材来源

本仓库自有代码、脚本、文档和演示截图采用 [GPL-3.0-only](LICENSE)；下列第三方素材不因收录在仓库中而改用 GPL，仍按各自许可分发。

| 资源 | 路径/用途 | 来源与许可 | 本仓库修改 |
| --- | --- | --- | --- |
| Lowpoly Cat Rig + Run Animation | `models/lowpoly_cat_rig__run_animation.glb`；Unity `Assets/Resources/Characters/Cat/` 的猫模型及材质变体由此派生 | Daily Lowpoly，Sketchfab，[原模型](https://sketchfab.com/3d-models/lowpoly-cat-rig-run-animation-c36df576c9ae4ed28e89069b1a2f427a)，[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | 调整尺寸、骨骼动作、材质贴图、细节网格和 LOD，保留运行时 FBX 派生文件 |
| Black Rat (Free download) | `models/black_rat__free_download.glb`、`models/black-rat-free-download.zip`；目前未接入效果 | Nestaeric，Sketchfab，[原模型](https://sketchfab.com/3d-models/black-rat-free-download-3db3acb4140d4de8bd62a171212bad9c)，[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | 未在运行时使用；ZIP 包含原始模型及贴图 |
| MakeHuman 内置图形资产 | Unity `Assets/Resources/Characters/MakeHumanDefault/` 人物资产 | MakeHuman Community；内置图形素材为 [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)；详见 `unity/InteractiveWallpaper3D/Assets/ThirdParty/MakeHuman/LICENSE.md` | 参数化人物导出与 Unity 材质适配；未包含 MakeHuman 应用源码 |
| Blocky Characters 2.0 | Kenney 回退角色资源 | Kenney，[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)；详见 `unity/InteractiveWallpaper3D/Assets/ThirdParty/Kenney/BlockyCharacters/LICENSE.txt` | 作为早期/回退效果素材 |

## 用户提供的素材与来源记录

- 仓库所有者表示 `models/cat_rigged.fbx` 和喷嚏人物动作视频具备公开分发权限；其独立来源、生成视频平台及上游授权文本尚未在仓库记录。该确认不表示原始第三方权利人已将这些素材重新授权为 GPL。
- 已处理的喷嚏人物视频位于 Unity `Assets/StreamingAssets/CharacterVideo/Performances/SneezePickupRestore/`；仓库不再包含原始绿幕片段和关键帧。
- 若后续发现上游署名或再分发要求，应补全本文件，并在发布二进制及素材包时一并提供。
