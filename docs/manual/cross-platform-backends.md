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

产物为 Windows 的 `galnet_miniaudio.dll`、Linux 的 `libgalnet_miniaudio.so`、macOS 的 `libgalnet_miniaudio.dylib`。Windows 默认托管构建也可自动调用 MSVC，按目标 RID 选择 win-x64/win-arm64，原生输出按 RID 隔离。自备动态库时传入绝对路径：

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

## AOT 与验证范围

静态音频回调和源生成 P/Invoke 消除了该原生边界的动态委托 thunk 需求，但不代表整个引擎已完成 AOT/裁剪适配。当前代码与 Core 分析器检查确认仍需处理：

- GameState 泛型状态数组的反射 JSON 编解码，以及项目自定义状态的序列化元数据注册。
- AssetMetaCodec 按运行时 Type 序列化/构造 metadata、配置 DTO 与 ValueDescriptor 的反射 JSON 路径。
- 作者 schema 属性反射的裁剪注解及生成元数据边界；应区分编辑器路径与实际玩家运行闭包。
- NCalc、LibVLCSharp、Avalonia/Skia 和项目扩展的最终发布闭包，仍需平台构建及运行验证。

不要用全局关闭裁剪或吞掉 IL 警告作为 AOT 支持证据。源生成 JSON/显式 codec 应与自定义状态、资源类型和项目配置的注册边界一起设计；本轮不修改正在进行的递归属性/schema 改动。

本轮本地证据：资源测试 76 项、音频普通测试 26 项、真实 Windows x64 设备测试 2 项、视频工厂/失败清理测试 5 项及架构测试 52 项通过；CMake 动态库在 Windows x64 构建成功。Core/Runtime 的 AOT/裁剪分析构建通过但仍报告 IL2026/IL3050 等警告。Windows ARM64 交叉编译已尝试，当前环境缺少 ARM64 `libcmt.lib`，未完成 DLL 链接，不能算已验证。`.github/workflows/backend-portability.yml` 增加 Windows/Linux/macOS 的资源、原生解码和 CMake 验证任务，远端任务尚未在本地执行。Android/iOS 原生打包、系统中断、移动 GPU/Shader 和全引擎 AOT 发布验收仍待对应环境完成。
