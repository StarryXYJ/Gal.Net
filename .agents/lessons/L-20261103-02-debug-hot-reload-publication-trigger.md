# 调试热重载由内容发布自动触发

状态：confirmed
来源：F-20261103-01-debug-content-hot-reload

## 现象

内容热重载的构建、分类和提示只接在“应用内容修改”命令后。作者保存内容时只出现 dirty 提示，安全变化不会直接生效，需要返回 Group 开头或重新开始的变化也不会即时 Toast。

## 根因

把顶部菜单入口误当成热重载主触发器，遗漏了项目内容的保存/原子发布边界。另一个容易踩到的边界是 `AuthorChangeJournal.Committed` 在历史事务退出捕获锁前发布；在回调内同步构建会报“Cannot capture a partially executing edit”。

## 发现方式

用户在真实调试 App 中确认只有点击按钮才有效。loopback UI 回归改为保存后等待自动应用，先复现无请求；补结构变化后又复现历史捕获锁冲突。

## 正确做法

- 已保存内容或原子结构发布完成后，自动调用既有构建、安装、分类和应用流程。
- Direct 自动激活并 Toast“热重载已应用”；Return/Restart 安装候选后立即 Toast 对应要求，并在面板保留动作。
- 顶部菜单和面板热重载按钮只作为显式重试入口。
- 在作者事务中只记录待更新事实，把实际构建投递到 UI 事务退出后；普通未保存编辑等待保存完成。
- 复用 Panel Busy 串行化，不另建版本、请求或调度状态机。

## 适用范围

适用于 Editor 项目作用域内，由保存会话或原子结构操作发布到磁盘的 Developer 调试内容变化。

## 禁止做法

不要只测试按钮命令后能否分类；回归必须从作者修改和保存/发布动作开始，并验证 Direct、Return、Restart 三条自动路径。不要在历史 `Committed` 回调栈内同步启动需要捕获作者内容的构建。

关联文档：`features/F-20261103-01-debug-content-hot-reload/design.md`、`docs/spec/editor/debugging.md`。
