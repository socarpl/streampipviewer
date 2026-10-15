Run on Windows:

```powershell
dotnet run --project tests/StreamHostWindowRegression
```

Checks actual native window styles for embedded and standalone stream hosts, including
handle recreation and embedding. Creates hidden forms without starting playback or
connecting to cameras. Requires the native LibVLC dependencies supplied by the host project.
