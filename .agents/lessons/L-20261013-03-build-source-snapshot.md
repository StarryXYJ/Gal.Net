# 构建期间保持源码快照稳定

Status: confirmed
Source feature: F-20261013-01-editor-cli-operations

## 现象与证据

顺序测试脚本已构建 Presentation.Abstractions 后，agent 在脚本继续构建 Editor 时新增 RequireInstructions，并修改消费者。Editor 报 CS1061，引用的是脚本先前构建的程序集。停止修改后完整顺序重跑，Builtins 64/64、Presentation 定向 7/7、Headless 构建和 Editor 31/31 通过。

## 根因与发现方式

顺序执行只避免多个进程竞争 obj，并不保证构建过程中被修改的源码会再次被构建。同一脚本中的依赖输出可能来自新增 API 之前。错误发生时源码已有 public 方法，重新构建后错误消失。

## 正确做法与适用范围

启动构建/测试前完成本轮源代码修改；等待整个脚本结束后再修改其依赖图里的源码。验证期间可阅读文件、整理不参与编译的文档。若已发生交错，完整重跑受影响的构建链，之前结果不能作为最终修改的验证证据。

## 禁止做法

不要在仍运行的构建脚本中修改其共享依赖，再把消费者编译错误归因于源代码 API 或把旧测试通过计入最终证据。

## 关联

参见 L-20260929-07-parallel-test-shared-obj-lock.md；两条规则分别约束进程竞争和源码快照。
