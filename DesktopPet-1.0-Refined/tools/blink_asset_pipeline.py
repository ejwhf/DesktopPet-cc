from __future__ import annotations

import argparse
import hashlib
import json
import math
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


CANVAS_SIZE = (512, 512)
FACE_SEARCH = (196, 168, 316, 260)
BLINK_STAGES = (("40", 0.40), ("75", 0.75), ("closed", 1.0))


@dataclass(frozen=True)
class EyeGeometry:
    name: str
    center_x: int
    center_y: int
    radius_x: int
    radius_y: int
    closure_y: int
    closure_arc: float
    closure_tilt: float

    @property
    def roi(self) -> tuple[int, int, int, int]:
        return (
            self.center_x - self.radius_x - 4,
            self.center_y - self.radius_y - 5,
            self.center_x + self.radius_x + 5,
            self.center_y + self.radius_y + 6,
        )


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def load_rgba(path: Path) -> Image.Image:
    image = Image.open(path).convert("RGBA")
    if image.size != CANVAS_SIZE:
        raise ValueError(f"{path} must be 512x512, found {image.size}")
    return image


def alpha_bounds(image: Image.Image) -> tuple[int, int, int, int]:
    alpha = np.asarray(image, dtype=np.uint8)[:, :, 3]
    ys, xs = np.nonzero(alpha >= 16)
    if len(xs) == 0:
        raise ValueError("image has no visible pixels")
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def luma(rgb: np.ndarray) -> np.ndarray:
    values = rgb.astype(np.float32)
    return (0.2126 * values[..., 0]) + (0.7152 * values[..., 1]) + (0.0722 * values[..., 2])


def darkness_candidates(image: Image.Image) -> list[dict[str, float]]:
    rgba = np.asarray(image, dtype=np.float32)
    brightness = luma(rgba[:, :, :3])
    alpha = rgba[:, :, 3] / 255.0
    left, top, right, bottom = FACE_SEARCH
    candidates: list[dict[str, float]] = []
    for y in range(top + 12, bottom - 12):
        for x in range(left + 12, right - 12):
            patch = brightness[y - 7 : y + 8, x - 7 : x + 8]
            patch_alpha = alpha[y - 7 : y + 8, x - 7 : x + 8]
            yy, xx = np.ogrid[-7:8, -7:8]
            disk = (xx * xx) + (yy * yy) <= 42
            ring = ((xx * xx) + (yy * yy) >= 50) & ((xx * xx) + (yy * yy) <= 85)
            if patch_alpha[disk].mean() < 0.98:
                continue
            score = float(patch[ring].mean() - patch[disk].mean())
            candidates.append({"x": x, "y": y, "score": score})
    candidates.sort(key=lambda item: item["score"], reverse=True)
    selected: list[dict[str, float]] = []
    for candidate in candidates:
        if all(
            ((candidate["x"] - item["x"]) ** 2) + ((candidate["y"] - item["y"]) ** 2) >= 18**2
            for item in selected
        ):
            selected.append(candidate)
        if len(selected) == 8:
            break
    return selected


def detect_eye_geometry(neutral: Image.Image) -> tuple[EyeGeometry, EyeGeometry]:
    candidates = darkness_candidates(neutral)
    screen_left = max(
        (item for item in candidates if 215 <= item["x"] < 260 and 195 <= item["y"] <= 238),
        key=lambda item: item["score"],
    )
    screen_right = max(
        (item for item in candidates if 260 <= item["x"] <= 302 and 185 <= item["y"] <= 230),
        key=lambda item: item["score"],
    )
    # The two eyes are intentionally not mirrored: the screen-left eye is visibly larger and
    # lower in the source perspective. Centers come from the image score above; radii and curve
    # parameters were measured against the coordinate-grid output at native pixels.
    left_eye = EyeGeometry(
        "screen-left",
        int(screen_left["x"]),
        int(screen_left["y"]),
        21,
        19,
        int(screen_left["y"]) + 1,
        5.0,
        2.0,
    )
    right_eye = EyeGeometry(
        "screen-right",
        int(screen_right["x"]),
        int(screen_right["y"]),
        20,
        18,
        int(screen_right["y"]) + 1,
        4.0,
        1.0,
    )
    return left_eye, right_eye


def render_face_grid(image: Image.Image, output: Path) -> None:
    left, top, right, bottom = FACE_SEARCH
    crop = image.crop(FACE_SEARCH).resize(
        ((right - left) * 6, (bottom - top) * 6),
        Image.Resampling.NEAREST,
    )
    draw = ImageDraw.Draw(crop, "RGBA")
    scale = 6
    for x in range(left, right + 1, 4):
        px = (x - left) * scale
        draw.line((px, 0, px, crop.height), fill=(0, 220, 255, 100), width=1)
        if x % 8 == 0:
            draw.text((px + 2, 2), str(x), fill=(0, 255, 255, 255))
    for y in range(top, bottom + 1, 4):
        py = (y - top) * scale
        draw.line((0, py, crop.width, py), fill=(255, 80, 180, 100), width=1)
        if y % 8 == 0:
            draw.text((2, py + 2), str(y), fill=(255, 80, 180, 255))
    output.parent.mkdir(parents=True, exist_ok=True)
    crop.save(output)


def glasses_reference_points(eyes: tuple[EyeGeometry, EyeGeometry]) -> dict[str, tuple[int, int]]:
    left, right = eyes
    return {
        "screenLeftOuter": (left.center_x - left.radius_x - 6, left.center_y - 3),
        "screenLeftTop": (left.center_x, left.center_y - left.radius_y - 5),
        "screenLeftBridge": (left.center_x + left.radius_x + 3, left.center_y - 12),
        "screenLeftBottom": (left.center_x, left.center_y + left.radius_y + 5),
        "screenRightBridge": (right.center_x - right.radius_x - 3, right.center_y - 7),
        "screenRightTop": (right.center_x, right.center_y - right.radius_y - 5),
        "screenRightOuter": (right.center_x + right.radius_x + 6, right.center_y - 3),
        "screenRightBottom": (right.center_x, right.center_y + right.radius_y + 5),
        "noseBridge": (round((left.center_x + right.center_x) / 2), round((left.center_y + right.center_y) / 2) + 6),
    }


def render_roi_debug(
    neutral: Image.Image,
    eyes: tuple[EyeGeometry, EyeGeometry],
    output: Path,
) -> None:
    image = neutral.copy()
    draw = ImageDraw.Draw(image, "RGBA")
    colors = ((0, 220, 255, 255), (255, 80, 180, 255))
    for eye, color in zip(eyes, colors, strict=True):
        draw.rectangle(eye.roi, outline=color, width=2)
        draw.line(
            (
                eye.center_x - 4,
                eye.center_y,
                eye.center_x + 4,
                eye.center_y,
            ),
            fill=color,
            width=1,
        )
        draw.line(
            (
                eye.center_x,
                eye.center_y - 4,
                eye.center_x,
                eye.center_y + 4,
            ),
            fill=color,
            width=1,
        )
        draw.text((eye.roi[0], eye.roi[1] - 12), f"{eye.name} {eye.roi}", fill=color)
    for name, point in glasses_reference_points(eyes).items():
        x, y = point
        draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(80, 255, 80, 255))
        if name == "noseBridge":
            draw.text((x + 4, y), name, fill=(80, 255, 80, 255))
    image.save(output)


def skin_surface(base: np.ndarray, eye: EyeGeometry) -> tuple[np.ndarray, int]:
    height, width = base.shape[:2]
    yy, xx = np.mgrid[0:height, 0:width]
    dx = (xx - eye.center_x) / eye.radius_x
    dy = (yy - eye.center_y) / eye.radius_y
    distance = (dx * dx) + (dy * dy)
    rgb = base[:, :, :3].astype(np.float64)
    skin = (
        (distance >= 1.08)
        & (distance <= 2.20)
        & (base[:, :, 3] >= 250)
        & (rgb[:, :, 0] >= 125)
        & (rgb[:, :, 0] >= rgb[:, :, 1] + 7)
        & (rgb[:, :, 1] >= rgb[:, :, 2] + 3)
    )
    sample_y, sample_x = np.nonzero(skin)
    if len(sample_x) < 35:
        raise ValueError(f"Not enough skin samples around {eye.name}: {len(sample_x)}")
    sx = (sample_x - eye.center_x) / eye.radius_x
    sy = (sample_y - eye.center_y) / eye.radius_y
    features = np.column_stack((np.ones_like(sx), sx, sy, sx * sy, sx * sx, sy * sy))
    target = rgb[sample_y, sample_x]
    coefficients, _, _, _ = np.linalg.lstsq(features, target, rcond=None)
    all_x = (xx - eye.center_x) / eye.radius_x
    all_y = (yy - eye.center_y) / eye.radius_y
    all_features = np.stack(
        (
            np.ones_like(all_x),
            all_x,
            all_y,
            all_x * all_y,
            all_x * all_x,
            all_y * all_y,
        ),
        axis=-1,
    )
    surface = np.clip(all_features @ coefficients, 0, 255).astype(np.uint8)
    return surface, len(sample_x)


def line_color(base: np.ndarray, eyes: tuple[EyeGeometry, EyeGeometry]) -> tuple[int, int, int]:
    samples: list[np.ndarray] = []
    brightness = luma(base[:, :, :3])
    for eye in eyes:
        left, top, right, bottom = eye.roi
        rgb = base[top:bottom, left:right, :3]
        values = brightness[top:bottom, left:right]
        selected = rgb[(values >= 35) & (values <= 105) & (rgb[:, :, 0] >= rgb[:, :, 2])]
        if len(selected):
            samples.append(selected)
    merged = np.concatenate(samples, axis=0)
    color = np.median(merged, axis=0).astype(np.uint8)
    return int(color[0]), int(color[1]), int(color[2])


def stage_masks(eye: EyeGeometry, progress: float, scale: int = 4) -> tuple[Image.Image, Image.Image]:
    left, top, right, bottom = eye.roi
    width = right - left
    height = bottom - top
    sample_x = left + ((np.arange(width * scale, dtype=np.float64) + 0.5) / scale)
    sample_y = top + ((np.arange(height * scale, dtype=np.float64) + 0.5) / scale)
    xx, yy = np.meshgrid(sample_x, sample_y)
    unit_x = (xx - eye.center_x) / eye.radius_x
    unit_y = (yy - eye.center_y) / eye.radius_y
    ellipse = (unit_x * unit_x) + (unit_y * unit_y) <= 1.0
    safe_x = np.clip(unit_x, -1, 1)
    extent = np.sqrt(np.maximum(0, 1 - (safe_x * safe_x)))
    open_upper = eye.center_y - (eye.radius_y * extent)
    open_lower = eye.center_y + (eye.radius_y * extent)
    closure = eye.closure_y + (eye.closure_arc * safe_x * safe_x) - (eye.closure_tilt * safe_x)
    moving_upper = (open_upper * (1 - progress)) + (closure * progress)
    lower_progress = max(0.0, min(1.0, (progress - 0.65) / 0.35))
    moving_lower = (open_lower * (1 - lower_progress)) + ((closure + 0.75) * lower_progress)
    fill = ellipse & ((yy <= moving_upper) | (yy >= moving_lower))
    fill_image = Image.fromarray(fill.astype(np.uint8) * 255, "L").resize(
        (width, height), Image.Resampling.LANCZOS
    )

    line_high = Image.new("L", (width * scale, height * scale), 0)
    line_draw = ImageDraw.Draw(line_high)
    points: list[tuple[float, float]] = []
    for unit in np.linspace(-0.88, 0.88, 80):
        upper = eye.center_y - (eye.radius_y * math.sqrt(max(0, 1 - (unit * unit))))
        closed = eye.closure_y + (eye.closure_arc * unit * unit) - (eye.closure_tilt * unit)
        y = (upper * (1 - progress)) + (closed * progress)
        x = eye.center_x + (eye.radius_x * unit)
        points.append(((x - left) * scale, (y - top) * scale))
    line_draw.line(points, fill=255, width=8 if progress == 1 else 6, joint="curve")
    line_image = line_high.resize((width, height), Image.Resampling.LANCZOS)
    return fill_image, line_image


def alpha_over(destination: np.ndarray, color: np.ndarray, alpha: np.ndarray) -> None:
    source_alpha = (alpha.astype(np.float32) / 255.0)[..., None]
    destination_alpha = (destination[..., 3:4].astype(np.float32) / 255.0)
    out_alpha = source_alpha + (destination_alpha * (1 - source_alpha))
    safe_alpha = np.where(out_alpha == 0, 1, out_alpha)
    out_rgb = (
        (color.astype(np.float32) * source_alpha)
        + (destination[..., :3].astype(np.float32) * destination_alpha * (1 - source_alpha))
    ) / safe_alpha
    destination[..., :3] = np.clip(out_rgb, 0, 255).astype(np.uint8)
    destination[..., 3] = np.clip(out_alpha[..., 0] * 255, 0, 255).astype(np.uint8)


def build_overlay(
    neutral: Image.Image,
    eyes: tuple[EyeGeometry, EyeGeometry],
    progress: float,
) -> tuple[Image.Image, dict[str, int]]:
    base = np.asarray(neutral, dtype=np.uint8)
    overlay = np.zeros_like(base)
    eyelid_color = np.asarray(line_color(base, eyes), dtype=np.uint8)
    sample_counts: dict[str, int] = {}
    for eye in eyes:
        surface, sample_count = skin_surface(base, eye)
        sample_counts[eye.name] = sample_count
        fill_mask, line_mask = stage_masks(eye, progress)
        left, top, right, bottom = eye.roi
        region = overlay[top:bottom, left:right]
        alpha_over(region, surface[top:bottom, left:right], np.asarray(fill_mask, dtype=np.uint8))
        line_rgb = np.broadcast_to(eyelid_color, (bottom - top, right - left, 3))
        alpha_over(region, line_rgb, np.asarray(line_mask, dtype=np.uint8))
    # Glasses are an immutable alignment reference. Any overlay pixel that intersects a frame or
    # bridge pixel is forced back to transparent so the rendered glasses always come from 06.
    overlay[glasses_mask(base, eyes)] = 0
    return Image.fromarray(overlay, "RGBA"), sample_counts


def changed_bounds(changed: np.ndarray) -> tuple[int, int, int, int] | None:
    ys, xs = np.nonzero(changed)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def roi_union_mask(eyes: tuple[EyeGeometry, EyeGeometry]) -> np.ndarray:
    mask = np.zeros((512, 512), dtype=bool)
    for eye in eyes:
        left, top, right, bottom = eye.roi
        mask[top:bottom, left:right] = True
    return mask


def eye_interior_mask(eyes: tuple[EyeGeometry, EyeGeometry]) -> np.ndarray:
    yy, xx = np.mgrid[0:512, 0:512]
    mask = np.zeros((512, 512), dtype=bool)
    for eye in eyes:
        dx = (xx - eye.center_x) / eye.radius_x
        dy = (yy - eye.center_y) / eye.radius_y
        distance = (dx * dx) + (dy * dy)
        mask |= distance < 0.98
    return mask


def glasses_mask(base: np.ndarray, eyes: tuple[EyeGeometry, EyeGeometry]) -> np.ndarray:
    yy, xx = np.mgrid[0:512, 0:512]
    mask = np.zeros((512, 512), dtype=bool)
    brightness = luma(base[:, :, :3])
    for eye in eyes:
        dx = (xx - eye.center_x) / eye.radius_x
        dy = (yy - eye.center_y) / eye.radius_y
        distance = (dx * dx) + (dy * dy)
        mask |= (distance >= 0.98) & (distance <= 1.85)
    refs = glasses_reference_points(eyes)
    bridge_left = min(refs["screenLeftBridge"][0], refs["screenRightBridge"][0])
    bridge_right = max(refs["screenLeftBridge"][0], refs["screenRightBridge"][0]) + 1
    bridge = brightness[196:219, bridge_left:bridge_right] <= 145
    mask[196:219, bridge_left:bridge_right] |= bridge
    return mask


def composite(neutral: Image.Image, overlay: Image.Image) -> Image.Image:
    return Image.alpha_composite(neutral, overlay)


def checker_background(size: tuple[int, int]) -> Image.Image:
    image = Image.new("RGBA", size, (238, 238, 238, 255))
    draw = ImageDraw.Draw(image)
    step = 16
    for y in range(0, size[1], step):
        for x in range(0, size[0], step):
            if ((x // step) + (y // step)) % 2:
                draw.rectangle((x, y, x + step - 1, y + step - 1), fill=(210, 210, 210, 255))
    return image


def render_contact_sheet(
    stages: list[tuple[str, Image.Image]],
    output: Path,
    background: tuple[int, int, int, int] | None = None,
) -> None:
    sheet = (
        checker_background((512 * len(stages), 548))
        if background is None
        else Image.new("RGBA", (512 * len(stages), 548), background)
    )
    draw = ImageDraw.Draw(sheet)
    for index, (name, image) in enumerate(stages):
        x = index * 512
        sheet.alpha_composite(image, (x, 36))
        draw.rectangle((x, 0, x + 511, 35), fill=(35, 35, 40, 255))
        draw.text((x + 12, 11), name.upper(), fill=(255, 255, 255, 255))
    sheet.save(output)


def dilate(mask: np.ndarray, radius: int) -> np.ndarray:
    result = mask.copy()
    for offset_y in range(-radius, radius + 1):
        for offset_x in range(-radius, radius + 1):
            shifted = np.zeros_like(mask)
            source_top = max(0, -offset_y)
            source_bottom = mask.shape[0] - max(0, offset_y)
            source_left = max(0, -offset_x)
            source_right = mask.shape[1] - max(0, offset_x)
            target_top = max(0, offset_y)
            target_bottom = mask.shape[0] - max(0, -offset_y)
            target_left = max(0, offset_x)
            target_right = mask.shape[1] - max(0, -offset_x)
            shifted[target_top:target_bottom, target_left:target_right] = mask[
                source_top:source_bottom, source_left:source_right
            ]
            result |= shifted
    return result


def dpi_alignment_report(
    neutral: Image.Image,
    overlays: list[tuple[str, Image.Image]],
    eyes: tuple[EyeGeometry, EyeGeometry],
) -> dict[str, object]:
    report: dict[str, object] = {}
    roi = Image.fromarray(roi_union_mask(eyes).astype(np.uint8) * 255, "L")
    for scale in (1.0, 1.25, 1.5, 2.0):
        size = (round(CANVAS_SIZE[0] * scale), round(CANVAS_SIZE[1] * scale))
        scaled_base = neutral.resize(size, Image.Resampling.LANCZOS)
        scaled_roi = np.asarray(roi.resize(size, Image.Resampling.NEAREST), dtype=np.uint8) > 0
        scaled_roi = dilate(scaled_roi, 3)
        stages: dict[str, object] = {}
        for name, overlay in overlays:
            scaled_overlay = overlay.resize(size, Image.Resampling.LANCZOS)
            rendered = Image.alpha_composite(scaled_base, scaled_overlay)
            changed = np.any(
                np.asarray(rendered, dtype=np.uint8) != np.asarray(scaled_base, dtype=np.uint8), axis=2
            )
            stages[name] = {
                "baseSize": scaled_base.size,
                "overlaySize": scaled_overlay.size,
                "sharedOrigin": [0, 0],
                "outsideScaledEyeRoiChangedPixels": int(np.count_nonzero(changed & ~scaled_roi)),
            }
        report[f"{int(scale * 100)}%"] = {"pixelSize": size, "stages": stages}
    return report


def render_timing_preview(stages: list[tuple[str, Image.Image]], output: Path) -> None:
    stage_map = dict(stages)
    names = ("open", "40", "75", "closed", "75", "40", "open")
    durations = (20, 30, 50, 50, 55, 55, 40)
    frames = [stage_map[name].convert("RGBA") for name in names]
    frames[0].save(
        output,
        save_all=True,
        append_images=frames[1:],
        duration=durations,
        loop=0,
        disposal=2,
    )


def render_eye_contact_sheet(
    stages: list[tuple[str, Image.Image]],
    eyes: tuple[EyeGeometry, EyeGeometry],
    output: Path,
) -> None:
    left = min(eye.roi[0] for eye in eyes) - 3
    top = min(eye.roi[1] for eye in eyes) - 3
    right = max(eye.roi[2] for eye in eyes) + 3
    bottom = max(eye.roi[3] for eye in eyes) + 3
    scale = 8
    crop_width = (right - left) * scale
    crop_height = (bottom - top) * scale
    sheet = Image.new("RGBA", (crop_width * len(stages), crop_height + 32), (32, 32, 36, 255))
    draw = ImageDraw.Draw(sheet)
    for index, (name, image) in enumerate(stages):
        crop = image.crop((left, top, right, bottom)).resize((crop_width, crop_height), Image.Resampling.NEAREST)
        x = index * crop_width
        sheet.alpha_composite(crop, (x, 32))
        draw.text((x + 8, 9), f"{name.upper()} · 8x", fill=(255, 255, 255, 255))
    sheet.save(output)


def detect_closed_reference_center(closed: Image.Image, eye: EyeGeometry) -> tuple[float, float]:
    array = np.asarray(closed, dtype=np.uint8)
    brightness = luma(array[:, :, :3])
    left = eye.center_x - eye.radius_x + 4
    right = eye.center_x + eye.radius_x - 4
    top = eye.center_y - 7
    bottom = eye.center_y + 16
    region = brightness[top:bottom, left:right]
    weights = np.clip(135 - region, 0, 135)
    yy, xx = np.mgrid[top:bottom, left:right]
    total = float(weights.sum())
    return float((xx * weights).sum() / total), float((yy * weights).sum() / total)


def analyze_inputs(
    neutral_path: Path,
    closed_path: Path,
    alternate_path: Path,
    output: Path,
) -> tuple[Image.Image, Image.Image, tuple[EyeGeometry, EyeGeometry], dict[str, object]]:
    neutral = load_rgba(neutral_path)
    closed = load_rgba(closed_path)
    alternate = load_rgba(alternate_path)
    output.mkdir(parents=True, exist_ok=True)
    eyes = detect_eye_geometry(neutral)
    render_face_grid(neutral, output / "blink_face_coordinate_grid_06.png")
    render_face_grid(closed, output / "blink_face_coordinate_grid_05.png")
    render_face_grid(alternate, output / "blink_face_coordinate_grid_07.png")
    render_roi_debug(neutral, eyes, output / "blink_eye_roi_debug.png")

    closed_centers = {eye.name: detect_closed_reference_center(closed, eye) for eye in eyes}
    closure_targets = {eye.name: (eye.center_x, eye.closure_y) for eye in eyes}
    deltas = {
        eye.name: (
            closed_centers[eye.name][0] - closure_targets[eye.name][0],
            closed_centers[eye.name][1] - closure_targets[eye.name][1],
        )
        for eye in eyes
    }
    delta_values = np.asarray(list(deltas.values()), dtype=np.float64)
    rigid_delta = delta_values.mean(axis=0)
    residuals = np.linalg.norm(delta_values - rigid_delta, axis=1)
    result: dict[str, object] = {
        "canvas": CANVAS_SIZE,
        "files": {
            "06": {"path": str(neutral_path), "sha256": sha256(neutral_path), "alphaBounds": alpha_bounds(neutral)},
            "05": {"path": str(closed_path), "sha256": sha256(closed_path), "alphaBounds": alpha_bounds(closed)},
            "07": {"path": str(alternate_path), "sha256": sha256(alternate_path), "alphaBounds": alpha_bounds(alternate)},
        },
        "faceSearch": FACE_SEARCH,
        "eyes": [{**asdict(eye), "roi": eye.roi} for eye in eyes],
        "glassesReferencePoints": glasses_reference_points(eyes),
        "darknessCandidates06": darkness_candidates(neutral),
        "closed05ReferenceCenters": closed_centers,
        "closed05To06TargetDeltas": deltas,
        "bestSharedRigidTranslation": tuple(float(value) for value in rigid_delta),
        "rigidAlignmentResidualPixels": {
            eye.name: float(residual) for eye, residual in zip(eyes, residuals, strict=True)
        },
        "use05": "shape-reference-only",
    }
    (output / "blink_analysis.json").write_text(
        json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    return neutral, closed, eyes, result


def analyze(args: argparse.Namespace) -> None:
    _, _, _, result = analyze_inputs(args.neutral, args.closed, args.alternate, args.output)
    print(json.dumps(result, indent=2, ensure_ascii=False))


def prototype(args: argparse.Namespace) -> None:
    neutral, _, eyes, analysis = analyze_inputs(args.neutral, args.closed, args.alternate, args.output)
    overlay_directory = args.output / "overlays"
    overlay_directory.mkdir(parents=True, exist_ok=True)
    stages: list[tuple[str, Image.Image]] = [("open", neutral.copy())]
    base = np.asarray(neutral, dtype=np.uint8)
    allowed_roi = roi_union_mask(eyes)
    allowed_interior = eye_interior_mask(eyes)
    protected_glasses = glasses_mask(base, eyes)
    report_lines = [
        "DesktopPet NaturalMotion Blink Pixel Diff Report",
        "================================================",
        f"06 SHA-256: {analysis['files']['06']['sha256']}",
        f"screen-left ROI: {eyes[0].roi}; center=({eyes[0].center_x},{eyes[0].center_y})",
        f"screen-right ROI: {eyes[1].roi}; center=({eyes[1].center_x},{eyes[1].center_y})",
        "05 usage: closure curvature/direction reference only; no 05 pixels are copied.",
        f"05 shared-rigid residuals: {analysis['rigidAlignmentResidualPixels']}",
        "",
    ]
    qa: dict[str, object] = {}
    for name, progress in BLINK_STAGES:
        overlay, sample_counts = build_overlay(neutral, eyes, progress)
        overlay_path = overlay_directory / f"blink_{name}.png"
        overlay.save(overlay_path)
        result = composite(neutral, overlay)
        result.save(args.output / f"blink_{name}.png")
        stages.append((name, result))

        result_array = np.asarray(result, dtype=np.uint8)
        changed = np.any(result_array != base, axis=2)
        outside_count = int(np.count_nonzero(changed & ~allowed_roi))
        outside_interior_count = int(np.count_nonzero(changed & ~allowed_interior))
        glasses_count = int(np.count_nonzero(changed & protected_glasses))
        overlay_alpha = np.asarray(overlay, dtype=np.uint8)[:, :, 3]
        overlay_pixels = overlay_alpha > 0
        stage_result = {
            "overlaySize": overlay.size,
            "overlayNonTransparentPixels": int(np.count_nonzero(overlay_pixels)),
            "overlayBounds": changed_bounds(overlay_pixels),
            "compositeChangedPixels": int(np.count_nonzero(changed)),
            "compositeChangedBounds": changed_bounds(changed),
            "outsideEyeRoiChangedPixels": outside_count,
            "outsideEyeInteriorChangedPixels": outside_interior_count,
            "protectedGlassesChangedPixels": glasses_count,
            "skinSampleCounts": sample_counts,
        }
        qa[name] = stage_result
        report_lines.extend(
            [
                f"[{name}]",
                f"overlay size: {overlay.size[0]}x{overlay.size[1]}",
                f"overlay non-transparent pixels: {stage_result['overlayNonTransparentPixels']}",
                f"overlay bounds: {stage_result['overlayBounds']}",
                f"composite changed pixels: {stage_result['compositeChangedPixels']}",
                f"composite changed bounds: {stage_result['compositeChangedBounds']}",
                f"outside Eye ROI changed pixels: {outside_count}",
                f"outside measured eye interiors changed pixels: {outside_interior_count}",
                f"protected glasses changed pixels: {glasses_count}",
                "",
            ]
        )
        if outside_count != 0 or outside_interior_count != 0 or glasses_count != 0:
            raise RuntimeError(f"Pixel-diff gate failed for blink_{name}")

    neutral.save(args.output / "blink_open.png")
    render_contact_sheet(stages, args.output / "blink_contact_sheet.png")
    render_contact_sheet(stages, args.output / "blink_contact_sheet_light.png", (248, 248, 248, 255))
    render_contact_sheet(stages, args.output / "blink_contact_sheet_dark.png", (24, 26, 31, 255))
    render_eye_contact_sheet(stages, eyes, args.output / "blink_eye_contact_sheet_8x.png")
    render_timing_preview(stages, args.output / "blink_timing_preview.gif")
    overlay_stages = [
        (name, load_rgba(overlay_directory / f"blink_{name}.png")) for name, _ in BLINK_STAGES
    ]
    dpi_report = dpi_alignment_report(neutral, overlay_stages, eyes)
    if any(
        stage["outsideScaledEyeRoiChangedPixels"] != 0
        for scale_result in dpi_report.values()
        for stage in scale_result["stages"].values()
    ):
        raise RuntimeError("DPI pixel-diff gate failed")
    (args.output / "blink_diff_report.txt").write_text("\n".join(report_lines), encoding="utf-8")
    (args.output / "blink_qa.json").write_text(
        json.dumps(qa, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    (args.output / "blink_dpi_report.json").write_text(
        json.dumps(dpi_report, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    print(json.dumps(qa, indent=2, ensure_ascii=False))


def main() -> None:
    parser = argparse.ArgumentParser(description="Deterministic DesktopPet blink asset analysis and QA.")
    subparsers = parser.add_subparsers(dest="command", required=True)
    for command, handler in (("analyze", analyze), ("prototype", prototype)):
        command_parser = subparsers.add_parser(command)
        command_parser.add_argument("--neutral", type=Path, required=True)
        command_parser.add_argument("--closed", type=Path, required=True)
        command_parser.add_argument("--alternate", type=Path, required=True)
        command_parser.add_argument("--output", type=Path, required=True)
        command_parser.set_defaults(handler=handler)
    args = parser.parse_args()
    args.handler(args)


if __name__ == "__main__":
    main()
