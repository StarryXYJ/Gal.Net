# 曲线采样需覆盖精确关键帧边界

状态：confirmed
日期：2026-10-19
来源：F-20261015-02-editor-authoring-workspace / P6e3b

现象：Step 段之后的中间关键帧，采样恰好落在其帧号时仍返回前一键值；第 0 帧、末键及区间中点原测试均通过。内联值曲线的键位置与运行时该时刻值可能不同。

根因：TimelineSampler 查找区间使用 next.Frame < frame，把内部键的精确时刻归给前段。Linear/Hermite 在末端返回下一键值，掩盖了问题；Step 保持前值而暴露错误。

发现与复现：新增 0→10→20 三键测试，首段 Step；Evaluate(9.999)=0，Evaluate(10) 应为 1、实际为 0，基线失败日志和 TRX 为 timeline-validation/inline-curve-step-before-fix.*。修正查找为 <=，精确内部键归入它自己的出段，末键仍由已有末端分支处理。

正确做法：动画/曲线采样回归同时覆盖精确内部键、键前后分数时刻、首末键和不同插值组合；Editor 绘制直接复用中立采样器，不在 UI 加偏移来绕过错误。

适用范围：时间区间归属、阶梯/离散插值、曲线与 Runtime 一致性。禁止仅测试区间中点或末键便宣称边界一致。

关联：features/F-20261015-02-editor-authoring-workspace/phase-6e3.md；docs/spec/editor/groups.md。
