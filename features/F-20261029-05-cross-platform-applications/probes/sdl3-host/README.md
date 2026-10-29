# 正式 GameHost SDL3 输入探针

此探针调用真实 `SdlGamepadSource`、`AvaloniaGamepadInput` 定时器及 `PlayerInputSettings`，从固定 NuGet 包部署私有 SDL 3.4.18，不使用 P0 的输入专用自建 native。虚拟设备接口只负责测试输入注入，不代表真实手柄验收。

在仓库根以 SDK 10.0.401 构建，输出目录应为本次自有目录：

```powershell
$probe = 'features/F-20261029-05-cross-platform-applications/probes/sdl3-host'
dotnet restore "$probe/HostProbe.csproj" --locked-mode -p:GalNetBuildMiniaudio=false
dotnet publish "$probe/HostProbe.csproj" -c Release -r win-x64 --self-contained true --no-restore -p:GalNetBuildMiniaudio=false -o out/sdl-host/app-win
python "$probe/verify-host.py" --app out/sdl-host/app-win --output out/sdl-host/windows-results
```

Linux 发布将 `-r` 改为 `linux-x64`；在 WSL/Linux 中用 Python 3.11+ 调用 `verify-host.py`。Windows 宿主可交叉发布，Linux 的自包含运行不需要 SDK。验证器复制到含空格的隔离目录，运行三次完整检查，再移走加载入口原生库，确认 UI 可启动、手柄不可用且原因明确；最后自动清理副本。Linux 使用 ext4 /tmp 与进程级 d3d12 默认选择，Windows 使用输出父目录。原生 UI 验证要避免与其他会切换焦点的手工/自动操作同时进行。

完整检查每次 40 项，覆盖：

- 包内原生文件与 `ThirdParty/SDL3/native-runtime.json` SHA256 一致，SDL 与 SDL3-CS 许可随包。
- 正式后端初始化不启用 SDL video/audio；设备发现、位置按钮、归一化轴、热插拔和新连接身份。
- 主窗口实际失活立即取消；后台不发布按钮；保持按住返回焦点不会误触发，松开后重新武装。
- 中断使用 Cancel，不提交虚假的释放；保持状态不重复发布边沿。
- 同线程多租约释放、完整关闭和重初始化；错误线程更新被拒绝；外部 SDL 输入所有权不被接管或退出。
- 自有输入事件不积累，无关 SDL user event 保留。

探针要求没有物理标准手柄，避免把实际设备操作混入可控虚拟测试；已连接设备会使探针明确失败，不做实机通过推断。真实设备、macOS 和移动初始化仍须独立验收。

最终证据：`out/cross-platform-validation/20261030-sdl3/windows-owned/`、`linux-owned/`；保留 `host-windows-first.json` 的输入队列失败、`input-final.trx` 的无主题测试夹具失败，修正后原生/共享测试通过。
