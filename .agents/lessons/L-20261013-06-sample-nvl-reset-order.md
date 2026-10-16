# Sample NVL reset must precede presenter disposal

Status: candidate  
Source: Sample save loading bug report, 2026-10-13

## Symptom

When the Avalonia Sample loaded another save while NVL text was on screen, old NVL rows could remain visible in the reused game page.

## Root cause

`SampleGameRunner.DisposeAsync` called its page reset callback after disposing `SampleGamePresentation`. Disposing its `AvaloniaDialoguePresenter` unsubscribed from `GamePageViewModel.NvlClearRequested`, so `ResetScenePresentation()` could no longer clear the page's `NvlPresenter`. The reset method also left `IsNvlMode` true, allowing the stale overlay to remain visible.

## Correct approach

After stopping the Engine and suspending persistent replay, reset the page while its presenter is still subscribed, then dispose the presentation. A full scene presentation reset also sets `IsNvlMode` to false.

## Scope

Sample load/restart flows that reuse the shared `GamePage` and its control instances.

## Do not

Do not unsubscribe/dispose a presenter before sending it host reset events that it owns. Do not leave NVL visibility as stale page state across run replacement.

## Evidence and related documents

- `SampleGameRunner.DisposeAsync`
- `GamePageViewModel.ResetScenePresentation`
- `AvaloniaDialoguePresenter` subscribes to `NvlClearRequested` and clears `GamePage.Nvl`.
- `docs/spec/control.md`
