# 分支节点高度变化必须发布连线布局刷新

- 状态：confirmed
- 日期：2026-10-16
- 来源：Editor Graph 分支节点连接线错位修复

## 现象

分支节点新增多个选项或条件后，节点高度变大，输入连接点向下移动，但连接到该节点的既有连线仍停留在旧终点；断开后重新连接会恢复正常。

## 根因

`GraphNode.RefreshConnectors()` 会先重建输入连接器，再重建输出连接器。入边监听到输入集合变化时，输出数量仍是旧值，因此计算出的节点高度和输入点位置仍是旧布局。输出集合重建完成后，节点没有再发布一次布局变化通知，导致入边的 `PathData` 不刷新。

## 正确做法

连接器集合重建完成后发布独立的节点布局变化通知，`GraphEdge` 对该通知重新发布起点、终点和 `PathData` 属性变化。布局通知不能只依赖某一侧连接器集合的 `CollectionChanged`，因为另一侧连接器数量也会改变节点整体高度。

## 适用范围

适用于所有通过连接器数量影响节点几何尺寸的 Graph 节点，尤其是 Choice 和 Condition 分支的入边。

## 禁止做法

不要要求用户通过断开/重连来修复显示，也不要只在输出边上监听输出连接器集合变化；这会遗漏分支节点高度变化对输入连接点的影响。

## 证据

- `src/Editor/GalNet.Editor/Models/Graph/GraphPresentationModels.cs` 发布 `LayoutHeight` 变化，`GraphEdge` 监听并刷新路径。
- `test/GalNet.Editor.Tests/GraphEditingServiceTests.cs` 覆盖目标分支扩展后入边终点下移和 `PathData` 通知。
- Editor 测试项目构建通过；当前环境的 NUnit/VSTest 测试宿主在启动执行阶段无输出并被停止，未取得运行时测试计数。
