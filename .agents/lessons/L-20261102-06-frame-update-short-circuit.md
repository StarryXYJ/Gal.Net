---
status: confirmed
feature: F-20261102-05-presentation-polish
---
# 更新所有帧对象后再汇总活跃状态，不能用 Any(Update)

现象：用户实测多轮天气始终串行，后建发射器等第一个排空才开始。先前 presenter 单体回归通过，因为测试手动逐个 Update，绕过真实场景宿主。

根因：SceneLayerHost.AdvanceRenderables 的 Any(renderable => renderable.Update(frame)) 在第一个 true 后短路，后续对象没有接收到帧。实际 Host.Render 回归的三个对象更新计数为 [1,0,0]，期望 [1,1,1]。

修复：foreach 无条件更新每个对象，使用非短路布尔聚合，最后才决定请求下一帧。不得把有副作用的 Update/Dispose/通知藏在 Any/All 这类短路查询里。

验证：实际 Host.Render 的多发射器计数回归先失败后修复；Headless DrawingGroup 不支持最终 Custom 操作，测试仅容许该特定 NotSupportedException，仍检查实际帧更新入口。单体粒子测试不能替代宿主整链回归。

来源：本 feature 的 P5 用户反馈与 output/weather-polish/host-repro-final.log。
