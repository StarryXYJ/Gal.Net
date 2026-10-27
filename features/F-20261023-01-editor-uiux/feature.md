# Editor UI/UX 优化

状态：implemented / automated-verified；用户于 2026-10-23 明确授权执行已确认方案。真实桌面 DPI/原生 IME 等人工验收保留为 follow-up，见 review。

目标：提高剧情编写、参数编辑、问题定位与工具导航效率；保留项目统一历史、作者 JSON、保存事务及自定义契约兼容性。

已确认：独立 DTO `[Common]` 标记行内默认字段，选中后 Inspector 完整编辑；手动布局预设；项目会话保存未完成输入；透明图标按钮；160ms 平滑且无亮度峰值的 hover。

范围：P1 按钮与 hover；P2 常用字段和项目草稿；P3 Group/Inspector；P4 保存状态和 Problems；P5 布局与导航。

非目标：参数多选批量编辑、游戏文件迁移、运行时语义改变、跨重启保留无效控件输入。

验收及实施顺序见 [设计](design.md)、[阶段](phase-plan.md)。现有其他 feature 与测试修改保留。

首次使用反馈见 [2026-10-24 后续记录](follow-up-20261024.md)：人物 Enter 与 Inspector 焦点问题已修复；按钮 hover 最初仅分析，随后按用户授权实施 [动画统一](motion-unification.md)。

第二轮 UI 优化的原始分析见 [2026-10-25 分析](analysis-20261025.md)：GameTextBox 主输入行对齐、Inspector 底部预览、角色/本地化图标与排版、变量子页选择及草稿、其他 UX 建议。用户随后明确授权实施，进度及证据见 [第二轮实施](phase-ui-20261025.md)。

本地化页后续反馈与资源表精简方案见 [本地化 UI 再分析](analysis-localization-20261025.md)：状态降噪、已有语言列表/新增候选/移除语义、双行定位列、不透明统一背景及 VS/Rider 参考。用户随后授权实施，并要求直接更新输入、不提供草稿或复核流程；进度与验证见 [本地化精简实施](phase-localization-20261025.md)。

本地化实测反馈：隐藏列后表头错位、正文输入需左上对齐、逐字更新卡顿。用户要求改为失焦提交，当前修正见 [本地化交互修正](follow-up-localization-20261025.md)。

2026-10-27 Group 后续：待提交分级、自动检查、人性化提示与稳定定位，以及 NumericUpDown/Slider/精确文本可选路径和浮点简写已实现验证，见 [实现及验证记录](follow-up-group-diagnostics-numeric-20261027.md)。
