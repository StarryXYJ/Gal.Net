# Do not silently complete dialogue when its Avalonia template is not ready

Status: confirmed  
Source feature: none (Avalonia Sample regression fix)

## Symptom

Starting a new game could briefly show the loading/game page and immediately return to the title page. The Sample log recorded `Game engine flow completed` directly after the first `dialogue.text` entry, without a runtime exception.

## Root cause

`DialoguePresenter.StartAsync()` returned `Task.CompletedTask` when its `PART_Typewriter` template part had not yet been assigned. `DialoguePrimitiveInstance` therefore treated the blocking dialogue as successfully presented and completed it, allowing `GameEngine` to advance through the whole smoke-test graph.

## Correct approach

Before starting typewriter rendering, call `ApplyTemplate()` and require `PART_Typewriter` to exist. A missing required part must fail explicitly rather than being represented as a successful presentation. In the Sample host, only set `IsPlaying` to false when `GameEngine.AdvanceAsync()` returns `false`; `true` means the engine stopped at an interactive boundary.

## Scope

Any Avalonia host that dispatches a blocking dialogue immediately after navigating to a newly-created game page.
