# DesktopPet.Core

UI-independent .NET 8 library for the desktop pet.

- `Assets`: the `assets.json` model, compact `[x,y]` geometry converters, stable ids for all
  eighteen poses, and cross-reference validation.
- `Behavior`: a caller-clocked weighted state machine. Construct it with a fixed seed to replay
  the same weighted choices; explicit transitions enforce cooldown, interruptibility, and the
  priority ordering encoded by `TransitionCause`.
- `Timeline`: MVP whole-bitmap animation (offset, rotation, breathing scale, opacity), a monotonic
  sampler, and the planned startup/idle/sit/read/sleep/heart/wave/write/drag/fall sequences.
- `Configuration`: schema-v2 settings, legacy/v1 migrations, validation, `.bak` recovery, and
  same-directory atomic save.

The app owns the monotonic clock. Pass elapsed milliseconds from `Stopwatch`, never wall-clock
timestamps, to state-machine and timeline methods.

Validate the production manifest with:

```powershell
dotnet run --project src/DesktopPet.Core/Smoke/DesktopPet.Core.Smoke.csproj -- `
  assets/manifest/assets.json
```
