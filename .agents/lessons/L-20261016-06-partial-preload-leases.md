# 批量预加载必须逐次捕获成功租约

状态：confirmed
来源：docs/design/game-host-refactor.md Step 4

## 现象与根因

先 await Task.WhenAll 再遍历结果登记租约时，只要一个获取任务失败，就不会进入遍历；其他已经成功获取的 AssetHandle 不在清理集合中。

## 发现与验证

检查原 SampleGameResourceScope.OpenAsync 发现此路径。AvaloniaGameResourcesTests.PartialPreloadFailureReleasesSuccessfulAcquisitions 用一次成功获取与一次失败获取，验证成功租约恰好释放一次。

## 正确做法

每个异步获取任务在成功后立即将租约登记到受同步保护的集合。等待全部任务结束后，若有失败，释放所有已登记租约并保留清理错误。重复身份不能覆盖旧租约而不释放。

## 适用范围与禁止做法

适用于任何并行获取拥有释放义务的资源。不要把 WhenAll 失败当成所有子任务均未成功，也不要只清理最终结果数组。
