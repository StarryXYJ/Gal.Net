# 调试清单的拓扑摘要必须排除 Group Entry 内容

状态：confirmed

## 现象

开发调试内容热更新中，只移动 Graph 节点或修改 Editor 显示名称，差异分类仍会得到 `RestartGame`，尽管运行拓扑没有变化。

## 根因

调试清单把完整 `Graph/graph.json` 的文件哈希记录为 `GraphSha256`。该文件除节点、边和分支外，还包含节点名称、画布坐标和源文件路径等 Editor 作者信息；分类器把这个摘要的任何变化都解释为 Graph 拓扑变化。

## 发现与证据

在接入 Editor 协调服务前核对真实构建清单发现摘要来源与分类语义不一致。`ProjectContentBuilderTests.DebugRevisionBuildsImmutableReusableDirectories` 增加回归：修改节点名称/坐标或作者 Entry 后 revision 可改变而拓扑摘要不变；新增节点和边后拓扑摘要必须改变。修复后该组 5/5 通过。

## 正确做法

- 为分类目的计算独立、规范化的拓扑身份，只包含会改变运行路径或会话结构的字段。
- Group Entry 内容由作者 Entry span 和内容摘要比较；Editor 名称、坐标和源文件路径也不得混入运行拓扑摘要。
- 摘要字段命名和生产方式必须对应消费端语义；文件哈希只适合“整个文件任意变化都同等级”的场景。

## 禁止做法

- 不要因为数据来自同一个 `graph.json` 就复用整文件哈希表示拓扑。
- 不要只用手工构造且固定拓扑摘要的分类器测试证明真实构建闭环。

适用范围：调试清单、增量构建、热更新与任何按差异类别决策的内容身份。

来源：`features/F-20261103-01-debug-content-hot-reload`

