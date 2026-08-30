# Asset tooling

The tooling uses only Python's standard library, so a clean Python 3.11+
installation is enough. Run commands from the repository root.

## Build from the transparent action sheet

```powershell
python tools/asset_pipeline.py extract assets/sheets/character-actions-rgba-v2.png
python tools/asset_pipeline.py masks
python tools/asset_pipeline.py contact-sheet assets/review/contact-sheet.png
python tools/asset_pipeline.py lint
```

`extract` expects an RGBA sheet containing 18 subjects arranged in rows of
4, 4, 4, 4, and 2. It finds the 18 large alpha-connected subject components,
assigns small disconnected effects to their nearest subject, and copies only
the pixels owned by each group. It then detects the largest connected face-skin region,
uses `idle.neutral` as the comparison master, normalises every face to 98 px,
and places the result on a 512×512 transparent canvas. It then scales the 12
complete standing poses around their contact anchors to a common 340 px visible
height. This second pass intentionally prioritises consistent animated stature
over identical final head size. Standing actions share their ground baseline;
seated/floor/falling actions use their designated anchor convention. The
command prints every detected face width, scale, and measured visual bound for
review.

The default 98 px target keeps every normalised action inside a 48 px safety
margin. Change it only after reviewing every output:

```powershell
python tools/asset_pipeline.py extract INPUT.png --target-face-width 96
```

`extract` overwrites generated sprites and the manifest. `masks` overwrites
generated hit masks using alpha threshold 16. `contact-sheet` creates an
opaque checkerboard review image with numbered cells; it is not a runtime
asset.

For the common post-extraction build gate, use:

```powershell
python tools/asset_pipeline.py all
```

## Tests

```powershell
python -m unittest discover -s tests -v
```

## Frame animation assets

Generate the v0.2 contract (20 clips / 160 frames), review sheets, prompt record,
and manifest from the approved pose masters:

```powershell
python tools/animation_pipeline.py all --asset-root assets
```

The runtime reads `assets/manifest/animations.json`. Each frame keeps its own
duration, visual bounds, contact anchor, interruptibility and source pose. The
18-pose manifest remains an independent fallback if this animation set is
missing or invalid. Review sheets are written to `assets/review/animations/`.

The linter validates the 18 required IDs, manifest field types, safe relative
paths, true RGBA sprite encoding, dimensions, alpha, measured visual bounds,
48 px safety margins, normalised anchors, and exact hit-mask/alpha agreement.
Its exit code is non-zero on any release-blocking issue.
