# 设计

## 字段与草稿

Core 的 CommonAttribute 由生成器传入 EditorHints.IsCommon，默认 false。共享表单默认完整，Editor 行内显式选择 Common，Inspector 完整。对象保留原有专属编辑器。

项目编辑会话拥有字段草稿，以 Group、Entry、字段路径与源值标识；控件拥有展示及订阅，不拥有草稿。来源改变时冲突，不覆盖新作者值。有效输入通过现有作者意图提交，保存统一提交有效草稿，无效草稿保留并定位。

## 交互

选择与展开独立；列表提供摘要和常用参数，Inspector 显示完整参数、条件及目标身份。多选不隐式批量修改参数。辅助工具不清空详情，Inspector 可锁定。

保存状态独立于校验状态；Problems 面向当前作者快照，导航使用稳定身份与版本。原始日志保留，不从字符串猜测位置。

剧情、动画、调试布局手动应用；旧保存布局优先。切换、最大化与恢复不丢作者选择、文档或草稿，也不进入作者历史。Timeline 树宽可调，Graph 可搜索和适配，资源增加导入/新建、筛选与列表。

## hover

已用 Avalonia 12.0.5 ColorAnimator 复现 Transparent(#00FFFFFF) → #FF313244：25%=#40e2e2e3、50%=#80bebec0，叠在暗背景会亮于端点。使用预乘透明度的 sRGB 线性插值，透明端点 RGB 不泄漏；160ms 线性过渡，支持从中间值反向。统一透明图标按钮保留焦点、禁用和切换状态。

首次使用反馈后的统一策略：背景/边框仅由实际绘制层执行过渡。Button/ToggleButton 外层作为模板绑定来源，不配置同一属性的第二次过渡；普通按钮由 ContentPresenter，图标按钮由 ButtonSurface，输入与菜单由模板 Border 执行。移除宽泛 Panel/TextBlock 过渡，避免额外的继承颜色追逐。时长集中在 EditorMotion.HoverDuration，保持 160ms、零延迟、线性曲线。图标切换按钮显式规定 checked、checked-hover、checked-pressed 的优先级，禁用状态不接受 hover/pressed 覆盖。

## 兼容

不改变作者 JSON、必填、默认值、运行时语义。保留现有历史与事务；自定义 DTO 可读取表单展示模式。新增 metadata 需要同源码重建契约程序集并核验指纹。
