# DesktopPet Windows integration smoke test

This dependency-free `net8.0-windows` console starts an already-built desktop pet, waits for the
visible top-level window owned by that exact PID, checks its borderless/layered/topmost/tool-window
styles, and sends `WM_NCHITTEST` to:

- a transparent client-area corner, which must return `HTTRANSPARENT`;
- an opaque pixel selected from `idle.neutral`'s production hit mask, which must not return
  `HTTRANSPARENT`.

The child process is forcibly terminated in a `finally` block, including on assertion failures.
Exit any existing DesktopPet instance first because the application enforces one instance per
interactive session.

```powershell
dotnet run --project tests/DesktopPet.Integration/DesktopPet.Integration.csproj -- `
  --exe src/DesktopPet.App/bin/Release/net8.0-windows/DesktopPet.exe `
  --manifest assets/manifest/assets.json
```
