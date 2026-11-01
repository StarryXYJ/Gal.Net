# NUnit Explicit 用例的 TRX 执行证据

- 状态：confirmed。
- 日期：2026-10-30。
- 来源：features/F-20261030-01-project-quality，P8a。

## 现象与根因

NUnit Adapter 5.0.0 的音频测试 TRX 中，total=28、executed=26、passed=26，但 notExecuted=0；两个 Explicit 设备用例仍有 outcome=NotExecuted 的结果记录。控制台最终摘要也只显示 26 个通过和 0 个跳过。仅照抄 notExecuted 或控制台摘要会漏记未运行的设备测试。

## 正确做法

保留原始 TRX 计数，并用 total-executed 明确记录未运行数量。零执行、缺失结果、非 Completed、失败或通过数不等于执行数均不能验收成功。真实设备、GUI、NativeAOT 等边界单独记录，不从后端测试成功推导。

本规则适用于当前 NUnit/VSTest 入口；其他适配器须检查其真实报告。回归见 scripts/ci/test-backend-checks.py，原始证据见 out/project-quality-p8a-windows/results/GalNet.Media.Audio.Tests.trx。

## F-20261101-01 补充复现

ResourceGallery 选择普通类并 OR 显式场景方法时，只执行了 13 个普通用例，四个 Explicit 场景仍未运行。随后仅选择 SceneGalleryRendersBothSampleScenesAndReturnsWithoutChangingPlayerProgress 方法，TRX 才确认四项全部执行。需要真实解码/渲染证明时单独选择 Explicit 方法并核对结果项；不要从同次筛选包含方法名或控制台零失败推断已运行。证据：该 feature 的 P2 scenes.trx、scenes-explicit.trx；P3 native-media-final.trx 独立选择显式方法，8/8 执行通过。
