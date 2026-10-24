# Editor UI/UX 优化

状态：implemented / automated-verified；用户于 2026-10-23 明确授权执行已确认方案。真实桌面 DPI/原生 IME 等人工验收保留为 follow-up，见 review。

目标：提高剧情编写、参数编辑、问题定位与工具导航效率；保留项目统一历史、作者 JSON、保存事务及自定义契约兼容性。

已确认：独立 DTO `[Common]` 标记行内默认字段，选中后 Inspector 完整编辑；手动布局预设；项目会话保存未完成输入；透明图标按钮；160ms 平滑且无亮度峰值的 hover。

范围：P1 按钮与 hover；P2 常用字段和项目草稿；P3 Group/Inspector；P4 保存状态和 Problems；P5 布局与导航。

非目标：参数多选批量编辑、游戏文件迁移、运行时语义改变、跨重启保留无效控件输入。

验收及实施顺序见 [设计](design.md)、[阶段](phase-plan.md)。现有其他 feature 与测试修改保留。

首次使用反馈见 [2026-10-24 后续记录](follow-up-20261024.md)：人物 Enter 与 Inspector 焦点问题已修复；按钮 hover 最初仅分析，随后按用户授权实施 [动画统一](motion-unification.md)。
