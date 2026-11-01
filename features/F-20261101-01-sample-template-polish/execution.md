# 实现与验证记录

## P1：启动协议与视觉策略

状态：verified，2026-11-01。用户授权实现后，因需要公开便利入口和属性，按已约定边界转普通 feature 流程；没有新增接口体系或扩大到拆包。

GameLaunchFlow 增加新游戏、继续、读档及带变量启动的便利方法，集中适配普通/两阶段会话；原构造及 RunAsync 保留。项目 VM 保留命令与业务、导航，移除重复 Prepare/Begin 判断。RunAsync 不再固定最短展示时间，明确检查取消后禁止切入游戏/Begin，并继续等待准备清理。

SampleCustomization 定义 350ms 加载停留与 2.8 秒公告；加载回调异步等待，负值明确拒绝。PlayerSurface 新增正时长 StyledProperty，通用默认 3 秒，Sample 显式覆盖；确认、输入、焦点和公告清理机制保留。

证据根 `.artifacts/sample-template-polish/p1/`：

- presentation.trx：234 total / 228 executed / 228 passed / 0 failed，6 个显式用例未执行。新增 7 种会话动作/能力组合、参数化不支持提前失败、准备忽略取消令牌时仍不开始播放；原顺序和取消收束回归继续通过。
- integration.trx：335 total / 323 executed / 322 passed / 1 failed，12 项显式未执行。唯一失败为前项已记录的 Editor 保存后 dirty/关闭取消，不作为本 feature 已修复或新增失败。
- targeted.trx：最终页面/宿主/扩展定向 39/39，包括实际 Toast 使用 50ms 项目时长提前关闭、非正时长拒绝、项目页启动及确认/焦点/输入恢复。
- architecture.trx：56/56。docs check 72 页及本地链接通过，diff 空白检查通过。首次新增测试误用了无参 AssertionException 导致编译失败，已修正并完成上述重跑，初始日志保留。

工作区有并行开场序列/启动可用性改动，涉及同一 TitlePageViewModel 与 SampleCustomization。只提交本 feature 的启动协议/时长改动，保留其余工作树内容；不将并行改动或旧 review、用户 lesson 混入阶段提交。
