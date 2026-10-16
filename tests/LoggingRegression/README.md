# Logging regression checks

Run `dotnet run --project tests/LoggingRegression` with the .NET 8+ SDK on Windows.
No cameras or viewer windows are opened. Temporary files are created below the test
output folder and removed afterward.

Checks startup cleanup, oldest-first deletion, exact filenames, UTF-8, safe exception
diagnostics, 1 MB rollover, separate XML configuration, session collisions, and the
25 MiB folder budget while three child processes write and rotate concurrently.
