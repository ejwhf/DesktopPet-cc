#!/usr/bin/env python3
"""Build and verify Desktop Pet sprite assets without third-party packages."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import json
from pathlib import Path, PurePosixPath
import re
import sys
from typing import Iterable

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from tools.png_codec import PngError, PngImage, crop, paste_rgba, read_png, resize_rgba, write_png


ASSET_IDS = (
    "peek.screen_left",
    "falling.reach",
    "sit.edge_idle",
    "sit.edge_alt",
    "idle.blink_smile",
    "idle.neutral",
    "idle.alt",
    "read.small",
    "read.hold",
    "sleep.seated",
    "heart",
    "read.surprised",
    "sleep.seated_alt",
    "heart.raise",
    "wave.single_a",
    "cheer.both_hands",
    "wave.single_b",
    "write.prone",
)

ANCHOR_KINDS = (
    "edge",
    "pivot",
    "seat",
    "seat",
    "ground",
    "ground",
    "ground",
    "ground",
    "ground",
    "floor",
    "ground",
    "ground",
    "floor",
    "ground",
    "ground",
    "ground",
    "ground",
    "floor",
)

ID_PATTERN = re.compile(r"^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$")
ANCHOR_FIELDS = ("groundAnchor", "seatAnchor", "floorAnchor", "edgeAnchor")

# Flat standing poses are authored at slightly different overall scales even
# after their faces have been normalised. Equalising their complete visible
# height keeps animation changes from looking like the character shrinks or
# grows. Sitting, falling and prone poses retain their natural heights.
STANDING_ASSET_IDS = frozenset(
    {
        "peek.screen_left",
        "idle.blink_smile",
        "idle.neutral",
        "idle.alt",
        "read.small",
        "read.hold",
        "heart",
        "read.surprised",
        "heart.raise",
        "wave.single_a",
        "cheer.both_hands",
        "wave.single_b",
    }
)
TARGET_STANDING_HEIGHT = 340


@dataclass(frozen=True)
class Bounds:
    left: int
    top: int
    right: int
    bottom: int

    @property
    def width(self) -> int:
        return self.right - self.left

    @property
    def height(self) -> int:
        return self.bottom - self.top

    def xywh(self) -> list[int]:
        return [self.left, self.top, self.width, self.height]

    def union(self, other: "Bounds") -> "Bounds":
        return Bounds(
            min(self.left, other.left),
            min(self.top, other.top),
            max(self.right, other.right),
            max(self.bottom, other.bottom),
        )


@dataclass(frozen=True)
class AlphaComponent:
    pixel_count: int
    bounds: Bounds
    pixels: tuple[int, ...]


def alpha_bounds(image: PngImage, threshold: int = 1) -> Bounds | None:
    rgba = image.as_rgba()
    left, top = rgba.width, rgba.height
    right = bottom = 0
    found = False
    for y in range(rgba.height):
        for x in range(rgba.width):
            if rgba.pixels[(y * rgba.width + x) * 4 + 3] >= threshold:
                found = True
                left = min(left, x)
                top = min(top, y)
                right = max(right, x + 1)
                bottom = max(bottom, y + 1)
    return Bounds(left, top, right, bottom) if found else None


def alpha_components(image: PngImage, threshold: int = 1) -> list[AlphaComponent]:
    """Return every 4-connected alpha component, largest first."""
    rgba = image.as_rgba()
    width, height = rgba.width, rgba.height
    visible = bytearray(
        alpha >= threshold for alpha in rgba.pixels[3::4]
    )
    seen = bytearray(width * height)
    components: list[AlphaComponent] = []
    for start in range(width * height):
        if not visible[start] or seen[start]:
            continue
        stack = [start]
        seen[start] = 1
        indices: list[int] = []
        left, top, right, bottom = width, height, 0, 0
        while stack:
            index = stack.pop()
            indices.append(index)
            x, y = index % width, index // width
            left, top = min(left, x), min(top, y)
            right, bottom = max(right, x + 1), max(bottom, y + 1)
            if x and visible[index - 1] and not seen[index - 1]:
                seen[index - 1] = 1
                stack.append(index - 1)
            if x + 1 < width and visible[index + 1] and not seen[index + 1]:
                seen[index + 1] = 1
                stack.append(index + 1)
            if y and visible[index - width] and not seen[index - width]:
                seen[index - width] = 1
                stack.append(index - width)
            if y + 1 < height and visible[index + width] and not seen[index + width]:
                seen[index + width] = 1
                stack.append(index + width)
        components.append(
            AlphaComponent(
                len(indices),
                Bounds(left, top, right, bottom),
                tuple(indices),
            )
        )
    components.sort(key=lambda component: component.pixel_count, reverse=True)
    return components


def _bounds_distance_squared(first: Bounds, second: Bounds) -> int:
    if first.right < second.left:
        dx = second.left - first.right
    elif second.right < first.left:
        dx = first.left - second.right
    else:
        dx = 0
    if first.bottom < second.top:
        dy = second.top - first.bottom
    elif second.bottom < first.top:
        dy = first.top - second.bottom
    else:
        dy = 0
    return dx * dx + dy * dy


def split_component_sheet(
    image: PngImage,
    *,
    main_pixel_threshold: int = 10_000,
    row_counts: tuple[int, ...] = (4, 4, 4, 4, 2),
) -> tuple[list[PngImage], list[AlphaComponent]]:
    """Partition a transparent action sheet by connected subject components.

    Each large component defines one subject. Every small disconnected effect
    (surprise mark, highlight, or detached detail) is assigned to the nearest
    subject. Cells are then emitted in row-major order, copying only pixels
    owned by that group; overlapping crop boxes therefore cannot contaminate
    one another.
    """
    rgba = image.as_rgba()
    components = alpha_components(rgba)
    main_components = [
        component for component in components if component.pixel_count >= main_pixel_threshold
    ]
    expected = sum(row_counts)
    if len(main_components) != expected:
        counts = ", ".join(str(component.pixel_count) for component in components[:expected + 4])
        raise ValueError(
            f"expected {expected} main alpha components >= {main_pixel_threshold} pixels, "
            f"found {len(main_components)} (largest counts: {counts})"
        )

    # Component top coordinates have clear row bands in the authored sheet.
    # Chunking after top-sort is stable even when sitting/prone poses have very
    # different heights and centres.
    by_row = sorted(main_components, key=lambda component: component.bounds.top)
    ordered: list[AlphaComponent] = []
    offset = 0
    for row_count in row_counts:
        row = by_row[offset : offset + row_count]
        ordered.extend(sorted(row, key=lambda component: component.bounds.left))
        offset += row_count

    groups: list[list[AlphaComponent]] = [[component] for component in ordered]
    main_ids = {id(component) for component in main_components}
    for component in components:
        if id(component) in main_ids:
            continue
        nearest = min(
            range(len(ordered)),
            key=lambda index: _bounds_distance_squared(
                component.bounds, ordered[index].bounds
            ),
        )
        groups[nearest].append(component)

    cells: list[PngImage] = []
    for group in groups:
        bounds = group[0].bounds
        for component in group[1:]:
            bounds = bounds.union(component.bounds)
        pixels = bytearray(bounds.width * bounds.height * 4)
        for component in group:
            for source_index in component.pixels:
                source_x, source_y = source_index % rgba.width, source_index // rgba.width
                target_x, target_y = source_x - bounds.left, source_y - bounds.top
                source_offset = source_index * 4
                target_offset = (target_y * bounds.width + target_x) * 4
                pixels[target_offset : target_offset + 4] = rgba.pixels[
                    source_offset : source_offset + 4
                ]
        cells.append(PngImage(bounds.width, bounds.height, "RGBA", bytes(pixels), 6))
    return cells, ordered


def _skin_mask(image: PngImage) -> bytearray:
    rgba = image.as_rgba()
    mask = bytearray(rgba.width * rgba.height)
    for index in range(rgba.width * rgba.height):
        red, green, blue, alpha = rgba.pixels[index * 4 : index * 4 + 4]
        # Tuned against the authored character palette. Requiring red to lead
        # green/blue excludes most robe, hair, paper, and edge pixels.
        if (
            alpha > 50
            and red > 150
            and green > 65
            and red > green * 1.05
            and green > blue * 1.02
            and red - blue > 35
        ):
            mask[index] = 1
    return mask


def _components(mask: bytearray, width: int, height: int) -> list[tuple[int, Bounds]]:
    seen = bytearray(width * height)
    components: list[tuple[int, Bounds]] = []
    for start in range(width * height):
        if not mask[start] or seen[start]:
            continue
        stack = [start]
        seen[start] = 1
        count = 0
        left, top, right, bottom = width, height, 0, 0
        while stack:
            index = stack.pop()
            x, y = index % width, index // width
            count += 1
            left, top = min(left, x), min(top, y)
            right, bottom = max(right, x + 1), max(bottom, y + 1)
            if x and mask[index - 1] and not seen[index - 1]:
                seen[index - 1] = 1
                stack.append(index - 1)
            if x + 1 < width and mask[index + 1] and not seen[index + 1]:
                seen[index + 1] = 1
                stack.append(index + 1)
            if y and mask[index - width] and not seen[index - width]:
                seen[index - width] = 1
                stack.append(index - width)
            if y + 1 < height and mask[index + width] and not seen[index + width]:
                seen[index + width] = 1
                stack.append(index + width)
        if count >= 10:
            components.append((count, Bounds(left, top, right, bottom)))
    components.sort(key=lambda component: component[0], reverse=True)
    return components


def detect_face(image: PngImage) -> Bounds:
    components = _components(_skin_mask(image), image.width, image.height)
    if not components:
        raise ValueError("no face/skin component detected")
    # The connected face region is consistently the largest skin component.
    return components[0][1]


def _cell_edges(length: int, count: int) -> list[int]:
    return [round(index * length / count) for index in range(count + 1)]


def _transparent_grid_edges(
    image: PngImage,
    count: int,
    *,
    axis: str,
    alpha_threshold: int = 1,
) -> list[int]:
    """Find cell separators from fully transparent gutters.

    Generated action sheets are laid out as a grid, but a row is not
    necessarily exactly ``height / rows`` high. In v2, equal arithmetic slicing
    cut the first row's shoes into the next row. Authored gutters are the actual
    contract, so prefer their centres and retain equal slicing as a fallback
    for synthetic/tightly-packed sheets.
    """
    rgba = image.as_rgba()
    length = rgba.width if axis == "x" else rgba.height
    cross_length = rgba.height if axis == "x" else rgba.width
    occupancy: list[int] = []
    for position in range(length):
        count_visible = 0
        for cross in range(cross_length):
            x, y = (position, cross) if axis == "x" else (cross, position)
            if rgba.pixels[(y * rgba.width + x) * 4 + 3] >= alpha_threshold:
                count_visible += 1
        occupancy.append(count_visible)

    runs: list[tuple[int, int]] = []
    run_start: int | None = None
    for index, value in enumerate(occupancy + [1]):
        if value == 0 and run_start is None:
            run_start = index
        elif value != 0 and run_start is not None:
            runs.append((run_start, index))  # half-open
            run_start = None

    internal = [(start, end) for start, end in runs if start > 0 and end < length]
    if len(internal) < count - 1:
        return _cell_edges(length, count)

    # Map each expected separator to a different nearest gutter. A monotonic
    # greedy choice is sufficient because generated gutters never cross.
    separators: list[int] = []
    remaining = internal[:]
    previous = 0
    for separator_index in range(1, count):
        expected = separator_index * length / count
        viable = [run for run in remaining if (run[0] + run[1]) / 2 > previous]
        if not viable:
            return _cell_edges(length, count)
        selected = min(viable, key=lambda run: abs((run[0] + run[1]) / 2 - expected))
        boundary = (selected[0] + selected[1]) // 2
        separators.append(boundary)
        previous = boundary
        remaining.remove(selected)
    edges = [0, *separators, length]
    if any(right <= left for left, right in zip(edges, edges[1:])):
        return _cell_edges(length, count)
    return edges


def split_sheet(image: PngImage, columns: int = 4, rows: int = 5, count: int = 18) -> list[PngImage]:
    if count > columns * rows:
        raise ValueError("requested cell count exceeds grid capacity")
    x_edges = _transparent_grid_edges(image, columns, axis="x")
    y_edges = _transparent_grid_edges(image, rows, axis="y")
    cells = []
    for index in range(count):
        column, row = index % columns, index // columns
        cells.append(
            crop(
                image,
                (x_edges[column], y_edges[row], x_edges[column + 1], y_edges[row + 1]),
            ).as_rgba()
        )
    return cells


def _transparent_canvas(size: int) -> bytearray:
    return bytearray(size * size * 4)


def _normalise_cell(
    cell: PngImage,
    *,
    face: Bounds,
    target_face_width: int,
    canvas_size: int,
    anchor_kind: str,
    safety_margin: int,
) -> tuple[PngImage, float]:
    visual = alpha_bounds(cell)
    if visual is None:
        raise ValueError("empty sprite cell")
    padding = 2
    crop_box = (
        max(0, visual.left - padding),
        max(0, visual.top - padding),
        min(cell.width, visual.right + padding),
        min(cell.height, visual.bottom + padding),
    )
    cutout = crop(cell, crop_box).as_rgba()
    scale = target_face_width / face.width
    width = max(1, round(cutout.width * scale))
    height = max(1, round(cutout.height * scale))
    if width > canvas_size - 2 * safety_margin or height > canvas_size - 2 * safety_margin:
        raise ValueError(
            f"normalised sprite {width}x{height} violates {safety_margin}px safety margin"
        )
    scaled = resize_rgba(cutout, width, height)
    if anchor_kind == "pivot":
        x = round((canvas_size - width) / 2)
        y = round((canvas_size - height) / 2)
    elif anchor_kind == "edge":
        x = safety_margin
        y = canvas_size - safety_margin - height
    else:
        x = round((canvas_size - width) / 2)
        y = canvas_size - safety_margin - height
    output = _transparent_canvas(canvas_size)
    paste_rgba(output, canvas_size, canvas_size, scaled, x, y)
    return PngImage(canvas_size, canvas_size, "RGBA", bytes(output), 6), scale


def _asset_entry(asset_id: str, sprite: PngImage, anchor_kind: str) -> dict[str, object]:
    bounds = alpha_bounds(sprite, threshold=2)
    if bounds is None:
        raise ValueError(f"{asset_id}: output is empty")
    center_x = (bounds.left + bounds.right) / 2 / sprite.width
    bottom = bounds.bottom / sprite.height
    entry: dict[str, object] = {
        "id": asset_id,
        "file": f"sprites/{asset_id}.png",
        "sourceSize": [sprite.width, sprite.height],
        "visualBounds": bounds.xywh(),
        "pivot": [0.5, 0.5],
        "hitMask": f"masks/{asset_id}.png",
        "durationMs": 1200,
        "interruptible": True,
        "weight": 1,
        "cooldownMs": 0,
        "nextStates": [],
    }
    if anchor_kind == "edge":
        entry["edgeAnchor"] = [bounds.left / sprite.width, bottom]
        entry["groundAnchor"] = [center_x, bottom]
    elif anchor_kind == "pivot":
        entry["pivot"] = [center_x, (bounds.top + bounds.bottom) / 2 / sprite.height]
    elif anchor_kind == "seat":
        entry["seatAnchor"] = [center_x, bounds.top / sprite.height + bounds.height * 0.58 / sprite.height]
        entry["groundAnchor"] = [center_x, bottom]
    elif anchor_kind == "floor":
        entry["floorAnchor"] = [center_x, bottom]
    else:
        entry["groundAnchor"] = [center_x, bottom]
    return entry


def normalise_standing_height(
    sprite: PngImage,
    *,
    anchor_kind: str,
    target_height: int = TARGET_STANDING_HEIGHT,
) -> PngImage:
    """Scale a whole flat pose around its contact point to a common height."""
    bounds = alpha_bounds(sprite, threshold=2)
    if bounds is None:
        raise ValueError("cannot normalise an empty standing sprite")
    if target_height <= 0:
        raise ValueError("target standing height must be positive")

    target_width = max(1, round(bounds.width * target_height / bounds.height))
    if target_width > sprite.width or target_height > sprite.height:
        raise ValueError(
            f"normalised pose {target_width}x{target_height} exceeds "
            f"{sprite.width}x{sprite.height} canvas"
        )

    cutout = crop(sprite, (bounds.left, bounds.top, bounds.right, bounds.bottom))
    scaled = resize_rgba(cutout, target_width, target_height)
    output = _transparent_canvas(sprite.width)
    y = bounds.bottom - target_height
    if anchor_kind == "edge":
        x = bounds.left
    else:
        center_x = (bounds.left + bounds.right) / 2
        x = round(center_x - target_width / 2)
    if x < 0 or y < 0 or x + target_width > sprite.width or y + target_height > sprite.height:
        raise ValueError("normalised standing pose does not fit its canvas")
    paste_rgba(output, sprite.width, sprite.height, scaled, x, y)
    return PngImage(sprite.width, sprite.height, "RGBA", bytes(output), 6)


def extract_sheet(
    sheet_path: Path,
    asset_root: Path,
    *,
    canvas_size: int = 512,
    target_face_width: int = 98,
    safety_margin: int = 48,
) -> list[dict[str, object]]:
    sheet = read_png(sheet_path)
    if sheet.mode != "RGBA":
        raise ValueError(f"sheet must be true RGBA PNG, got {sheet.mode}")
    cells, main_components = split_component_sheet(sheet)
    print(
        f"detected {len(main_components)} main subject components "
        f"(alpha pixels {min(item.pixel_count for item in main_components)}.."
        f"{max(item.pixel_count for item in main_components)})"
    )
    faces = [detect_face(cell) for cell in cells]
    master_face_width = faces[5].width
    entries: list[dict[str, object]] = []
    sprites_dir = asset_root / "sprites"
    sprites_dir.mkdir(parents=True, exist_ok=True)
    print(
        "#  id                       face  relative  output-scale  visualBounds",
        file=sys.stdout,
    )
    for index, (asset_id, anchor_kind, cell, face) in enumerate(
        zip(ASSET_IDS, ANCHOR_KINDS, cells, faces), start=1
    ):
        sprite, scale = _normalise_cell(
            cell,
            face=face,
            target_face_width=target_face_width,
            canvas_size=canvas_size,
            anchor_kind=anchor_kind,
            safety_margin=safety_margin,
        )
        if asset_id in STANDING_ASSET_IDS:
            sprite = normalise_standing_height(sprite, anchor_kind=anchor_kind)
        write_png(sprites_dir / f"{asset_id}.png", sprite)
        entry = _asset_entry(asset_id, sprite, anchor_kind)
        entries.append(entry)
        print(
            f"{index:02d} {asset_id:<24} {face.width:>4}px  "
            f"{master_face_width / face.width:>7.3f}x  {scale:>10.3f}x  "
            f"{entry['visualBounds']}"
        )

    manifest = {
        "schemaVersion": 1,
        "sourceSize": [canvas_size, canvas_size],
        "hitMaskAlphaThreshold": 16,
        "sourceSheet": sheet_path.as_posix(),
        "normalization": {
            "masterAssetId": "idle.neutral",
            "masterFaceWidthPx": master_face_width,
            "targetFaceWidthPx": target_face_width,
            "targetStandingHeightPx": TARGET_STANDING_HEIGHT,
            "safetyMarginPx": safety_margin,
        },
        "assets": entries,
    }
    manifest_path = asset_root / "manifest" / "assets.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return entries


def generate_masks(asset_root: Path, threshold: int = 16) -> None:
    manifest = load_manifest(asset_root / "manifest" / "assets.json")
    for entry in manifest["assets"]:
        sprite = read_png(asset_root / str(entry["file"]))
        rgba = sprite.as_rgba()
        pixels = bytes(
            255 if rgba.pixels[index * 4 + 3] >= threshold else 0
            for index in range(rgba.width * rgba.height)
        )
        target = asset_root / str(entry["hitMask"])
        write_png(target, PngImage(rgba.width, rgba.height, "L", pixels, 0))


_DIGITS = {
    "0": ("111", "101", "101", "101", "111"),
    "1": ("010", "110", "010", "010", "111"),
    "2": ("111", "001", "111", "100", "111"),
    "3": ("111", "001", "111", "001", "111"),
    "4": ("101", "101", "111", "001", "001"),
    "5": ("111", "100", "111", "001", "111"),
    "6": ("111", "100", "111", "101", "111"),
    "7": ("111", "001", "010", "010", "010"),
    "8": ("111", "101", "111", "101", "111"),
    "9": ("111", "101", "111", "001", "111"),
}


def _draw_index_badge(canvas: bytearray, width: int, height: int, x: int, y: int, label: str) -> None:
    scale = 2
    badge_width = len(label) * 4 * scale + 4
    badge_height = 5 * scale + 4
    for badge_y in range(y, min(height, y + badge_height)):
        for badge_x in range(x, min(width, x + badge_width)):
            offset = (badge_y * width + badge_x) * 4
            canvas[offset : offset + 4] = bytes((24, 24, 32, 220))
    cursor = x + 2
    for digit in label:
        for glyph_y, row in enumerate(_DIGITS[digit]):
            for glyph_x, bit in enumerate(row):
                if bit == "0":
                    continue
                for dy in range(scale):
                    for dx in range(scale):
                        pixel_x = cursor + glyph_x * scale + dx
                        pixel_y = y + 2 + glyph_y * scale + dy
                        offset = (pixel_y * width + pixel_x) * 4
                        canvas[offset : offset + 4] = bytes((255, 255, 255, 255))
        cursor += 4 * scale


def generate_contact_sheet(asset_root: Path, output: Path, cell_size: int = 256) -> None:
    manifest = load_manifest(asset_root / "manifest" / "assets.json")
    columns, rows = 4, 5
    width, height = columns * cell_size, rows * cell_size
    canvas = bytearray(width * height * 4)
    # Opaque neutral checkerboard makes transparent halos visible in review.
    checker = 16
    for y in range(height):
        for x in range(width):
            value = 238 if ((x // checker) + (y // checker)) % 2 == 0 else 210
            offset = (y * width + x) * 4
            canvas[offset : offset + 4] = bytes((value, value, value, 255))
    for index, entry in enumerate(manifest["assets"]):
        sprite = read_png(asset_root / str(entry["file"]))
        reduced = resize_rgba(sprite, cell_size, cell_size)
        paste_rgba(
            canvas,
            width,
            height,
            reduced,
            (index % columns) * cell_size,
            (index // columns) * cell_size,
        )
        _draw_index_badge(
            canvas,
            width,
            height,
            (index % columns) * cell_size + 5,
            (index // columns) * cell_size + 5,
            f"{index + 1:02d}",
        )
    write_png(output, PngImage(width, height, "RGBA", bytes(canvas), 6))


def load_manifest(path: Path) -> dict[str, object]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot load manifest {path}: {error}") from error
    if not isinstance(document, dict):
        raise ValueError("manifest root must be an object")
    return document


def _is_number(value: object) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def _coordinate(value: object) -> bool:
    return (
        isinstance(value, list)
        and len(value) == 2
        and all(_is_number(item) and 0 <= item <= 1 for item in value)
    )


def _safe_asset_path(asset_root: Path, value: object, label: str, errors: list[str]) -> Path | None:
    if not isinstance(value, str) or not value or "\\" in value:
        errors.append(f"{label} must be a non-empty forward-slash relative path")
        return None
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts or "." in path.parts:
        errors.append(f"{label} must stay inside the asset root")
        return None
    return asset_root.joinpath(*path.parts)


def lint_assets(asset_root: Path, *, minimum_margin: int = 48) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    manifest_path = asset_root / "manifest" / "assets.json"
    try:
        manifest = load_manifest(manifest_path)
    except ValueError as error:
        return [str(error)], warnings
    if manifest.get("schemaVersion") != 1:
        errors.append("manifest.schemaVersion must equal 1")
    root_size = manifest.get("sourceSize")
    if (
        root_size is not None
        and (
            not isinstance(root_size, list)
            or len(root_size) != 2
            or not all(isinstance(value, int) and not isinstance(value, bool) and value > 0 for value in root_size)
        )
    ):
        errors.append("manifest.sourceSize must be two positive integers")
    mask_threshold = manifest.get("hitMaskAlphaThreshold", 16)
    if not isinstance(mask_threshold, int) or isinstance(mask_threshold, bool) or not 0 <= mask_threshold <= 255:
        errors.append("manifest.hitMaskAlphaThreshold must be an integer from 0 to 255")
        mask_threshold = 16
    normalization = manifest.get("normalization")
    if normalization is not None:
        if not isinstance(normalization, dict):
            errors.append("manifest.normalization must be an object")
        else:
            target_standing_height = normalization.get("targetStandingHeightPx")
            if (
                not isinstance(target_standing_height, int)
                or isinstance(target_standing_height, bool)
                or target_standing_height <= 0
            ):
                errors.append(
                    "manifest.normalization.targetStandingHeightPx must be a positive integer"
                )
    assets = manifest.get("assets")
    if not isinstance(assets, list):
        return errors + ["manifest.assets must be an array"], warnings
    if len(assets) != len(ASSET_IDS):
        errors.append(f"manifest must contain exactly {len(ASSET_IDS)} assets, found {len(assets)}")
    seen: set[str] = set()
    for position, entry in enumerate(assets):
        prefix = f"assets[{position}]"
        if not isinstance(entry, dict):
            errors.append(f"{prefix} must be an object")
            continue
        asset_id = entry.get("id")
        if not isinstance(asset_id, str) or not ID_PATTERN.fullmatch(asset_id):
            errors.append(f"{prefix}.id is invalid")
            continue
        if asset_id in seen:
            errors.append(f"duplicate asset id {asset_id!r}")
        seen.add(asset_id)
        for field in (
            "file",
            "visualBounds",
            "pivot",
            "hitMask",
            "durationMs",
            "interruptible",
            "weight",
            "cooldownMs",
            "nextStates",
        ):
            if field not in entry:
                errors.append(f"{asset_id}: missing required field {field}")
        source_size = entry.get("sourceSize", manifest.get("sourceSize"))
        if (
            not isinstance(source_size, list)
            or len(source_size) != 2
            or not all(isinstance(value, int) and value > 0 for value in source_size)
        ):
            errors.append(f"{asset_id}: sourceSize must be two positive integers")
            continue
        if not _coordinate(entry.get("pivot")):
            errors.append(f"{asset_id}: pivot must be two numbers from 0 to 1")
        present_anchors = [field for field in ANCHOR_FIELDS if field in entry]
        if asset_id != "falling.reach" and not present_anchors:
            errors.append(f"{asset_id}: at least one contact anchor is required")
        for field in present_anchors:
            if not _coordinate(entry[field]):
                errors.append(f"{asset_id}: {field} must be two numbers from 0 to 1")
        duration = entry.get("durationMs")
        if not isinstance(duration, int) or isinstance(duration, bool) or duration <= 0:
            errors.append(f"{asset_id}: durationMs must be a positive integer")
        if not isinstance(entry.get("interruptible"), bool):
            errors.append(f"{asset_id}: interruptible must be boolean")
        weight = entry.get("weight")
        if not _is_number(weight) or weight < 0:
            errors.append(f"{asset_id}: weight must be a non-negative number")
        cooldown = entry.get("cooldownMs")
        if not isinstance(cooldown, int) or isinstance(cooldown, bool) or cooldown < 0:
            errors.append(f"{asset_id}: cooldownMs must be a non-negative integer")
        next_states = entry.get("nextStates")
        if not isinstance(next_states, list) or not all(isinstance(value, str) for value in next_states):
            errors.append(f"{asset_id}: nextStates must be an array of strings")

        file_name = entry.get("file")
        sprite_path = _safe_asset_path(asset_root, file_name, f"{asset_id}.file", errors)
        if sprite_path is None:
            continue
        try:
            sprite = read_png(sprite_path)
        except PngError as error:
            errors.append(f"{asset_id}: cannot read sprite: {error}")
            continue
        if sprite.mode != "RGBA" or sprite.color_type != 6:
            errors.append(f"{asset_id}: sprite must be true RGBA PNG, got {sprite.mode}")
        if [sprite.width, sprite.height] != source_size:
            errors.append(
                f"{asset_id}: PNG is {sprite.width}x{sprite.height}, sourceSize is {source_size}"
            )
        bounds = alpha_bounds(sprite, threshold=2)
        if bounds is None:
            errors.append(f"{asset_id}: sprite has no visible pixels")
            continue
        alpha_values = sprite.as_rgba().pixels[3::4]
        if min(alpha_values) != 0:
            errors.append(f"{asset_id}: sprite has no fully transparent pixels")
        if max(alpha_values) != 255:
            warnings.append(f"{asset_id}: sprite has no fully opaque pixels")
        declared = entry.get("visualBounds")
        if (
            not isinstance(declared, list)
            or len(declared) != 4
            or not all(isinstance(value, int) and not isinstance(value, bool) for value in declared)
            or (len(declared) == 4 and (declared[0] < 0 or declared[1] < 0 or declared[2] <= 0 or declared[3] <= 0))
        ):
            errors.append(f"{asset_id}: visualBounds must be [x, y, width, height] positive integers")
        if declared != bounds.xywh():
            errors.append(
                f"{asset_id}: visualBounds {declared} does not match measured {bounds.xywh()}"
            )
        margins = (
            bounds.left,
            bounds.top,
            sprite.width - bounds.right,
            sprite.height - bounds.bottom,
        )
        if min(margins) < minimum_margin:
            errors.append(
                f"{asset_id}: visual margin {min(margins)}px is below {minimum_margin}px"
            )
        mask_path = _safe_asset_path(
            asset_root, entry.get("hitMask"), f"{asset_id}.hitMask", errors
        )
        if mask_path is not None:
            try:
                mask = read_png(mask_path)
                if mask.mode != "L":
                    errors.append(f"{asset_id}: hit mask must be grayscale L PNG")
                elif (mask.width, mask.height) != (sprite.width, sprite.height):
                    errors.append(f"{asset_id}: hit mask dimensions differ from sprite")
                elif any(value not in (0, 255) for value in mask.pixels):
                    errors.append(f"{asset_id}: hit mask must be binary (0 or 255)")
                else:
                    expected_mask = bytes(
                        255 if alpha >= mask_threshold else 0
                        for alpha in sprite.as_rgba().pixels[3::4]
                    )
                    mismatch_count = sum(
                        expected != actual
                        for expected, actual in zip(expected_mask, mask.pixels)
                    )
                    if mismatch_count:
                        errors.append(
                            f"{asset_id}: hit mask differs from alpha threshold at "
                            f"{mismatch_count} pixels"
                        )
            except PngError as error:
                errors.append(f"{asset_id}: cannot read hit mask: {error}")
    missing_ids = set(ASSET_IDS) - seen
    extra_ids = seen - set(ASSET_IDS)
    if missing_ids:
        errors.append("missing asset IDs: " + ", ".join(sorted(missing_ids)))
    if extra_ids:
        warnings.append("unrecognised asset IDs: " + ", ".join(sorted(extra_ids)))
    return errors, warnings


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    extract = subparsers.add_parser("extract", help="extract and normalise a 4x5 RGBA action sheet")
    extract.add_argument("sheet", type=Path)
    extract.add_argument("--asset-root", type=Path, default=Path("assets"))
    extract.add_argument("--canvas-size", type=int, default=512)
    extract.add_argument("--target-face-width", type=int, default=98)
    extract.add_argument("--safety-margin", type=int, default=48)
    masks = subparsers.add_parser("masks", help="generate binary hit masks")
    masks.add_argument("--asset-root", type=Path, default=Path("assets"))
    masks.add_argument("--threshold", type=int, default=16)
    contact = subparsers.add_parser("contact-sheet", help="generate a checkerboard review sheet")
    contact.add_argument("output", type=Path)
    contact.add_argument("--asset-root", type=Path, default=Path("assets"))
    contact.add_argument("--cell-size", type=int, default=256)
    lint = subparsers.add_parser("lint", help="validate manifest, sprites, and hit masks")
    lint.add_argument("--asset-root", type=Path, default=Path("assets"))
    lint.add_argument("--minimum-margin", type=int, default=48)
    all_command = subparsers.add_parser("all", help="generate masks/contact sheet and lint existing assets")
    all_command.add_argument("--asset-root", type=Path, default=Path("assets"))
    all_command.add_argument("--contact-sheet", type=Path, default=Path("assets/review/contact-sheet.png"))
    return parser


def main(argv: Iterable[str] | None = None) -> int:
    arguments = _build_parser().parse_args(argv)
    try:
        if arguments.command == "extract":
            extract_sheet(
                arguments.sheet,
                arguments.asset_root,
                canvas_size=arguments.canvas_size,
                target_face_width=arguments.target_face_width,
                safety_margin=arguments.safety_margin,
            )
        elif arguments.command == "masks":
            if not 0 <= arguments.threshold <= 255:
                raise ValueError("threshold must be between 0 and 255")
            generate_masks(arguments.asset_root, arguments.threshold)
        elif arguments.command == "contact-sheet":
            generate_contact_sheet(arguments.asset_root, arguments.output, arguments.cell_size)
        elif arguments.command == "all":
            generate_masks(arguments.asset_root)
            generate_contact_sheet(arguments.asset_root, arguments.contact_sheet)
            errors, warnings = lint_assets(arguments.asset_root)
            for warning in warnings:
                print(f"warning: {warning}", file=sys.stderr)
            for error in errors:
                print(f"error: {error}", file=sys.stderr)
            return 1 if errors else 0
        elif arguments.command == "lint":
            errors, warnings = lint_assets(
                arguments.asset_root, minimum_margin=arguments.minimum_margin
            )
            for warning in warnings:
                print(f"warning: {warning}", file=sys.stderr)
            for error in errors:
                print(f"error: {error}", file=sys.stderr)
            if errors:
                return 1
            print(f"asset lint passed ({len(ASSET_IDS)} sprites)")
    except (OSError, PngError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
