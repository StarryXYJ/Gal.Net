# 跨平台后端接入

本页覆盖引擎后端，不创建 Avalonia Android/iOS App 模板。Core/Runtime 的执行和存档逻辑、PAK 格式、PCM 混音/DSP 继续共享；平台宿主选择资源入口、媒体实现和原生库部署。普通目录中的存档/变量/进度继续使用现有 .NET 文件实现，由宿主传入沙盒可写目录，不再包装一套 File API。

## 从包内流读取 PAK

```csharp
var provider = new PakStreamProvider(
    new Dictionary<string, Func<Stream>>
    {
        ["assets"] = () => OpenPackagedResource("Assets/assets.pak")
    }, resourceTypes);
using var assets = new AssetManager([provider]);
await using var files = new AssetFileMaterializer(assets, applicationCacheDirectory);
```

`OpenPackagedResource` 是宿主提供的普通委托，可调用 Avalonia AssetLoader、Android AssetManager 或其他资源 API。每次返回独立、位于 PAK 起点的新可读流；provider 负责关闭流，不要求流可寻址，也不枚举包内目录。此入口沿用当前 Archive 的全量内存加载，不是大 PAK 的随机访问或流式解压方案。媒体实体化始终来自 AssetManager acquire 的字节，并保留文件租约。

Graph/settings/I18n 的默认 `InstalledGameContentProvider` 仍读取展开后的安装目录。可将已验证游戏内容安装到应用私有目录再使用该实现；直接从应用包读取这些内容需实现现有 `IGameContentProvider`。新增 PAK 流入口不表示整个安装内容加载器已变成流接口。

## 构建原生音频

桌面 CMake 构建示例（在所需架构的编译器环境执行）：

```sh
cmake -S src/Infrastructure/GalNet.Media.Miniaudio/native -B out/native -DCMAKE_BUILD_TYPE=Release
cmake --build out/native --config Release
cmake --install out/native --config Release --prefix out/native/install
```

产物为 Windows 的 `galnet_miniaudio.dll`、Linux 的 `libgalnet_miniaudio.so`、macOS 的 `libgalnet_miniaudio.dylib`。Windows 默认托管构建也可自动调用 MSVC，按目标 RID 选择 win-x64/win-arm64，原生输出按 RID 隔离。脚本从已发现的 VS 实例中选择目标架构的编译器、头文件和静态运行库齐全的最高版本工具集，并用 `-vcvars_ver` 显式初始化；不要求默认最新工具集也安装该架构。自备动态库时传入绝对路径：

```sh
dotnet build path/to/Host.csproj -p:GalNetMiniaudioNativePath=/absolute/path/libgalnet_miniaudio.so
```

自备路径只关闭默认自动原生编译，并把该动态库复制到普通构建/发布输出；库的 CPU 架构和系统 ABI 必须与目标一致。Miniaudio 原生 ABI 为 2，托管和原生部分应同时更新。构建禁用 null 音频设备，无法打开真实设备时按初始化失败报告。

Android 使用 NDK CMake 工具链，例如：

```sh
cmake -S src/Infrastructure/GalNet.Media.Miniaudio/native -B out/native-android \
  -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-23 -DCMAKE_BUILD_TYPE=Release
cmake --build out/native-android
```

平台宿主将生成的 `.so` 声明为对应 ABI 的 `AndroidNativeLibrary`；普通 Content 复制不替代 APK 内原生库打包。系统音频焦点和路由事件仍由宿主接入。

iOS 在有 Xcode 的 macOS 上构建静态库，设备与模拟器分别构建：

```sh
cmake -S src/Infrastructure/GalNet.Media.Miniaudio/native -B out/native-ios \
  -DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT=iphoneos -DCMAKE_OSX_ARCHITECTURES=arm64 \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=15.0 -DGALNET_MINIAUDIO_STATIC=ON -DCMAKE_BUILD_TYPE=Release
cmake --build out/native-ios --config Release
```

宿主用 `NativeReference` 链接生成的 `.a`，选择 `Kind=Static`、`ForceLoad=true` 并带上 CoreFoundation/CoreAudio/AudioToolbox frameworks；构建托管适配器时传入全局属性 `GalNetMiniaudioStaticLink=true`，使导入绑定 `__Internal`。动态和静态托管变体应使用独立输出目录。宿主负责 AVAudioSession 策略，先激活系统音频会话再恢复设备。这些移动端命令是接入路径，当前 Windows 验证环境未执行其编译、签名和实机播放。

## 系统中断与视频替换

`PcmAudioPlaybackHost` 可通过 `SupportsOutputSuspension` 检查能力，在控制线程调用 `SuspendOutput` / `ResumeOutput`。Miniaudio 暂停会关闭设备并等待回调结束，恢复重新打开当前默认设备；保留源、租约、剧情 Pause 和 DSP，失败可重试。后台读取只继续到有界 PCM 缓冲填满。宿主负责将系统中断/路由事件映射到这些调用，并另外处理剧情暂停与恢复点；后端不会调用终态 `Engine.StopAsync`。

剧情视频可通过 `AvaloniaGamePresentationOptions.CreateVideoPresenter` 提供任意 `IVideoPresenter`，返回实例由呈现会话拥有并优先异步释放。该 presenter 需要将画面接入宿主场景组合，不能仅创建一个脱离图层的播放器。默认仍为 LibVLC CPU 像素输出，不宣称已经支持移动端硬件解码与零拷贝。需要继续用 LibVLC 时，可用 `LibVlcRuntime(() => CreatePlatformLibVlc())` 完成平台初始化并创建实例；运行时拥有返回值，工厂失败保留 InitializationError。Gallery 当前仍使用默认 LibVLC 服务和控件，不会随剧情视频工厂一起替换。

## 桌面 VLC 配置与会话所有权

剧情视频、Gallery 播放/缩略图和 Editor 音频预览通过 `GalNet.Media.LibVlc.LibVlcFactory` 创建会话。组合根可以在 `AddAvaloniaGameViewPages` / `AddEditorServices` 前注册自己的 singleton factory；默认注册使用 TryAdd，不覆盖宿主选择。工厂只集中原生初始化和加载配置，每个调用方仍释放自己的 LibVLC/MediaPlayer，Gallery 与剧情播放器不互相接管。

```csharp
services.AddSingleton(new LibVlcFactory(new LibVlcOptions
{
    NativeLibraryDirectory = absoluteNativeDirectory,
    PluginDirectory = Path.Combine(absoluteNativeDirectory, "plugins")
}));
```

显式路径必须绝对化；私有目录和 UseSystemLibraries 二选一。Windows 默认使用托管入口目录下的 libvlc/<RID>，不依赖工作目录。缺库、缺插件目录或初始化错误包含 RID、配置路径与原始原因，不自动改用已安装 VLC。工厂暴露 NativeVersion / InitializationError；LibVlcRuntime 保留初始化失败以供宿主报告。

原生库加载和 VLC_PLUGIN_PATH 为进程级配置，首次初始化受锁保护；后续会话必须使用相同配置，禁止运行中切换另一套库/插件。插件目录保留至进程结束，因为 VLC 可能延迟加载插件。剧情像素输出继续使用 --avcodec-hw=none。

Linux 默认明确选择系统 loader，作为开发验证基线。LibVLCSharp 的 Core.Initialize(directory) 参数不支持 Linux；显式私有目录会报错，不能把传路径当成私有部署成功。应用私有 Linux 动态依赖与启动加载路径、macOS 原生部署/.app、移动绑定仍需后续阶段实现和实测。当前只有 Windows 的真实视频、Gallery 与 Editor 预览证据，不据此声明三平台应用可用。

## AOT 与验证范围

静态音频回调和源生成 P/Invoke 已消除该原生边界的动态委托 thunk；Core/Runtime、内置契约、Assets 和文件持久化的玩家 JSON 路径也已接入源生成元数据。状态数组、自定义资源、配置和字段 schema 均提供显式注册入口，普通 JIT 保留反射兼容路径。现有 Headless Sample 用标准 PublishAot 选项完成 Windows x64 原生发布并运行自检，确认动态代码/反射 JSON 均关闭；最终 ILLink/ILC 警告作为错误处理。操作和验收范围见 [NativeAOT 发布与扩展契约](native-aot.md)。

桌面自检覆盖引擎和文件后端，不代表 Avalonia/Skia、LibVLC、原生设备或 Editor/CLI 作者编译器全部支持 AOT。移动端最终发布闭包、原生打包和实机生命周期仍需对应环境验收。不要用全局关闭裁剪或吞掉 IL 警告作为支持证据。

本轮本地证据：资源测试 76 项、音频普通测试 26 项、真实 Windows x64 设备测试 2 项、视频工厂/失败清理测试 5 项及架构测试 52 项通过；CMake 动态库在 Windows x64 构建成功。Core/Runtime 的 AOT/裁剪分析构建通过但仍报告 IL2026/IL3050 等警告。补装 ARM64 MSVC 14.44 后，Windows ARM64 原生库交叉编译通过，确认 PE Machine 为 `0xAA64` 且 8 个原生 ABI 导出齐全；`GalNet.Media.Miniaudio` 的 Release / `-r win-arm64` 托管及原生组合构建也通过，0 错误。并验证 x64 仍选用完整的 MSVC 14.51，PE Machine 为 `0x8664`。Windows ARM64 尚未做实机运行验收。`.github/workflows/backend-portability.yml` 增加 Windows/Linux/macOS 的资源、原生解码和 CMake 验证任务，远端任务尚未在本地执行。Android/iOS 原生打包、系统中断、移动 GPU/Shader 和移动宿主 AOT 发布验收仍待对应环境完成。
