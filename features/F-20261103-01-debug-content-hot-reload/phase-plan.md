# 开发调试内容热更新阶段计划

状态：verified。设计依据见 [design.md](design.md)。

## P1 — 完整候选与纯分类（verified）

- 发布内部不可变调试构建目录；清单记录内容/契约/作者身份、作者 Entry span 与非 Entry 摘要。
- 分类器按稳定作者边界返回 `Direct`、`ReturnGroupStart` 或 `RestartGame`。
- 非 Entry 内容变化统一保守重开，不区分拓扑种类或分析资源使用位置。

证据：`GalNet.Debugging.Tests` 19/19；相关构建集成测试通过。

历史提交：`d8fb5675`；简化修订：`c049ebce`。

## P2 — 运行事实与原子替换（verified）

- Snapshot v6 保存自然经过的 Group ID 序列；inspect 报告运行位置、活动 primitive 和序列。
- Runtime 在执行门内整体替换完整 Graph，并使用 Editor 映射的候选边界。
- 继续复用已有 Group 入口缓存和 generation 失效机制；不保存候选、影响等级或环标志。

证据：`GalNet.Runtime.Tests` 128/128；相关集成测试通过。

历史提交：`cfed4cc6`；简化修订：`c049ebce`。

## P3 — 客户端候选服务与协议（verified）

- 客户端只持有当前运行候选与最新候选两个对象引用。
- 协议提供 install、activate、activate-and-seek；不传或回报内容版本号。
- 新 Run 选择最新候选，未激活旧候选及时释放，已使用 scope 随服务结束释放。

证据：Debugging 19/19、Runtime 128/128、定向集成 30/30。

历史提交：`0b7e0c66`；简化修订：`c049ebce`。

## P4 — Editor 协调、提示与闭环（verified）

- 唯一 Editor 服务保存 applied/pending build，负责构建、安装、分类和明确命令。
- Direct 自动替换；Return/Restart 在调试面板显示唯一对应动作。
- 顶部菜单只保留“应用内容修改”；Toast 由 Editor Panel 在命令完成后显示。
- 只有命令成功才切换源码映射；失败保持旧运行和旧映射。

证据：真实 loopback 挂载 UI 测试覆盖 Direct、ReturnGroupStart、RestartGame pending，1/1 通过；Editor 构建通过。

提交：`4f825460`。

## P5 — 文档与收尾（verified）

- 已同步 feature 设计、Runtime/Editor spec 与 Editor 操作手册。
- 审查中指出的内容版本状态机、主动对账、原因数组、环状态、重复入口布尔、请求序号、Notice 副本、额外菜单、资源依赖分析与退休上限均已删除或取消。

未专项验证：真实 Developer Sample 窗口中的媒体资源观感和移动平台。资源/I18n/settings 变化当前保守要求重新开始游戏，不声称运行中原地替换已持有对象。
