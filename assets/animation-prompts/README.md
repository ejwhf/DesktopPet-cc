# Animation generation prompt record

Built-in imagegen is the intended source for replacement in-between frames.
Every generated sheet is treated as an identity-preserve edit of the listed source poses.
Generated frames replace matching files under `assets/animations/` only after visual approval.

The checked-in v0.2.0 frame pack is the conservative fallback set: it expands the
approved 18 poses into the complete 160-file contract without accepting identity-drifted
imagegen output. Repeated source-pose frames are intentional and remain replaceable one
file at a time after a generated 2x2 sheet passes visual review. They provide the runtime,
timing, transition and fallback contract, but are not a claim of artist-approved limb
in-betweens.

Shared constraints: fixed camera and scale; preserve face, round glasses, black bob haircut, navy robe, cuff pattern, collar, shoes and props; transparent background; no text, watermark, shadow, extra character, limb or object.

| Clip | Frames | Source poses |
|---|---:|---|
| `boot.enter` | 8 | `peek.screen_left`, `idle.neutral` |
| `idle.loop` | 8 | `idle.neutral`, `idle.alt`, `idle.blink_smile` |
| `sit.enter` | 8 | `idle.neutral`, `sit.edge_idle`, `sit.edge_alt` |
| `sit.loop` | 6 | `sit.edge_idle`, `sit.edge_alt` |
| `sit.exit` | 8 | `sit.edge_alt`, `sit.edge_idle`, `idle.neutral` |
| `read.enter` | 8 | `idle.neutral`, `read.small`, `read.hold` |
| `read.loop` | 8 | `read.hold`, `read.small` |
| `read.surprise` | 6 | `read.hold`, `read.surprised` |
| `read.exit` | 8 | `read.hold`, `read.small`, `idle.neutral` |
| `sleep.enter` | 8 | `idle.neutral`, `sleep.seated` |
| `sleep.loop` | 6 | `sleep.seated`, `sleep.seated_alt` |
| `sleep.exit` | 8 | `sleep.seated_alt`, `sleep.seated`, `idle.neutral` |
| `heart.play` | 10 | `idle.neutral`, `heart`, `heart.raise` |
| `wave.play` | 12 | `idle.neutral`, `wave.single_b`, `wave.single_a`, `cheer.both_hands` |
| `write.enter` | 10 | `idle.neutral`, `write.prone` |
| `write.loop` | 8 | `write.prone` |
| `write.exit` | 10 | `write.prone`, `idle.neutral` |
| `drag.loop` | 6 | `falling.reach` |
| `fall.play` | 8 | `falling.reach` |
| `landing.play` | 6 | `falling.reach`, `idle.neutral` |
