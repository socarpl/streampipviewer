Run on Windows:

```powershell
dotnet run --project tests/ReconnectRegression
```

Exercises the real viewer's context menu and main/PIP targeting before and after swaps.
The form is disabled and placed off-screen; stream startup is suppressed. No camera
connections are made, and the test uses its own temporary configuration file.
