# Headless Dispatch must await an async result

Status: confirmed
Source: F-20261013-01-editor-cli-operations / ScreenshotEntryTests

## Evidence and cause

Avalonia.Headless 12.0.5 exposes Dispatch(Action), Dispatch<T>(Func<T>) and Dispatch<T>(Func<Task<T>>), but no Dispatch(Func<Task>). An async lambda with no return value can bind to Action and run as async void. Screenshot tests appeared green without producing the PNG written after their first await. The installed Avalonia.Headless.xml lists the overloads explicitly; switching to Dispatch<bool>(async () => { await action(); return true; }, token) makes the outer test await all assertions and file output.

## Rule

For asynchronous Headless session actions, explicitly select the async generic result overload and return a result. A helper taking Func<Task> may wrap it in Dispatch<bool>. Keep await using for session disposal as required by L-20261011-01. Verify a durable artifact or a post-await assertion actually ran; a green test alone is insufficient when async void may have been selected.

Never pass an unqualified no-result async lambda to Dispatch. This applies to any Avalonia.Headless test with awaits inside the dispatch action. Existing theme tests need a separate follow-up audit; this feature corrects its own new screenshot tests.
