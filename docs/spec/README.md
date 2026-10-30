# 技术规范

规范描述当前实现的接口、状态、格式及限制，不存放未实现的计划。操作教程见手册；实现取舍与验证历史见对应 feature。

<!-- navigation:start -->
- [架构设计](architecture/README.md)
  - [GalNet 架构](architecture/modules.md)
  - [组合、生命周期与所有权](architecture/lifecycles.md)
  - [CPU 性能事件](architecture/diagnostics.md)
- [运行时规范](runtime/README.md)
  - [条目类型与 target profile](runtime/entries.md)
  - [运行时参考](runtime/execution.md)
  - [运行状态、快照与存档](runtime/state-and-save.md)
- [内容与资源规范](content/README.md)
  - [资源文件管理](content/assets.md)
  - [项目与发布格式](content/file-formats.md)
  - [国际化](content/localization.md)
- [呈现与宿主规范](presentation/README.md)
  - [游戏宿主与玩家会话](presentation/game-host.md)
  - [Avalonia 游戏页面](presentation/game-view.md)
  - [媒体后端契约](presentation/media.md)
- [Editor 技术规范](editor/README.md)
  - [Editor 项目文件操作](editor/project-and-files.md)
  - [Editor Graph 编辑](editor/graph.md)
  - [Editor Group 编辑交互](editor/groups.md)
  - [Editor 变量面板](editor/variables.md)
  - [Editor 项目历史与保存](editor/history-and-save.md)
  - [Editor 游戏本地化基础](editor/localization.md)
  - [Editor 场景目标查询](editor/scene-targets.md)
  - [Dock 面板与布局契约](editor/dock.md)
  - [外部调试状态与缓存契约](editor/debugging.md)
<!-- navigation:end -->
