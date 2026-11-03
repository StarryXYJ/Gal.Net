# 开发调试内容热更新设计

状态：implemented

## 1. 行为

Editor 构建一份完整候选内容并先交给调试 App 保存，然后根据 App 当前运行位置决定动作：

| 变化位置 | 行为 |
| --- | --- |
| 未经过的 Group、当前 Group 尚未执行的作者 Entry | 直接替换，下一次分发读取新内容 |
| 当前 Entry 或当前 Group 已执行前缀 | Editor 提示返回当前 Group 开头应用 |
| 已离开的 Group，或不能证明可安全替换的非 Entry 内容 | Editor 提示重新开始游戏并新建存档 |

首期不处理环。Contracts 变化要求重启调试 App。

## 2. 职责

### Editor

`DebugContentHotReloadService` 是唯一决策者，只保存：

- `PublishedBuild`：当前运行采用的完整构建及源码映射；
- pending build：已经安装、尚待返回或重开的完整候选；
- `PendingImpact`：面板应显示的一个动作。

服务负责构建、安装、分类和发送明确命令。Panel 在命令完成后显示 Toast；顶部菜单只提供“应用内容修改”，返回和重开按钮只在面板出现。

### 调试 App

`DebugContentUpdateService` 只保存当前运行候选与最新完整候选两个对象引用。它验证并打开候选目录、提供最新 Graph、在激活成功后切换运行引用，并让新 Run 选择最新候选。它不分类、不显示提示，也不维护内容版本号。

### Runtime

Runtime 报告当前 Group、下一编译 Entry、活动 primitive、自然经过的 Group ID 序列和已有 Group 入口缓存。它只提供一个原子 Graph 替换原语，以及已有的返回 Group 开头能力。

## 3. 清单与分类

调试清单记录项目/契约/内容身份、作者签名、每个稳定作者 Entry 的编译 span 与摘要，以及非 Entry 文件摘要。它没有 revision 字段。

Editor 用当前运行清单解释编译位置和活动 primitive，再按稳定作者 Entry 比较候选：

- 已执行作者前缀必须保持相同，否则返回 Group 开头；
- 已离开 Group 的内容必须保持相同，否则重新开始；
- 非 Entry Graph、Assets、I18n 或 settings 变化统一保守要求重新开始；
- 直接替换时把当前作者边界映射到候选编译 index。

不区分更细的拓扑变化种类，也不构建资源使用位置依赖图。

## 4. 协议

协议只有三条内容命令：

- `content.install`：安装完整最新候选，不改变当前运行；
- `content.activate`：在 run/generation/运行边界仍匹配时激活最新候选；
- `content.activateAndSeekGroupStart`：激活最新候选后复用当前 Group 入口缓存返回开头。

请求不携带内容版本号。命令成功才提交 Editor 源码映射；失败或超时保持旧映射。既有 run ID、generation 和位置前置条件继续防止在错误执行位置替换，这不是内容版本状态机。

重新开始复用 `stop` 和 `start`；客户端的新 Run 自动选择最新候选。

## 5. 生命周期

构建目录按内容生成内部不可变 key，避免 Windows 已打开资源被覆盖；该 key 不进入协议或运行状态。

从未激活且被新候选替代的资源 scope 立即释放。曾作为运行内容使用的 scope 由调试服务持有到服务结束，已有媒体和资源句柄按正常生命周期完成。不设置退休版本上限或容量错误。

## 6. 明确不做

- 内容 revision、LatestRevision/RunRevision 对账；
- 响应丢失后的 inspect 提交协议；
- 环检测和重复 Group 专用状态；
- Graph 局部拼接、自动迁移旧存档；
- 正在播放对象的原地替换；
- 资源引用位置分析和逐句柄退休调度。
