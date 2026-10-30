# L-20261029-05：存档列表通知不能反复重建已解码缩略图

- 状态：confirmed
- 来源：2026-10-29 Sample 保存卡顿跟进（独立修复）
- 关联：docs/spec/presentation/game-view.md，AvaloniaGameSession，SaveSlotsPageViewModel

## 现象与根因

整表 Clear 后逐项 Add，每次 CollectionChanged 都重建全部行并重新解码图片，60 个槽位会产生二次方增长的重复工作。异步磁盘写入无法避免随后 UI 线程的图片解码卡顿。

## 正确做法与验证

整表准备好后发送一次 Reset；单槽保存使用 Replace，VM 只替换对应行并释放旧图，改名仅更新名称。保存前后只查询目标元数据，自定义存储通过可选 ISaveSlotInfoSource 实现，原 ISaveService 保持兼容。缩略图生成和旧图解码均限制尺寸。

GameThemeTests.Saving_one_slot_preserves_the_other_rows_and_decoded_previews 验证其他行与 Bitmap 身份保持不变；FileSaveServiceTests.SingleSlotLookupMatchesListMetadataForOccupiedEmptyAndCorruptSlots 验证单槽查询语义。禁止在每次集合通知中无条件 Clear/Rebuild 带解码资源的全部行。
