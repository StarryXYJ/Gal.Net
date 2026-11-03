# 调试内容更新优先使用对象状态，不扩展为版本协议

状态：confirmed
来源：F-20261103-01-debug-content-hot-reload

## 现象

开发调试热更新最初被实现为 manifest revision、LatestRevision/RunRevision、inspect 对账、请求序号、超时后提交确认和退休版本上限。功能可以工作，但状态和错误分支远超产品行为需要，并促使拓扑摘要、资源依赖分析等继续扩张。

## 根因

把“客户端暂存一个完整候选，Editor 决定何时应用”误建模成跨进程内容版本管理。实际安全边界已经由 run ID、runtime generation 和执行位置前置条件保证；内容本身无需再有一个可观察版本状态机。

## 正确做法

- Editor 只保存当前已应用 build 与 pending build；客户端只保存当前运行候选与 latest candidate 两个对象引用。
- install 始终替换完整最新候选，activate 始终激活该候选；协议不传内容 revision。
- 命令成功才提交 Editor 源码映射；失败或超时保留旧映射并报告，不增加响应丢失对账协议。
- 分类只返回产品需要的 Direct/Return/Restart；非 Entry 变化无法证明安全时统一重开。
- 先复用 Panel Busy、作者签名、已有 Group 缓存与 runtime generation，避免为同一事实增加专用字段。

## 适用范围

适用于 Editor 与本地 Developer App 之间、候选始终完整且用户可重试的调试更新。若未来出现远程分发、持久安装、回滚或多客户端并发，再作为独立 feature 证明版本协议的必要性。

## 禁止做法

不要仅为处理极端迟到响应或推测未来资源退休需求，预先加入内容 revision、双指针 DTO、请求序号、容量状态或依赖图。

关联文档：`features/F-20261103-01-debug-content-hot-reload/design.md`、`docs/spec/editor/debugging.md`。
