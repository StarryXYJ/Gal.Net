# CI 与本地基线调查

## 远端证据

- GitHub run `40099479342`（2026-10-24，commit f32b683e）失败于 desktop-build-and-test 的 Editor/UI 测试；format 成功。
- 失败用例：UiuxVariableEditorsMountCompletionControlsAndSaveOtherValidDrafts；异常位于 Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync，270 项中 269 通过、1 失败。
- 当前源码已经使用 GetOrStartForAssembly 复用 session，不再在此用例逐次 DisposeAsync；该修正早于本 feature，不将其记作本次新增修复。
- `40098419620` 是此前成功的 CI run。用户观察到多数运行失败属实，但不是从未成功。
- gh run list 可读；gh 日志接口反复返回 EOF。使用 GitHub 连接器读取 jobs 与具体 job 日志获得上述证据。
- [远端失败记录](https://github.com/StarryXYJ/Gal.Net/actions/runs/40099479342)。本次工作未推送、未触发或重跑远端任务，不能宣称远端已绿。

## 本地发现

1. 普通沙箱中 dotnet format 的 build host 管道被拒绝；相同命令在允许本地 IPC 的执行环境可以运行。初次 testhost 连接超时单独复验通过，不通过加大超时或重试掩盖测试失败。
2. 当前源码存在空白与 using 顺序违规；使用原 CI 的 whitespace / IDE0005 规则修正，不关闭规则。
3. solution 未列 fixture，Release 构建实际将这些引用工程输出到 Debug，而测试读取 Release 残留。加入全部 fixture 后，ProjectContractPipelineTests 8 项通过。
4. 精简音轨/Bus JSON 的源生成反序列化丢失 init-only 属性默认值；新增回归先失败，显式 JsonConstructor 默认参数后通过，既有 Editor 音频草稿用例恢复。
5. UI 用例仍寻找已换成 EntryTypePicker 的旧 AddEntryCommand 按钮；改为验证同一工具栏现有 ExpandAll 图标按钮，保留尺寸、透明背景、键盘焦点等断言。场景选择器先 await PrepareAsync，再操作候选；三项失败用例一起复验通过。
6. 完整 coverage 运行进一步暴露共享 Headless 指针命中的顺序依赖；场景选择器用例改用实际 Flyout.ShowAt 打开，仍验证候选、提交和 Undo。随后整套 Editor 342 项通过。
7. GameTestCase ending 的 shake/impact 仍使用旧 targets 数组；迁移为当前 handleId，并同步两种语言的展示说明。集成测试 130 项通过。

最终 P1：`out/ci/local-pass/ci-results.json` 为 passed=true，15 个测试项目合计 2011 passed / 0 failed，包含 coverage；没有禁用用例或放宽产品校验。发布保护用例 6 项通过。远端尚未推送或执行新的 workflow。

## AOT 实际限制

真实桌面 Sample 在独立 artifacts 目录发布，最终 ILC 将约 60 条裁剪/AOT 分析诊断视为错误。涉及源生成 JSON 缺口、反射绑定、动态主题资源、DI 类型保留，以及 Serilog / Ursa / Ursa.Themes.Semi。没有压制这些诊断，也没有产出标记为支持 AOT 的发行 ZIP。

`Test-DesktopAot.ps1` 保留可复现的严格试编译；Release.ps1 对 Aot/Both 明确失败，不回退 R2R。桌面 AOT 仍是未完成阶段，需要单独完成闭包迁移、依赖处理和真实 UI/音视频验收。
