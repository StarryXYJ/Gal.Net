# 全新隔离源码需补齐固定路径契约产物

状态：confirmed
日期：2026-10-19
来源：F-20261015-02-editor-authoring-workspace / P6e3b

现象：在 HEAD 加本轮改动的全新源码快照中，离线 restore 成功，使用 --artifacts-path 构建时 Editor Copy 阶段 MSB3030，缺少 Editor.DefaultContracts/bin/Debug/net10.0/GalNet.Project.Contracts.dll；主工作树同参数构建此前成功。

根因：Editor.csproj 的 Content 固定读取默认 bin 路径，而 --artifacts-path 将项目输出移至独立 artifacts/bin/项目/debug。已有工作树残留的普通 bin 产物掩盖了这一前置条件。Editor 的 Contracts()/Deploy() 测试夹具也按源码树 test/Fixtures/*/bin/Debug/net10.0 定位。

发现方式：inline-curve-isolated-build.log 在全新快照中复现；不修改业务项目文件，将该快照本次构建产物补到它自身预期的默认契约与 fixture bin 位置后，inline-curve-isolated-build-2.log 构建通过。

正确做法：在验证脚本中明确准备此类固定路径前置产物，且必须来自同一源码快照；或另立构建修正任务消除硬编码。不要把主工作树不明版本 DLL 复制过来，也不要把有旧 bin 的 --artifacts-path 构建当作干净构建证据。临时快照包含重复 csproj 时放在忽略目录并在验证后清理，避免架构测试递归扫描测试根目录时误识别重复项目。

适用：全新源码快照、多个输出根、动态 schema 测试夹具；不代表新建 Editor 项目需要用户准备 DLL，Editor 仍自带默认契约。
