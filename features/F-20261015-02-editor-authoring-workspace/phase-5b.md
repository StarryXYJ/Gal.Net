# P5b：Entry 多选与剪贴板

日期：2026-10-17。状态：verified。P5 整体仍 in-progress。

## 实现与边界

- 原生 Ctrl/Shift 多选、活动项与选中集合分离；按列表顺序复制、剪切、粘贴、创建副本与批量删除。右键菜单、Ctrl+C/X/V/D/A、Delete/Escape 保留文字/数字/条件输入的本地处理。多选拖动保持相对顺序及折叠状态；行内删除只删除当前行。
- 项目级 GroupEntryAuthoringService 以 Group ID/项目投影执行批量意图，历史不持有页面。每批一条历史，删除 Undo 恢复原位置，粘贴 Redo 复用新身份；关闭页面后仍可按项目顺序跨 Group 撤销。当前其他字段与添加入口仍沿用已有投影/绑定路径，变量 UI 和剩余作者绑定迁移未完成。
- 独立 GalNet.Entries / Version 1 JSON 信封，最多 1,000 条/16 MiB UTF-8。目标缺类型或字段整批拒绝；作者 JSON、条件、省略/显式 null 原样复制，普通字符串不猜测为引用。完整编译校验不挪到剪贴板入口，非法值保存仍沿用原校验；未知 schema 通用编辑不在本切片。
- 剪切在系统剪贴板写入成功后才删除，并核对等待期间源内容未变。异步粘贴捕获稳定锚点，锚点删除时拒绝；页面销毁或换绑后忽略晚结果。新错误和历史标题接入四语，已显示错误随语言切换更新。
- 可观察集合在通知抛错时本步可能已生效；批次按实际集合恢复。恢复失败抛 HistoryRepairRequiredException，项目/历史进入 RepairRequired。见 L-20261017-07-observable-batch-compensation。

## 验证

最终 validate-p5b.ps1：dotnet build GalNet.slnx --no-restore --artifacts-path test/GalNet.Editor.Tests/bin/p4b-artifacts -m:1 -p:UseSharedCompilation=false，通过，0 错误、48 条其他文件的编译/分析器警告。Editor 147、Shared 191、Architecture 45、Integration 110，共 493 项通过、0 失败、0 未执行。自有 C# 的 whitespace 与 IDE0005 --verify-no-changes 通过，前后 SHA256 一致。

证据目录：C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/group-ui-validation。含 validate-p5b.ps1、p5b-solution-build.log、p5b-*-final.trx、p5b-source-sha256.json 和 group-multiselect.png。截图已检查，选择、活动项、菜单挂载、输入让路及拖放另有真实主题/视觉树的原生鼠标或路由按键断言。系统剪贴板成功路径使用 Headless 平台剪贴板，失败/延迟通过视图的平台适配入口注入；未宣称完成 Windows 原生剪贴板争用的人工验收。

故障测试 EntryBatchFailuresRestoreContentAndHistoryOrRequireRepair 覆盖新增/删除/移动中途失败、失败 Undo/Redo 的游标及恢复再次失败；p5b-failure.trx 独立 1/1，最终完整 Editor 再次覆盖。跨 Group 深复制、省略/允许 null/复杂 JSON、单批 Undo/Redo 身份、页面释放、保存重开、非法 payload、上下移动、剪切失败/源变化/晚返回和四语动态错误均通过。

## 迭代与自查

首次编译的菜单 ListBoxItem/ListBox 空合并改为显式 Control。首次聚焦 2/7：测试误用只供自定义 schema 的 Contracts() fixture，无法创建 Wait；改用 DefaultContracts 后 6/7。剩余失败是以不允许 null 的 duration 做保存断言，改为省略 duration，并在允许 null 的 effect.instanceId 验证显式 null；没有放宽产品校验。修正后聚焦 7/7，第一次完整 Editor 146/146；补充集合补偿故障验证后最终 147/147。

复核了全批预检、先写后删、稳定锚点/新身份、项目历史不引用页面、通知失败补偿与四语生命周期。仅推进本切片，不将 P5 或 P6 宣称完成。构建使用当前源码工作树，隔离目录只隔离输出；另一任务 GameTestCase/shader/ComplexPrimitiveOptions/PlayerInputTests/entry-types/runtime 改动保留并排除本阶段提交。

当前正式行为见 [Group 规范](../../docs/spec/editor/groups.md)；下一步为变量编辑及剩余作者绑定迁移，状态查询和专业动画工具继续后续安排。
