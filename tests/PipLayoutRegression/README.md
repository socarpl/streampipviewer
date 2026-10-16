Run on Windows:

```powershell
dotnet run --project tests/PipLayoutRegression
```

Checks percentage geometry across 4K/1080p and small/empty video areas, legacy configuration migration,
locale-independent persistence, synchronized controls, hex validation, import/export/reload,
and the settings dialog's preview/save/cancel behavior. Uses temporary
test configurations and does not open streams. Produces `settings-preview.png` in the test output folder.
