# F-20260929-09 实现总结

## 实际实现

- 将 `GeneralTest` 与原 Assets 混合测试拆分为 Architecture、Core、Runtime、Builtins、Assets、Storage.FileSystem、Editor.Shared、Editor、Presentation 和 Integration 十个测试项目。
- 通过 `test/Directory.Build.props` 共享 NUnit、test SDK、analyzers 与 coverage 配置；各项目仅声明必要的生产依赖。
- 删除 `GeneralTest` 项目，将跨 Assets/Storage/Editor/Runtime/Sample 的场景归入 `GalNet.IntegrationTests`。
- CI 对所有测试项目执行 locked restore，分组执行测试，并为每个项目保留独立 TRX 和 coverage 产物。

## 验证证据

- Architecture 14、Core 55、Runtime 32、Builtins 40、Assets 30、Storage 12、Editor.Shared 16、Editor 18、Presentation 65、Integration 29，合计 311/311 通过。
- `GalNet.Core.Tests` 仅引用 Core；`GalNet.Assets.Tests` 仅引用 Core 与 Assets；Editor.Shared 测试不编译完整 Editor UI。
- `GalNet.Editor.Headless`、`GalNet.Sample.Headless` 和 `GalNet.slnx` Release 构建通过；完整 solution 仅保留既有 Browser WebAssembly native reference 警告。

## 计划偏差

- 未新增测试 category：项目边界已能稳定表达运行分组，此时再在测试方法上重复标注会增加维护成本。
- CI 继续上传各项目的原始 coverage 产物，没有引入新的合并或阈值工具，符合本 feature 的非目标。

## 后续工作

- 维护性路线图下一阶段是 Phase 4a：在已建立的 Editor.Shared 与 Editor 测试边界上拆分 Editor 职责。
