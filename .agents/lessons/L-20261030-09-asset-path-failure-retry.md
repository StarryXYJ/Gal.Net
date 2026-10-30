# 空句柄不等于资源身份失效

状态：confirmed，2026-10-30。
来源：F-20261030-01-project-quality / P7。

新增按路径共享失败测试复现：12 个请求共享一次解码后，得到两次 DecodeFailure。原 AcquireByPathAsync 将缓存 ID 返回的所有 null 句柄都视为旧路径映射失效，删除映射并重查；已经找到文件但读取/解码失败也因此重复执行。ID 请求没有这次重查，所以只测 ID 并发会漏掉问题。

修正：共享 completion 返回是否找到文件，资源所有权仍在原缓存；路径调用只有未找到文件才重查映射。不要把回退用的 null 当成具体失败原因，也不要增加常驻失败缓存来掩盖重复工作。显式后续请求仍可重试恢复，真正 ID 消失时仍要重新按路径解析。

验证：Shared_failure_is_reported_once_and_can_be_retried_successfully 的路径分支修正前稳定得到 2 次、修正后 1 次；额外测试覆盖旧 ID 被替换、暖路径读取失败只调用一次、随后恢复。Assets 全量通过。失败证据 out/project-quality-p7-path-failure-before.log，后续证据 out/project-quality-p7-assets.log。

适用于带别名/路径缓存和共享加载的资源获取：区分身份查找与读取/转换结果，不能用诊断去重掩盖重复执行。公共空结果、取消与引用计数契约保持原有职责。
