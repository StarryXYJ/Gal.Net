# 模板验收探针

这些是 feature 验收工具，不是新的模板生成器或运行时扩展协议。默认使用新的输出目录；不提交包、截图、profile 和 SDK。

## 仓库外消费者

先使用 `scripts/release/Pack-NuGet.ps1` 和 `Test-NuGet.ps1` 生成、验证同一版本的本地包，然后运行：

```powershell
python features/F-20261031-02-sample-ui-template/probes/external-template.py `
  --repository . --packages .artifacts/sample-ui-template/p6/packages `
  --version 0.1.0-dev.99 --output .artifacts/sample-ui-template/p6
```

`external-template.json` 记录临时消费者目录、版本、提交和修改文件。探针只复制 Sample 应用和项目契约源码，将内置工程引用换成真实包引用；复制现有模板回归并增加五项定制测试。标题按钮、计数命令、Notebook 页面、Rose 配色与独立按钮模板、游戏工具栏均属于消费者。结果不能解释成原生平台验证。

## Windows 真实窗口

使用 `scripts/ci/publish-applications.py` 发布同版本 Windows CLI；把上一项 JSON 的 `consumer` 传入：

```powershell
pwsh -File features/F-20261031-02-sample-ui-template/probes/native-windows.ps1 `
  -Consumer '<consumer>' -Cli '<published-CLI>/GalNet.Editor.Headless.exe' `
  -Output '<new-evidence-directory>'
```

`NativeFlow.cs` 使用真实窗口和真实会话，在 UI 线程调用已呈现控件的绑定命令，验证场景、预览、保存/改名/读取、暂停/重挂载、scope 重建及确认取消/接受。它不是操作系统鼠标自动化；退出确认额外触发实际按钮 Click。内容从消费者输出的完整契约集合导出，避免误用 Editor 默认项目契约。

## Linux 原生 SDK 与 WSLg

`linux-publish.py` 接收 `--repository`、`--environment`、`--commit` 和 `--version`。环境 JSON 的 `root` 必须是当前用户 `~/work/galnet-sample-ui-p6-*` 独占目录，`sdk` 指向其中核验过版本和发行 SHA512 的原生 .NET SDK。脚本在新检出中发布 Editor、CLI、Player 和 Developer，检查契约一致性；不复用旧应用产物。

用仓库 `prepare-linux-media.py` 在该目录准备 `media/`，使用签名 apt 索引和 hash 验证，不全局安装软件。`linux-native.py` 接收相同环境、仓库根、Windows 独立消费者目录及其 `--content`。它在 Linux 原生编译定制应用，显式引用冻结的消费者项目契约产物，运行真实 WSLg 窗口。同一版源码在两个 OS 重新编译不保证严格字节身份；不能用原生 Linux CLI 导出另一套包构建身份的契约，也不能关闭校验。

`linux-media.py --environment <json>` 在冻结源码中执行五项 Gallery 实际 LibVLC 解码、定位、取消和释放测试。控件测试是 headless，媒体是真实私有后端；与前项真实窗口证据分别记录。

`linux-stock.py --repository <repo> --environment <json>` 使用 Linux 原生发布的独立契约集合导出内容，复用既有 X11 探针，验证未改造的 Player/Developer 各自启动、落盘和确认退出，不依赖 SDK 发现。给定发布目录必须尚无 `pak/`；再次执行应重新准备发布目录。

本机缓存和本地 feed 路径按当前验证机器配置，换机器需要配置实际包源。WSLg 不等于原生 Linux 桌面发行验收；Android 真机、macOS/iOS 与真实手柄单列。
