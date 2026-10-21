# P1：定义与纯解析

日期：2026-10-21
状态：verified。

## 交付

`GalNet.Core.Variants` 提供作者描述 DTO、宿主图片事实、不可变定义、带位置的构建诊断和纯解析器。没有注册资源类型、游戏指令或编辑器入口，没有修改现有 Layer 行为。

文件名只表达连续前缀，匹配过程始终保留维度原位置；从后向前累计删除未保护维度，不做通配符或其他值替换。完整请求与实际图片分别保存，经过单段特殊表情后仍可恢复之前服装。相同值重复选择会重新设置本次保护维度。

构建器按 ordinal 生成稳定顺序；显式维度不足、重复维度、空段、重复主名/大小写碰撞、坏 default、无资源 ID、非 sprite 等产生诊断，且不返回半份定义。自动维度为 d1/d2 等，未设标签时显示位置编号；没有人为固定维度数量上限。

作者 DTO 可用 System.Text.Json 读写；只包含描述字段。运行时索引序列化与 decoder 留 P2，避免现在引入未经验证的发行协议。

## 验证证据

- Core 测试共 **148 通过，0 失败，0 跳过**，包含新增图集 **32** 项用例。
- Architecture 测试 **45 通过，0 失败，0 跳过**。
- 四份新增 Core 源码与新增测试文件的 `dotnet format whitespace --verify-no-changes` 通过；`git diff --check` 通过。
- Core 项目在测试命令中完成编译。P1 仅新增纯 Core API，没有运行全解决方案/图形人工验证，不宣称运行时图集或 UI 已可用。

验证命令（仓库根目录，测试需在允许本地进程通信的执行环境运行）：

```powershell
dotnet test test/GalNet.Core.Tests/GalNet.Core.Tests.csproj --no-restore --logger 'trx;LogFileName=core-p1-final.trx' --results-directory artifacts/image-variants-p1 --verbosity quiet -p:WarningLevel=0
dotnet test test/GalNet.Architecture.Tests/GalNet.Architecture.Tests.csproj --no-restore --logger 'trx;LogFileName=architecture-p1.trx' --results-directory artifacts/image-variants-p1 --verbosity quiet
```

最终 Core 回归仅关闭编译警告输出，未关闭分析器或跳过编译；前次未抑制警告的编译成功，Core 既有 API 命名警告和测试期望数组的 CA1861 建议不影响验证结果。TRX 留在忽略的 artifacts/image-variants-p1，未加入源码提交。

覆盖：用户五张示例；三维逐项删除；保护中间维度不压缩位置；不自动切到其他表情；多维初始覆盖；同值重新选择；未知维度和值；空请求与空保护集合；不完整默认图；源对象、请求和实例隔离；目录枚举顺序；十六维与 ar-SA 下的不变编号；错误诊断及作者 JSON wire 字段。

## 环境问题与处理

第一次基线测试在沙箱中因 testhost 90 秒未连接而中止，没有执行用例；增加到 180 秒也不能恢复本地连接。沙箱内格式检查明确抛出命名管道 `UnauthorizedAccessException`。同一份已编译测试在获准的沙箱外执行后，新增 31 项立即通过；补充十六维用例后的最终全量为 148 项通过。两次沙箱连接失败不计为测试通过或产品断言失败。

这与已有 `.agents/lessons/L-20260922-01-dotnet-build-sandbox.md` 的进程间管道权限经验相关；本次验证说明 testhost 和格式工具也可能受限。不要通过修改业务代码或仅延长超时处理；应在许可范围内使用可通信的执行环境，并记录真实通过证据。

## 状态与后续

P1 退出条件已满足，无匹配算法偏差。P2 接入目录边界检查、资源类型/decoder、本地与 PAK 编译定义和资源操作事务；P3–P5 仍未开始。

提交只包含本 feature 文档、Core/Variants 与对应测试。工作区已有 `F-20261015-02-editor-authoring-workspace/phase-plan.md` 修改保留，不混入本阶段。
