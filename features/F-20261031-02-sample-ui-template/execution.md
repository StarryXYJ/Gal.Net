# 实施记录

## P1：基线与修订设计

提交：`65bf293d`。消费者、所有权、测试及未验证边界见 [baseline.md](baseline.md)。本阶段修正原生测试夹具遗漏的资源文案，不改变产品行为。

## P2：共享呈现状态

提取 `GamePresentationState` 及 Playback/History partial，持有原场景、对白/NVL/选项、等待、播放与历史交互机制。GamePage、各 presenter、视频、输入、GameRun/Presentation/Session、设置和调试保存接线使用该对象；音频公告也绑定该状态。

默认 GamePageViewModel 暂仅提供存读档、设置及返回页面的命令，继承状态并通过 Scoped DI 别名与运行链路共享唯一实例，不复制状态和转发全部属性。状态暂继承仅含 ObservableObject 的 PageViewModelBase，使旧导航页面继续可运行；P3/P4 将游戏页改为组合状态，P5 删除此过渡继承及旧页面适配，不作为最终公开应用模型。

GameActionDispatcher/PlayerInputRouter 使用活动呈现状态；应用页面动作以单个 `Func<string, Task>` 回调交回当前 shell，设备输入不再引用具体页面 VM。取消播放、首空槽保存及其活动状态检查仍在内置机制执行。

验证：

- `.artifacts/sample-ui-template/p2/presentation-final.trx`：367 total / 361 executed / 361 passed / 0 failed，6 项显式用例未执行，与基线的未执行范围一致。新增 3 项：作用域状态身份、无页面/导航的选项取消后恢复、应用动作回调与交互阻塞。
- `.artifacts/sample-ui-template/p2/integration.trx`：80 executed / 79 passed / 1 failed。失败名称及错误与 P1 相同：Editor 保存后的 IsDirty=true，随后 CloseRequestedAsync 取消。没有新增 Sample 集成失败。
- 受影响构建通过，现有场景鉴赏、模态输入、重挂载、存档与清理测试随上述回归执行。
- 修改/新增 C# 文件的 `dotnet format whitespace --folder --verify-no-changes --include ...` 通过；`git diff --check` 通过。solution 模式格式检查因受限命名管道无法连接 BuildHost，改用不加载项目的文件夹模式完成同一批文件的空白检查。

原生窗口关闭、真实窗口存读档和其他平台验收仍沿用 P1 缺口，不从本阶段 headless 结果推断通过。主题/普通页面仍为迁移前结构，尚未完成用户目标；后续按 P3–P6 继续，不把这次状态提取描述为 UI 模板改造完成。
