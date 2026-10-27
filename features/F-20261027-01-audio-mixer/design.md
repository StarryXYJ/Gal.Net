# 设计

后端设计已实现；2026-10-27 新增 [音频效果创作与目标分析设计](authoring-design.md)，描述用户确认的类型选择、隐藏实例身份、已有实例选择，以及对现有 Layer 选择/分析机制的复用。该补充已实现，见 [创作实现记录](authoring-summary.md)；其验证与原后端验证分开记录。

当前问题：每 Voice 的 LibVLC player 独立输出；启停 EQ 改变原生滤波链。Group 只参与标量音量计算，不能代表实际 Bus。先 Start 后附着效果也不能保证首帧处理。

采用统一 float32/48 kHz/stereo PCM 混音。跨平台托管混音/DSP 与平台设备/解码分离。miniaudio 0.11.23 使用薄 C ABI 封装，隐藏原生结构布局，Windows 首期构建 DLL；固定第三方源版本和 SHA256，MIT/Public Domain 授权随源码保存。设备回调只消费已准备的 PCM，后台解码至有界 SPSC 缓冲；不在回调读文件、等待剧情锁或调用完成订阅者。

实例拥有资源、gain、淡入/淡出、有限播放次数和完成处理。Track 保留容量/队列/溢出规则，新增顺序/随机和队列循环策略；随机状态随 Track 存档。Bus 替代原 Group，每 Track 可进入一个 Bus 或直接进入 Master。数据流：实例增益/包络 → Track 汇总 → Track 效果链/音量 → Bus 汇总 → Bus 效果链/音量 → Master 音量与硬限幅。Bus 效果执行一次，空输入仍运行以保留尾音。

SEffect 支持 Track/Bus。首次 apply 定位链顺序，同实例更新不改变顺序；真正双二阶高低通（cutoffHz/Q）、算法混响（roomSize/decaySeconds/damping/mix）、gain。参数和效果启停平滑；移除默认平滑旁路，reset/读档清除 DSP 尾音。停止某个音源不清除目标链。旧 attenuationDb 不再接受，示例显式迁移。

Runtime 只持久化逻辑播放、目标效果和作者混音状态，后端 Configure/ApplyMix/Reset 为目标级命令，不再把 Track 效果复制给每 Voice。旧音频状态表版本提升，拒绝旧 payload，不伪装兼容。玩家音量仍是独立的即时偏好投影。

候选：NAudio/WASAPI 能快速接入，但此处选择 miniaudio 设备/解码层，托管 DSP 保持可离线验证且无需依赖第三方绑定结构 ABI。Windows 构建需要 MSVC C++ 工具；发布产物携带 DLL，玩家无需编译器。Linux/macOS 端口只保留接口，不宣称已验证。
