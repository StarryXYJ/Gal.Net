# P3：图层指令与恢复

日期：2026-10-21
状态：verified。

## 交付

- `variants.show` / `variants.select` 与生成式 DTO、schema、参数业务校验加入既有 layers 模块，内置总数 44，不新建平行场景状态表。局部 schema 选择和完整执行覆盖仍可用。
- `LayerVariantsState` 保存请求意图与解析事实；实际图片仍由 Layer.AssetId 表示。切换保留 Layer 身份、布局、透明度和活动循环动画；普通来源替换清除图集绑定，hide/re-show 遵循既有实例生命周期。
- Normal/Quick 提交逻辑状态后通知普通 presenter，CatchUp 提交同样事实且不触发展示；实例非阻塞、不可跳过，batchId 保留。
- builtin.layers 状态节升至 2，旧 v1 明确拒绝，GameSnapshot 外层仍为 4。结构校验拒绝视频/颜色/Flipbook 冲突和坏差分子状态；模块预检重新解析并核对实际图片、变体键和原因，坏内容在 Engine 替换状态根前拒绝。
- 恢复阶段再次复核内容并重建普通图层；恢复后可继续选择，Reduced/Default 状态不会丢弃服装等未显示的选择。
- 默认 Avalonia 呈现、Avalonia Sample、Headless Sample、Editor Preview 已接入会话目录准备和注入；自定义宿主可用显式注册参数接入。

## 设计修订

现有 IGameView.ValidateState 是同步的读档预检，位于根替换之前。为保留这个边界，不扩展或同步阻塞异步恢复协议，定义改为会话准备时通过 `LoadImageVariantsCatalogAsync` 异步加载全部图集并校验 sprite 引用，释放句柄后返回不可变 ImageVariantsCatalog。show/select 只做同步查找/解析/状态提交，无指令内 I/O 或异步队列。

因此不存在旧定义加载晚于新 show 而覆盖新实例的回调；取消或资源失败发生在会话准备阶段，不返回半份目录，也不产生部分 Layer。目录按 scope 隔离，播放中采用准备时的内容快照。P1 匹配算法未变，未改 Engine 生产代码。

## 验证

相关七项目共 **1097 通过，0 失败，0 跳过**：

| 项目 | 通过 |
| --- | ---: |
| Core | 148 |
| Assets | 58 |
| Primitives.Builtins | 140 |
| Runtime | 96 |
| Presentation | 243 |
| Editor.Shared | 365 |
| Architecture | 47 |

本阶段新增 **27 项**，其中 Builtins 23、Assets 4。验证涵盖公开指令三模式、实例/选择隔离、变换与动画属性保留、真实活动循环实例、replace/show/showColor/hide、缺目标/坏输入/取消原子性、Reduced/Default 存读档与继续切换、保护集合持久化、旧状态版本、内容/实际结果/请求/保护/来源冲突在换根前拒绝、资源 ID 规则，以及预载目录的依赖校验、作用域隔离和取消。

完整解决方案在隔离目录 `artifacts/image-variants-p3/build` 构建通过，0 错误，保留 4 项既有 Avalonia AVLN3001 警告。19 份相关 C# 文件的 whitespace 检查通过；发现的测试对象初始化换行已修正并单独复验。未新增图形代码，现有 Presentation 全量回归验证底层 Avalonia presenter；真实图集素材的端到端 GUI 人工验收仍留 P4/P5，不以这些测试宣称已完成作者 UI。

主要验证方式：

```powershell
dotnet build GalNet.slnx --artifacts-path artifacts/image-variants-p3/build --verbosity quiet -m:1 -p:WarningLevel=0 -p:NuGetAudit=false --ignore-failed-sources
dotnet build GalNet.slnx --no-restore --artifacts-path artifacts/image-variants-p3/build --verbosity quiet -m:1 -p:WarningLevel=0
dotnet test test/GalNet.Primitives.Builtins.Tests/GalNet.Primitives.Builtins.Tests.csproj --no-restore --verbosity quiet -p:WarningLevel=0
dotnet test test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --no-restore --verbosity quiet -p:WarningLevel=0
```

其余五项目使用相同隔离构建产物，`dotnet test --no-build --no-restore --artifacts-path artifacts/image-variants-p3/build`；TRX、构建日志留在忽略的 artifacts/image-variants-p3。

验证基于当时工作区源码，其中并存其他任务的 Runtime/调试模块改动。本阶段不修改或提交这些文件，也不混入其他任务已暂存的解决方案/架构变更；提交使用显式路径范围。已有通用测试随该工作区验证，但不是本阶段新增交付。

## 文档和后续

已同步[资源规范](../../docs/spec/assets.md)、[指令规范](../../docs/spec/entry-types.md)、[运行时规范](../../docs/spec/runtime.md)和[新表现层接入手册](../../docs/manual/new-presentation-layer.md)。设计偏差、实际接口和状态版本已回写 design.md / phase-plan.md。

P4 继续图集配置/预览、动态选择控件与场景查询适配；P5 继续真实素材示例、发布运行和完整收尾。当前不提供图集维度动画轨道或专属切图淡化。
