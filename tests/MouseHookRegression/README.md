Run on Windows with an interactive desktop:

```powershell
dotnet run --project tests/MouseHookRegression
```

This dependency-free regression harness briefly shows small test windows without activating them. It checks occlusion by windows in the same and a separate process, input over an embedded cross-process stream window, normal right-click/double-click actions, interrupted double-clicks, and queued actions when another window covers the viewer. It invokes the hook handler directly without injecting mouse input or starting camera streams.
