# 游戏宿主分层重构

用户已确认：核心提供调度、编译和执行机制，指令由可选模块提供。保留 Core 现有模型位置，暂不实现 WPF、WinUI 或 Unity。

目标：Core/Runtime → 平台无关的游戏应用与展示契约 → Avalonia 实现与默认组合 → Sample/Editor。Assets 与 Storage.FileSystem 作为外部基础设施组合。

## 实施步骤

1. 移除项目目录删除与 ProjectTools.Windows；保留最近记录移除。
2. 下放默认 Avalonia 展示组合、纹理工厂及释放流程。
3. 抽出 GameHost 通用运行会话；Engine 独占执行机制。
4. 分离通用资源和 Avalonia 纹理预加载。
5. 下放玩家持久化协调，分离 Gallery 展示。
6. 分离玩家偏好规则与 Avalonia 设置 UI。
7. 调整平台无关接口归属，检查媒体后端边界。
8. 收敛 Sample、补充架构文档并执行回归。

每一步验证后独立提交。不得提交任务开始前已有的输入设置与编辑器改动。

## 验证记录

- Step 1 verified: Architecture 37/37; Editor.Shared 180/180; ProjectSchemaLifecycle 5/5.
