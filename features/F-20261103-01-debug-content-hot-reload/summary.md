# 开发调试内容热更新实现总结

状态：verified
日期：2026-11-03

## 结果

Editor 启动的 Developer 调试 App 现可接收完整内容候选，并由 Editor 按当前运行位置决定直接应用、返回当前 Group 开头应用或重新开始游戏。客户端不决定产品行为，也不显示提示。

最终实现没有内容版本号或跨进程版本对账：Editor 只保存当前已应用构建与待处理构建；客户端只保存当前运行候选与最新候选两个对象引用。既有 run ID、runtime generation 和 Group/index/活动 primitive 前置条件保证替换发生在已检查边界。

## 修改模块

- `GalNet.Debugging`：候选清单、纯影响分类、install/activate/activate-and-seek 协议。
- `GalNet.Runtime`：Snapshot v6 的自然经过 Group ID 序列，以及完整 Graph 原子替换。
- `GalNet.Avalonia.Debugging`：验证并拥有完整候选，激活最新候选，新 Run 选择最新内容。
- `GalNet.Editor`：构建/安装/分类/激活协调服务、调试面板待处理动作、Toast 和单一顶部菜单入口。

## 行为边界

- 未经过 Group、当前 Group 未执行作者 Entry：直接替换。
- 当前 Entry 或当前 Group 已执行前缀：提示返回 Group 开头应用。
- 已离开 Group、非 Entry Graph、Assets、I18n、settings：提示重新开始并新建存档。
- Contracts 改变：重启调试 App。
- 首期不处理环，不迁移旧存档，不原地替换已持有媒体对象。

## 关键简化

审查后删除或取消了 manifest revision、Latest/Run DTO、inspect 版本字段、超时对账、分类 Reasons、环标志、重复入口能力布尔、请求序号、Notice 副本、两个额外常驻菜单、资源使用位置分析和退休版本上限。内部不可变构建目录 key 只解决文件句柄与复用问题，不进入协议。

## 验证

- `GalNet.Debugging.Tests`：19/19。
- `GalNet.Runtime.Tests`：128/128。
- Debug 内容服务、构建器、Snapshot/primitive dispatch 定向集成：30/30。
- Editor 真实 loopback 挂载 UI 测试：1/1，覆盖 Direct、ReturnGroupStart、RestartGame pending、源码映射提交与 generation 后续命令。

仓库既有 analyzer/Avalonia warning 保留，无新增编译错误。

## 提交

- `d8fb5675`：候选与分类初版。
- `cfed4cc6`：Runtime 原子替换。
- `0b7e0c66`：客户端安装/激活初版。
- `a4787c71`：撤销独立拓扑摘要。
- `c049ebce`：删除内容版本状态机并收缩客户端状态。
- `4f825460`：Editor 协调、提示与闭环。

## 文档

- [Editor 调试规范](../../docs/spec/editor/debugging.md)
- [Runtime 执行规范](../../docs/spec/runtime/execution.md)
- [Editor 调试操作手册](../../docs/manual/editor/preview-and-debugging.md)

## 未专项验证

未执行真实 Developer Sample 窗口的媒体观感和移动平台验证。资源/I18n/settings 变化当前明确走重新开始路径，因此不声称已持有对象可在运行中原地更新。
