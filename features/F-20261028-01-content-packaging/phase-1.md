# P1 — Metadata 依赖和 PAK 用途

状态：`verified`，2026-10-28。

## 实现

- Core 的 `IAssetDependencies` / `DependentAssetMeta` 暴露直接引用及完整性；资源目录通过 `WithoutDependencies` 明确叶类型，未声明的 data 仍为未知依赖。
- 图集编译在同一快照填充全部 sprite 引用，保留未知 metadata 字段，不回写源 sidecar。
- `AssetDependencyCollector` 提供类型检查、缺失诊断、取消、循环处理、显式动态完整性声明和 graph→group 的可选剧情包边界；输出稳定排序的闭包，不决定包归属。
- GPAK v3 使用独立控制描述区分 base/story/override，校验用途字段组合、分片序号、映射目标及映射循环。旧调用默认写匿名 base，旧 v2 明确拒绝。
- 归档按稳定 ID 排序，按实际数据重算 SHA-256；读取校验索引身份、重复 ID/路径、数据范围和尾随数据，获取资源时校验实际 hash/长度。已知编译图集对照正文检查依赖 metadata。
- 三种用途目前是格式协议，实际分包导出、story 缺席跳过和 override 映射将在 P3/P4 接入；当前并未保证未引用素材已从现有导出中剔除。

## 验证证据

所有构建使用 SDK 10.0.401，先确认 build 完成，再启动 no-build 测试。

| 验证 | 结果 |
| --- | --- |
| `dotnet build GalNet.slnx -c Release --no-restore --nologo -m:1 -nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly` | 通过，250 warning / 0 error |
| `dotnet test test/GalNet.Assets.Tests/GalNet.Assets.Tests.csproj -c Release --no-build --no-restore` | 89/89 通过，包括新增 13 个用途/依赖契约用例 |
| Editor.Shared 测试筛选 `ImageVariantsResourceTests` / `ExportPreparationTests` | 12/12 通过 |
| `verification/ReflectionOff.csproj` Release 构建及直接执行 | 输出 `P1_REFLECTION_OFF_OK kinds=3 dependencies=2 reflectionJson=false` |
| `git diff --check` | 通过 |

反射关闭验证程序包含断言，必须实际关闭 reflection JSON，检查本地编译、图集依赖闭包、三种用途 metadata/控制描述往返。复跑：

```powershell
dotnet restore features/F-20261028-01-content-packaging/verification/ReflectionOff.csproj --locked-mode
dotnet build features/F-20261028-01-content-packaging/verification/ReflectionOff.csproj -c Release --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet features/F-20261028-01-content-packaging/verification/bin/Release/net10.0/ReflectionOff.dll
```

## 验证中的修正与限制

- 两个旧图集负例手动构造普通 AssetMeta；迁移为有效依赖 metadata 后继续检查它们原本的缺 sprite/错误类型，不删除原负例。
- 对 PAK 控制 JSON 的重复属性/未知字段，以及大小写不敏感的映射循环均有校验；图集依赖与正文不一致在写入和读取时拒绝。
- 受限进程环境曾出现构建失败但无错误、测试宿主无结果；终止这些无结果运行后，在正常进程环境构建/测试得到上述明确通过结果。未把无结果尝试当成成功，也未据此认定业务缺陷。
- 原有 release 脚本、发布文档及其他 feature 的并行改动不属于本提交。

正式规范已同步到 `docs/spec/assets.md`、`docs/spec/file-formats.md`；未提前宣称 Graph 已资源化或补丁挂载行为已实现。
