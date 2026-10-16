# Headless 拖动的移动事件必须携带按键状态

状态：confirmed
来源：F-20261015-02-editor-authoring-workspace / P2

现象：实际节点收到 PointerPressed，但下一次 MouseMove 的 IsLeftButtonPressed 为 false，画布按取消处理，坐标没有移动。

根因：Avalonia Headless 的 MouseMove 使用该事件显式提供的 RawInputModifiers，不从之前的 MouseDown 自动推导持续按下。普通悬停测试的 MouseMove(point) 不能直接用于拖动。

正确做法：按下后使用 MouseMove(point, RawInputModifiers.LeftMouseButton)，松手使用 MouseUp；取消测试还要发送 Escape 的完整 KeyPress 参数。先挂载主题、窗口及实际控件并捕获帧，再测试真实命中与事件路径。

验证：GraphCanvasActualPointerDragCanCancelAndCommitOneHistoryStep 在缺少修饰符时稳定失败；显式携带修饰符后实际预览、Escape 还原、松手提交和一步撤销全部通过。

适用范围：Avalonia Headless 的拖动、框选、滑块等持续按键手势。禁止通过直接调用提交方法来替代失败的实际指针验收。
