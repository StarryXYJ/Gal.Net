# Headless session cleanup in asynchronous Avalonia tests

Status: confirmed
Source: F-20261011-01-game-ui-themes

## Symptom and evidence

The UI action reached an assertion, then the test appeared to hang until the blame timeout killed testhost. The same case passed after asynchronous session disposal and fixing the palette comparison. Avalonia.Headless 12.0.5 decompiled implementation confirms Dispatch uses a TaskCompletionSource without RunContinuationsAsynchronously, while synchronous Dispose waits for the session dispatch task.

## Cause and rule

An awaited Dispatch may resume the test continuation inline on the session worker. Synchronous Dispose can then wait on that same worker task. Use await using for HeadlessUnitTestSession in asynchronous tests; never synchronously dispose the session after awaited Dispatch. A hang at a failed assertion can hide the original assertion error.

## Related palette observation

A newly loaded resource dictionary that is not attached to a resource host has unresolved DynamicResource brush colors. Compare semantic Color resources or attach the expected resources before comparing brush colors; do not treat detached transparent brush values as the expected palette.

## Scope

Avalonia.Headless session tests with asynchronous Dispatch. Tests: GameThemeTests. See feature summary for final verification.
