# 阶段与证据

| Phase | 状态 | 退出条件 |
| --- | --- | --- |
| P1 按钮与 hover | verified（自动验证） | `TransparentHoverDoesNotCreateALightIntermediateSurface` 对照真实 ColorAnimator 的闪白结果，检查多个合成中间值及反向；四主题实际挂载透明图标按钮、焦点/选中反馈 |
| P2 常用字段与草稿 | verified（自动验证） | 继承生成 schema、手写 metadata、旧构造 ABI；实际挂载两处编辑、关闭重开、嵌套缺省对象、来源冲突、本地化与 IME 保存保护 |
| P3 Group 与 Inspector | verified（自动验证） | 完整/常用字段独立展示、Inspector 独立绑定；沿用重排、多选、撤销及旧绑定失效回归 |
| P4 保存与 Problems | verified（自动验证） | 当前未保存快照检查、稳定 ID 导航、重复跳转复用详情、混合草稿提交及发布中新编辑状态断言 |
| P5 布局与导航 | verified（自动验证） | 三套预设保留文档/选择/历史、最大化恢复、被裁剪容器重建、资源重开、Timeline 旧关键帧操作与实际小窗口挂载 |

## 回归命令和结果（2026-10-24）

统一测试参数：`dotnet test <csproj> --no-restore -c Release -m:1 -p:RunAnalyzers=false --verbosity minimal`。本机 testhost 需要沙箱外 IPC；沙箱内超时不作为业务测试失败。

| 测试项目 | 结果 | 日志 |
| --- | --- | --- |
| Core | 148 通过 | `artifacts-uiux-final-GalNet.Core.Tests.log` |
| Presentation | 243 通过；来源冲突修复后全量复跑仍为 243 通过 | `artifacts-uiux-presentation-verified.log` |
| Editor.Shared | 393 通过；新增发布状态断言另行通过 1 项 | `artifacts-uiux-final-GalNet.Editor.Shared.Tests.log`、`artifacts-uiux-recheck-GalNet.Editor.Shared.Tests.log` |
| Editor | 270 全量通过；此前新增测试漏装表单模板已修正，UIUX 10 项另行通过 | `artifacts-uiux-editor-verified.log`、`test/GalNet.Editor.Tests/TestResults/uiux-editor-verified.trx` |
| Architecture | 47 通过 | `artifacts-uiux-recheck-GalNet.Architecture.Tests.log` |
| Primitives.Builtins | 175 通过 | `artifacts-uiux-recheck-GalNet.Primitives.Builtins.Tests.log` |
| SchemaGenerator | 18 通过 | `artifacts-uiux-recheck-GalNet.SchemaGenerator.Tests.log` |

`scripts/test-contract-reproducibility.ps1 -Configuration Release` 通过：两次不同 SourceRevisionId 的 Sample 契约 SHA-256 同为 `9B97CA52F84880E640C7DAD5012C9DB9667C7887FD47959FDB1A07F1FF2CE535`。Editor 默认契约输出与打包目录 DLL 指纹一致。

`dotnet build GalNet.slnx --no-restore -c Release -m:1` 在本机 IPC 权限下通过，0 错误、14 个 NU1900 审计连接警告（`artifacts-uiux-solution-elevated.log`）。首次沙箱运行的 Android/Browser MSBuild 任务宿主连接失败；同一命令在本机构建权限下成功，未为绕过错误修改平台构建配置。

最终只读格式检查通过：对本 feature 的 59 个 C# 文件运行 `dotnet format whitespace GalNet.slnx --no-restore --include <files> --verify-no-changes` 及 `dotnet format style ... --verify-no-changes --diagnostics IDE0005 --severity info`，均 exit 0；`git diff --check` 通过。格式器提示工作区加载警告，未报告格式违规。

默认契约 SHA-256：`D932442825361BC17AE7BE3D292850A468C8DE2BAC704AFBA9159FB7DB58221D`，Editor.DefaultContracts 输出与 Editor `DefaultContracts/` 打包副本相同。

最后的来源冲突修复后，Editor 再次全量 270 通过（2m55s），Presentation 再次全量 243 通过。`UiuxNestedDraftCreatesAnOmittedObjectAndRejectsLaterSourceChanges` 覆盖来源值恢复相同 JSON、来源类型切换后恢复仍不得提交旧草稿。对应三个 C# 文件的追加只读空白检查通过（`artifacts-uiux-identity-format.log`）。七组相关回归共 1294 个测试通过，定向复跑不重复计数。

来源冲突修复后最终解决方案构建再次通过（`artifacts-uiux-solution-verified.log`）：0 错误、175 个警告，包含仓库现有 CA/nullable/XAML 分析器提示与 NU1900 审计连接警告。构建包含 Android 和 Browser 目标；未关闭分析器或通过裁掉平台规避构建。未将成功构建描述为无警告构建。

首次使用反馈后，P1 进一步收敛为单个绘制层的动画，共享 EditorMotion.HoverDuration；实际模板时钟验证四主题前 32ms 响应、反向连续和 checked/hover/pressed/disabled 优先级。最新 Editor 272 项通过，局部只读格式检查通过，详见 [动画统一证据](motion-unification.md)。此前两项交互反馈与 271 项回归见 [首次反馈](follow-up-20261024.md)。

## 窗口和证据边界

实际挂载 1200×800、960×640 和模拟 150% 缩放，并检查实际字段尺寸、透明按钮、资源/Graph/Timeline 控件。稳定截图位于忽略的 `artifacts/uiux/uiux-*.png`。截图捕获临时取消实例过渡以获得稳定端点；颜色中间值由独立过渡测试验证，不用稳定截图替代动画验证。

真实系统 DPI、原生 IME 候选窗口及完整桌面人工操作仍待人工验收。GUI 检查同步捕获当前版本，没有异步旧结果发布窗口。对象/列表草稿使用保守根来源签名，相关根值变化需要解决冲突。正式 spec/manual 已同步，详见 summary。
