# DesktopPet user installer

This project is a current-user, framework-dependent Windows x64 installer. It embeds the
published portable ZIP as a managed resource and does not require elevation.

Security properties:

- The installation location is fixed to `%LOCALAPPDATA%\Programs\DesktopPet`.
- ZIP entries are checked for rooted paths, traversal, NTFS ADS/device names, duplicate
  destinations, and excessive counts/sizes before being written.
- Updates are staged beside the installation and use a backup/rollback directory.
- Running `DesktopPet` processes must be closed by the user; the installer never kills them.
- Uninstall runs from a copied worker under `%LOCALAPPDATA%\Temp`, strictly validates every
  recursive-delete target, and deliberately preserves `%LOCALAPPDATA%\DesktopPet` settings.

Build from the repository root:

```powershell
.\scripts\build-installer.ps1 -Version 0.3.0
```

The resulting installer requires the .NET 8 Desktop Runtime (x64), just like the portable app.
