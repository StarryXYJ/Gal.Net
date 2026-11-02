# 设计

沿用 SDL → StandardGamepadSnapshot → GamepadInputSession → PlayerInputSettings。PlayerInputRouter 决定当前输入用途、组合按键、400/100ms 连发、菜单模式和取消优先级；PlayerFocusNavigator 只处理当前范围内的焦点、编辑与方向寻找。不增加全局调度服务或每按钮 VM 状态。

范围优先级：宿主传入的模态 > 打开的按钮 Flyout > 跳过确认 > 历史 > 剧情分支 > 已进入的工具栏 > 当前普通页面。GameShell 提供当前实际页面，避免转场中的旧页面进入候选。宿主退出/语言选择和截图覆盖层显式交接范围，继续保留底层禁用。

布局声明 PlayerNavigation.IsToolbar / IsDefault / IsTarget。候选按实际可见、可用状态和 Bounds 计算；方向按中心与行列交叠优先，再比较距离；边界返回同排/同列另一端。不保存按钮数量、列数或索引表。每范围用弱引用保存最近焦点，失效目标回退默认/首个控件；Focus 使用 Directional 并 BringIntoView。

确认通过控件原生键盘激活路径执行，使 Toggle、Click、Command、Flyout 保持原有语义。组合编辑器排除内部实现按钮；数值框借用现有步进处理；存档名在 Sample 样式中不参与手柄导航，仍支持键鼠改名。

ChoiceList 将高亮和提交分开，鼠标/触摸点击或 Enter/Space 或手柄确认显式提交。设置下拉框预览期间同时阻断共享字段工厂和输入配置面板的提交，确认只提交最终值，取消恢复。

Start 改为保留导航键；恢复旧偏好时只移除含 Start 的手柄绑定，保留其他配置。方向斜推保持已有方向；中断仍由 GamepadInputSession 的中立门恢复。

历史方向可浏览语音按钮，无语音按钮时上下滚动；LT/RT 翻页。系统文本录入和原生目录选择仍由系统/键鼠负责。
