# P0：Runtime 当前 Group 末尾追帧

状态：verified。

新增 GameEngine.SkipToGroupEndAsync，复用现有 CatchUp；忽略未读边界，显式可忽略 Entry 条件，但原有逻辑写入仍执行。停在当前 Group 的尾游标，不自动进入后继、不显示后继 Choice；下次推进离开。

不同目标并发追帧拒绝；同目标同策略共享任务。保留 scope、清理等待、最终展示同步、失败传播与 Stop drain。没有改 IPlayerPlaybackController，也没有提前接入外部 Editor UI。

验证：

- dotnet test test/GalNet.Runtime.Tests/GalNet.Runtime.Tests.csproj --no-restore --filter "FullyQualifiedName~GroupEndCatchUpTests|FullyQualifiedName~PlaybackModeTests" --verbosity minimal：33 通过。
- dotnet test test/GalNet.Runtime.Tests/GalNet.Runtime.Tests.csproj --no-restore --verbosity minimal：96 通过。
- 构建包含 Runtime/Core/展示抽象与测试项目；既有 CA1861 建议未修改。

测试覆盖场景行为与生命周期，不以字段赋值代替展示清理验证。跨进程/Editor/原生媒体尚未实施验收；P1–P6 继续。
