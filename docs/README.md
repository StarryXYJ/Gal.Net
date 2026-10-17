# GalNet 文档

这里仅保留当前可用的项目文档。功能开发过程、已废弃方案和决策记录不作为规范来源；需要追溯一次实现时，查看对应的 `features/F-*/` 记录。

## 项目入口

- [设计理念与架构概览](philosophy.md)：分层原则、依赖方向与高层架构图。
- [术语表](glossary.md)：项目通用术语的统一定义。

## 规范

`spec/` 描述已实现的模块契约和当前限制。新增或修改功能后，应更新其唯一对应页面，而不是另建设计或 ADR 文档。

- [架构与程序集边界](spec/architecture.md)
- [运行时与存档](spec/runtime.md)
- [条目类型与项目契约](spec/entry-types.md)
- [资源管理](spec/assets.md)
- [项目与发布格式](spec/file-formats.md)
- [Avalonia 游戏页面](spec/control.md)
- [国际化](spec/i18n.md)
- [Editor Graph 编辑](spec/editor-graph.md)
- [Editor 历史与保存](spec/editor-history-and-save.md)
- [Editor 项目文件操作](spec/editor-files.md)
- [Dock 集成](spec/dock-design.md)

## 二次开发手册

- [接入新的表现层](manual/new-presentation-layer.md)：为 WPF、WinUI 或其他 UI 框架实现宿主和呈现适配的最小路径。
- [定制 Avalonia Sample](manual/customize-avalonia-sample.md)：基于现有 Sample 落地项目配置、主题、原语和平台能力。
