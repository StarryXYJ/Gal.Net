# 嵌套 Expander 的展开事件会冒泡

状态：confirmed。

## 现象与根因

音频界面首版在外层 Expander 的 Expanded 事件里重建总线列表。展开子总线或轨道后，无头测试的 Dispatcher 队列持续运行。Expanded/Collapsed 是路由事件，子项事件会冒泡至父级；父级重建子列表，后续展开状态恢复又可再次触发父级处理。

## 发现与验证

来源：F-20261103-02-editor-ui-polish 的音频嵌套列表。最终的 AudioTracksDisplayTheirIdentityAndAccessibleActions 在明暗主题中统计父级收到的子项展开事件，并验证展开总线、轨道及进阶项后，列表对象身份保持不变，测试可正常结束。

## 正确做法

普通嵌套列表直接绑定 IsExpanded，数据更新由集合和字段通知驱动。确需监听当前 Expander 的路由事件时，检查事件 Source 为当前控件；也可监听 IsExpanded 属性变化。监听父级事件时不要无条件销毁整个子树。

适用范围：Avalonia 的嵌套 Expander 以及类似使用路由事件驱动惰性构建的控件。禁止通过省略 Dispatcher 排空、禁用布局测试或不断调整窗口尺寸掩盖重入。
