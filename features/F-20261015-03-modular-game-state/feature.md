# 模块化状态与恢复

2026-10-15 用户授权实施 [已确认设计](../../docs/design/modular-game-state-and-restore.md)。同批迁移视觉状态、音频轨道与 SEffect；直接删除旧状态/重放路径，不兼容旧档，使用新建存档验证。目标是唯一持久事实来源、显式模块注册和通用恢复生命周期。

范围：Core 中立状态容器/封套、Runtime 快照与读档、Builtins 状态与恢复贡献、音频逻辑/后端映射分离、Sample 和真实回归。保护工作树中独立 Editor authoring feature 的改动。
