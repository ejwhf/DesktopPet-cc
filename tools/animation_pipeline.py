#!/usr/bin/env python3
"""Generate and validate the v0.2 frame-clip animation asset set."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import json
from pathlib import Path, PurePosixPath
import shutil
import sys

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from tools.asset_pipeline import _draw_index_badge, alpha_bounds
from tools.png_codec import PngError, PngImage, paste_rgba, read_png, resize_rgba, write_png


@dataclass(frozen=True)
class ClipSpec:
    clip_id: str
    sources: tuple[str, ...]
    durations_ms: tuple[int, ...]
    loop: bool
    fallback_pose_id: str
    anchor_kind: str
    interruptible: bool = True

    @property
    def frame_count(self) -> int:
        return len(self.sources)


def repeated(value: int, count: int) -> tuple[int, ...]:
    return (value,) * count


CLIPS: tuple[ClipSpec, ...] = (
    ClipSpec("boot.enter", ("peek.screen_left",) * 3 + ("idle.neutral",) * 5,
             repeated(83, 8), False, "peek.screen_left", "ground", False),
    ClipSpec("idle.loop", ("idle.neutral", "idle.alt", "idle.neutral", "idle.blink_smile",
                           "idle.blink_smile", "idle.alt", "idle.neutral", "idle.neutral"),
             (500, 83, 83, 167, 83, 83, 83, 918), True, "idle.neutral", "ground"),
    ClipSpec("sit.enter", ("idle.neutral",) * 3 + ("sit.edge_idle",) * 3 + ("sit.edge_alt",) * 2,
             repeated(83, 8), False, "sit.edge_idle", "ground"),
    ClipSpec("sit.loop", ("sit.edge_idle", "sit.edge_alt") * 3,
             repeated(250, 6), True, "sit.edge_idle", "seat"),
    ClipSpec("sit.exit", ("sit.edge_alt",) * 3 + ("sit.edge_idle",) * 2 + ("idle.neutral",) * 3,
             repeated(83, 8), False, "idle.neutral", "ground"),
    ClipSpec("read.enter", ("idle.neutral",) * 2 + ("read.small",) * 3 + ("read.hold",) * 3,
             repeated(83, 8), False, "read.hold", "ground"),
    ClipSpec("read.loop", ("read.hold", "read.hold", "read.small", "read.hold") * 2,
             repeated(150, 8), True, "read.hold", "ground"),
    ClipSpec("read.surprise", ("read.hold", "read.surprised", "read.surprised",
                               "read.surprised", "read.hold", "read.hold"),
             repeated(83, 6), False, "read.surprised", "ground", False),
    ClipSpec("read.exit", ("read.hold",) * 3 + ("read.small",) * 2 + ("idle.neutral",) * 3,
             repeated(83, 8), False, "idle.neutral", "ground"),
    ClipSpec("sleep.enter", ("idle.neutral",) * 3 + ("sleep.seated",) * 5,
             repeated(83, 8), False, "sleep.seated", "floor"),
    ClipSpec("sleep.loop", ("sleep.seated", "sleep.seated_alt") * 3,
             (333, 333, 334, 333, 333, 334), True, "sleep.seated", "floor"),
    ClipSpec("sleep.exit", ("sleep.seated_alt",) * 3 + ("sleep.seated",) * 2 + ("idle.neutral",) * 3,
             repeated(83, 8), False, "idle.neutral", "ground"),
    ClipSpec("heart.play", ("idle.neutral", "heart", "heart", "heart.raise", "heart.raise",
                            "heart.raise", "heart", "heart", "idle.neutral", "idle.neutral"),
             repeated(83, 10), False, "heart", "ground", False),
    ClipSpec("wave.play", ("idle.neutral", "wave.single_b", "wave.single_a", "cheer.both_hands",
                           "wave.single_a", "wave.single_b", "wave.single_a", "cheer.both_hands",
                           "wave.single_a", "wave.single_b", "idle.neutral", "idle.neutral"),
             repeated(83, 12), False, "wave.single_a", "ground", False),
    ClipSpec("write.enter", ("idle.neutral",) * 4 + ("write.prone",) * 6,
             repeated(83, 10), False, "write.prone", "floor"),
    ClipSpec("write.loop", ("write.prone",) * 8,
             repeated(83, 8), True, "write.prone", "floor"),
    ClipSpec("write.exit", ("write.prone",) * 6 + ("idle.neutral",) * 4,
             repeated(83, 10), False, "idle.neutral", "ground"),
    ClipSpec("drag.loop", ("falling.reach",) * 6,
             repeated(83, 6), True, "falling.reach", "pivot"),
    ClipSpec("fall.play", ("falling.reach",) * 8,
             repeated(83, 8), False, "falling.reach", "pivot", False),
    ClipSpec("landing.play", ("falling.reach",) * 3 + ("idle.neutral",) * 3,
             repeated(83, 6), False, "idle.neutral", "ground", False),
)

EXPECTED_FRAME_COUNT = 160


def _load_pose_manifest(asset_root: Path) -> dict[str, dict[str, object]]:
    path = asset_root / "manifest" / "assets.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    return {entry["id"]: entry for entry in document["assets"]}


def generate(asset_root: Path) -> None:
    poses = _load_pose_manifest(asset_root)
    clips: list[dict[str, object]] = []
    total = 0
    for spec in CLIPS:
        directory = asset_root / "animations" / spec.clip_id
        directory.mkdir(parents=True, exist_ok=True)
        frames: list[dict[str, object]] = []
        for index, (pose_id, duration_ms) in enumerate(zip(spec.sources, spec.durations_ms)):
            pose = poses[pose_id]
            source = asset_root / str(pose["file"])
            target = directory / f"{index:03d}.png"
            shutil.copyfile(source, target)
            frame: dict[str, object] = {
                "file": target.relative_to(asset_root).as_posix(),
                "durationMs": duration_ms,
                "visualBounds": pose["visualBounds"],
                "anchor": _select_anchor(pose, spec.anchor_kind),
                "interruptible": spec.interruptible,
                "sourcePoseId": pose_id,
            }
            frames.append(frame)
            total += 1
        clips.append({
            "id": spec.clip_id,
            "loop": spec.loop,
            "fallbackPoseId": spec.fallback_pose_id,
            "frames": frames,
        })
    if total != EXPECTED_FRAME_COUNT:
        raise ValueError(f"expected {EXPECTED_FRAME_COUNT} frames, generated {total}")
    manifest = {
        "schemaVersion": 1,
        "canvasSize": [512, 512],
        "defaultFrameDurationMs": 83,
        "frameCount": total,
        "clips": clips,
    }
    path = asset_root / "manifest" / "animations.json"
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def _select_anchor(pose: dict[str, object], kind: str) -> list[float]:
    field = {
        "ground": "groundAnchor",
        "seat": "seatAnchor",
        "floor": "floorAnchor",
        "pivot": "pivot",
    }[kind]
    anchor = pose.get(field) or pose.get("groundAnchor") or pose.get("floorAnchor") or pose["pivot"]
    return [float(anchor[0]), float(anchor[1])]  # type: ignore[index]


def _safe_path(asset_root: Path, value: object) -> Path | None:
    if not isinstance(value, str) or not value or "\\" in value:
        return None
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts or "." in path.parts:
        return None
    return asset_root.joinpath(*path.parts)


def lint(asset_root: Path) -> list[str]:
    errors: list[str] = []
    path = asset_root / "manifest" / "animations.json"
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        return [f"cannot load animations manifest: {error}"]
    if document.get("schemaVersion") != 1:
        errors.append("schemaVersion must equal 1")
    if document.get("canvasSize") != [512, 512]:
        errors.append("canvasSize must equal [512, 512]")
    clips = document.get("clips")
    if not isinstance(clips, list) or len(clips) != len(CLIPS):
        return errors + [f"expected {len(CLIPS)} clips"]
    seen: set[str] = set()
    frame_count = 0
    for clip in clips:
        clip_id = clip.get("id") if isinstance(clip, dict) else None
        if not isinstance(clip_id, str) or not clip_id or clip_id in seen:
            errors.append(f"invalid or duplicate clip id {clip_id!r}")
            continue
        seen.add(clip_id)
        frames = clip.get("frames")
        if not isinstance(frames, list) or not frames:
            errors.append(f"{clip_id}: frames must be non-empty")
            continue
        for index, frame in enumerate(frames):
            frame_count += 1
            prefix = f"{clip_id}[{index}]"
            if not isinstance(frame, dict):
                errors.append(f"{prefix}: frame must be an object")
                continue
            target = _safe_path(asset_root, frame.get("file"))
            if target is None or not target.is_file():
                errors.append(f"{prefix}: frame file is missing or unsafe")
                continue
            try:
                image = read_png(target)
            except PngError as error:
                errors.append(f"{prefix}: cannot read PNG: {error}")
                continue
            if image.mode != "RGBA" or (image.width, image.height) != (512, 512):
                errors.append(f"{prefix}: frame must be 512x512 RGBA")
            bounds = alpha_bounds(image, 2)
            if bounds is None or bounds.xywh() != frame.get("visualBounds"):
                errors.append(f"{prefix}: visualBounds mismatch")
            duration = frame.get("durationMs")
            if not isinstance(duration, int) or isinstance(duration, bool) or duration <= 0:
                errors.append(f"{prefix}: invalid durationMs")
            anchor = frame.get("anchor")
            if not (isinstance(anchor, list) and len(anchor) == 2 and
                    all(isinstance(value, (int, float)) and 0 <= value <= 1 for value in anchor)):
                errors.append(f"{prefix}: invalid anchor")
    if frame_count != EXPECTED_FRAME_COUNT or document.get("frameCount") != EXPECTED_FRAME_COUNT:
        errors.append(f"expected exactly {EXPECTED_FRAME_COUNT} frames, found {frame_count}")
    if seen != {spec.clip_id for spec in CLIPS}:
        errors.append("clip ids differ from the v0.2 contract")
    return errors


def write_prompt_record(asset_root: Path) -> None:
    path = asset_root / "animation-prompts" / "README.md"
    path.parent.mkdir(parents=True, exist_ok=True)
    rows = [
        "# Animation generation prompt record",
        "",
        "Built-in imagegen is the intended source for replacement in-between frames.",
        "Every generated sheet is treated as an identity-preserve edit of the listed source poses.",
        "Generated frames replace matching files under `assets/animations/` only after visual approval.",
        "",
        "Shared constraints: fixed camera and scale; preserve face, round glasses, black bob haircut, navy robe, cuff pattern, collar, shoes and props; transparent background; no text, watermark, shadow, extra character, limb or object.",
        "",
        "| Clip | Frames | Source poses |",
        "|---|---:|---|",
    ]
    rows.extend(
        f"| `{spec.clip_id}` | {spec.frame_count} | {', '.join(f'`{pose}`' for pose in dict.fromkeys(spec.sources))} |"
        for spec in CLIPS
    )
    path.write_text("\n".join(rows) + "\n", encoding="utf-8")


def generate_contact_sheets(asset_root: Path) -> None:
    document = json.loads((asset_root / "manifest" / "animations.json").read_text(encoding="utf-8"))
    output_root = asset_root / "review" / "animations"
    output_root.mkdir(parents=True, exist_ok=True)
    cell_size, columns = 128, 4
    for clip in document["clips"]:
        rows = (len(clip["frames"]) + columns - 1) // columns
        width, height = columns * cell_size, rows * cell_size
        canvas = bytearray(width * height * 4)
        for y in range(height):
            for x in range(width):
                value = 238 if ((x // 16) + (y // 16)) % 2 == 0 else 210
                offset = (y * width + x) * 4
                canvas[offset : offset + 4] = bytes((value, value, value, 255))
        for index, frame in enumerate(clip["frames"]):
            sprite = read_png(asset_root / frame["file"])
            reduced = resize_rgba(sprite, cell_size, cell_size)
            paste_rgba(
                canvas, width, height, reduced,
                (index % columns) * cell_size,
                (index // columns) * cell_size,
            )
            _draw_index_badge(
                canvas, width, height,
                (index % columns) * cell_size + 4,
                (index // columns) * cell_size + 4,
                f"{index + 1:02d}",
            )
        write_png(
            output_root / f"{clip['id']}.png",
            PngImage(width, height, "RGBA", bytes(canvas), 6),
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("generate", "contact-sheets", "lint", "all"))
    parser.add_argument("--asset-root", type=Path, default=Path("assets"))
    args = parser.parse_args()
    if args.command in ("generate", "all"):
        generate(args.asset_root)
        write_prompt_record(args.asset_root)
    if args.command in ("contact-sheets", "all"):
        generate_contact_sheets(args.asset_root)
    if args.command in ("lint", "all"):
        errors = lint(args.asset_root)
        if errors:
            print("\n".join(f"ERROR: {error}" for error in errors), file=sys.stderr)
            return 1
        print(f"animation lint passed ({len(CLIPS)} clips, {EXPECTED_FRAME_COUNT} frames)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
