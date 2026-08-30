from __future__ import annotations

import json
from pathlib import Path
import tempfile
import unittest

from tools.asset_pipeline import (
    ASSET_IDS,
    alpha_bounds,
    alpha_components,
    detect_face,
    generate_masks,
    lint_assets,
    normalise_standing_height,
    split_component_sheet,
    split_sheet,
)
from tools.png_codec import (
    PngError,
    PngImage,
    decode_png,
    encode_png,
    read_png,
    resize_rgba,
    write_png,
)


def rgba_image(width: int, height: int, fill: tuple[int, int, int, int] = (0, 0, 0, 0)) -> PngImage:
    return PngImage(width, height, "RGBA", bytes(fill) * (width * height), 6)


def fixture_manifest(asset_id: str, source_size: int, bounds: list[int]) -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "sourceSize": [source_size, source_size],
        "hitMaskAlphaThreshold": 16,
        "assets": [
            {
                "id": asset_id,
                "file": f"sprites/{asset_id}.png",
                "sourceSize": [source_size, source_size],
                "visualBounds": bounds,
                "pivot": [0.5, 0.5],
                "groundAnchor": [0.5, 0.9],
                "hitMask": f"masks/{asset_id}.png",
                "durationMs": 1200,
                "interruptible": True,
                "weight": 1,
                "cooldownMs": 0,
                "nextStates": [],
            }
        ],
    }


class PngCodecTests(unittest.TestCase):
    def test_rgba_round_trip(self) -> None:
        pixels = bytes(
            (
                255,
                0,
                0,
                0,
                0,
                255,
                0,
                85,
                0,
                0,
                255,
                170,
                255,
                255,
                255,
                255,
            )
        )
        decoded = decode_png(encode_png(2, 2, "RGBA", pixels))
        self.assertEqual((decoded.width, decoded.height, decoded.mode), (2, 2, "RGBA"))
        self.assertEqual(decoded.pixels, pixels)

    def test_grayscale_round_trip(self) -> None:
        decoded = decode_png(encode_png(3, 1, "L", bytes((0, 127, 255))))
        self.assertEqual(decoded.mode, "L")
        self.assertEqual(decoded.pixels, bytes((0, 127, 255)))

    def test_bad_signature_is_rejected(self) -> None:
        with self.assertRaises(PngError):
            decode_png(b"not a png")

    def test_resize_uses_premultiplied_alpha(self) -> None:
        source = PngImage(
            2,
            1,
            "RGBA",
            bytes((255, 0, 0, 255, 0, 255, 0, 0)),
            6,
        )
        resized = resize_rgba(source, 1, 1)
        red, green, blue, alpha = resized.pixels
        self.assertEqual((red, green, blue), (255, 0, 0))
        self.assertIn(alpha, range(127, 129))


class AnalysisTests(unittest.TestCase):
    def test_alpha_bounds_are_half_open_and_return_xywh(self) -> None:
        pixels = bytearray(6 * 5 * 4)
        for y in range(1, 4):
            for x in range(2, 5):
                pixels[(y * 6 + x) * 4 + 3] = 255
        bounds = alpha_bounds(PngImage(6, 5, "RGBA", bytes(pixels), 6))
        self.assertIsNotNone(bounds)
        assert bounds is not None
        self.assertEqual(bounds.xywh(), [2, 1, 3, 3])

    def test_split_sheet_uses_all_pixels_when_not_evenly_divisible(self) -> None:
        sheet = rgba_image(11, 7, (1, 2, 3, 4))
        cells = split_sheet(sheet, columns=4, rows=2, count=8)
        self.assertEqual(sum(cell.width * cell.height for cell in cells), 11 * 7)
        self.assertEqual(len(cells), 8)

    def test_split_sheet_uses_transparent_gutters_instead_of_equal_rows(self) -> None:
        width, height = 8, 16
        pixels = bytearray(width * height * 4)
        # Two deliberately uneven rows separated by a transparent gutter at
        # y=6..7; arithmetic half-height slicing would be wrong.
        for y in range(1, 6):
            for x in range(1, 4):
                pixels[(y * width + x) * 4 + 3] = 255
        for y in range(8, 15):
            for x in range(1, 4):
                pixels[(y * width + x) * 4 + 3] = 255
        cells = split_sheet(PngImage(width, height, "RGBA", bytes(pixels), 6), columns=1, rows=2, count=2)
        self.assertEqual([cell.height for cell in cells], [7, 9])
        self.assertEqual(alpha_bounds(cells[0]).bottom, 6)  # type: ignore[union-attr]
        self.assertEqual(alpha_bounds(cells[1]).top, 1)  # type: ignore[union-attr]

    def test_detect_face_selects_largest_skin_component(self) -> None:
        pixels = bytearray(20 * 20 * 4)
        skin = bytes((220, 150, 100, 255))
        for y in range(2, 9):
            for x in range(4, 13):
                pixels[(y * 20 + x) * 4 : (y * 20 + x) * 4 + 4] = skin
        for y in range(15, 18):
            for x in range(1, 4):
                pixels[(y * 20 + x) * 4 : (y * 20 + x) * 4 + 4] = skin
        face = detect_face(PngImage(20, 20, "RGBA", bytes(pixels), 6))
        self.assertEqual(face.xywh(), [4, 2, 9, 7])

    def test_component_sheet_partitions_source_without_cross_contamination(self) -> None:
        width, height = 30, 20
        pixels = bytearray(width * height * 4)
        colours = ((255, 0, 0, 255), (0, 255, 0, 255))
        for start_x, colour in zip((2, 20), colours):
            for y in range(2, 12):
                for x in range(start_x, start_x + 6):
                    offset = (y * width + x) * 4
                    pixels[offset : offset + 4] = bytes(colour)
        # A disconnected effect belongs to the second/main green component.
        effect_offset = (4 * width + 28) * 4
        pixels[effect_offset : effect_offset + 4] = bytes((0, 0, 255, 255))
        source = PngImage(width, height, "RGBA", bytes(pixels), 6)
        cells, mains = split_component_sheet(
            source, main_pixel_threshold=50, row_counts=(2,)
        )
        self.assertEqual(len(mains), 2)
        source_visible = sum(alpha > 0 for alpha in source.pixels[3::4])
        output_visible = sum(
            alpha > 0 for cell in cells for alpha in cell.pixels[3::4]
        )
        self.assertEqual(output_visible, source_visible)
        first_colours = set(zip(*(iter(cells[0].pixels),) * 4))
        second_colours = set(zip(*(iter(cells[1].pixels),) * 4))
        self.assertNotIn(colours[1], first_colours)
        self.assertNotIn(colours[0], second_colours)
        self.assertIn((0, 0, 255, 255), second_colours)

    def test_standing_height_normalisation_preserves_ground_and_center(self) -> None:
        width = height = 20
        pixels = bytearray(width * height * 4)
        for y in range(5, 15):
            for x in range(7, 13):
                offset = (y * width + x) * 4
                pixels[offset : offset + 4] = bytes((20, 30, 40, 255))
        source = PngImage(width, height, "RGBA", bytes(pixels), 6)
        result = normalise_standing_height(source, anchor_kind="ground", target_height=8)
        bounds = alpha_bounds(result, 2)
        self.assertIsNotNone(bounds)
        assert bounds is not None
        self.assertEqual(bounds.height, 8)
        self.assertEqual(bounds.bottom, 15)
        self.assertLessEqual(abs((bounds.left + bounds.right) - 20), 1)


class LintTests(unittest.TestCase):
    def _write_fixture(self, root: Path, *, corrupt_mask: bool = False) -> None:
        size = 12
        pixels = bytearray(size * size * 4)
        for y in range(3, 9):
            for x in range(3, 9):
                offset = (y * size + x) * 4
                pixels[offset : offset + 4] = bytes((100, 120, 140, 255))
        sprite = PngImage(size, size, "RGBA", bytes(pixels), 6)
        write_png(root / f"sprites/{ASSET_IDS[0]}.png", sprite)
        document = fixture_manifest(ASSET_IDS[0], size, [3, 3, 6, 6])
        (root / "manifest").mkdir(parents=True)
        (root / "manifest/assets.json").write_text(json.dumps(document), encoding="utf-8")
        generate_masks(root)
        if corrupt_mask:
            mask_path = root / f"masks/{ASSET_IDS[0]}.png"
            mask = bytearray(read_png(mask_path).pixels)
            mask[0] = 255
            write_png(mask_path, PngImage(size, size, "L", bytes(mask), 0))

    def test_lint_validates_manifest_and_pixels(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self._write_fixture(root)
            errors, warnings = lint_assets(root, minimum_margin=3)
            # This deliberately minimal fixture has only one of the required
            # production IDs; all its per-asset checks should still pass.
            self.assertEqual(warnings, [])
            self.assertEqual(
                errors,
                [
                    "manifest must contain exactly 18 assets, found 1",
                    "missing asset IDs: " + ", ".join(sorted(set(ASSET_IDS[1:]))),
                ],
            )

    def test_lint_detects_mask_alpha_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self._write_fixture(root, corrupt_mask=True)
            errors, _ = lint_assets(root, minimum_margin=3)
            self.assertTrue(any("hit mask differs from alpha threshold" in error for error in errors))

    def test_lint_rejects_path_traversal(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            document = fixture_manifest(ASSET_IDS[0], 12, [3, 3, 6, 6])
            document["assets"][0]["file"] = "../outside.png"  # type: ignore[index]
            (root / "manifest").mkdir(parents=True)
            (root / "manifest/assets.json").write_text(json.dumps(document), encoding="utf-8")
            errors, _ = lint_assets(root)
            self.assertTrue(any("must stay inside the asset root" in error for error in errors))


class ProductionAssetTests(unittest.TestCase):
    def test_v2_sheet_has_exactly_18_main_components_and_partitions_losslessly(self) -> None:
        project_root = Path(__file__).resolve().parents[1]
        sheet_path = project_root / "assets/sheets/character-actions-rgba-v2.png"
        if not sheet_path.exists():
            self.skipTest("v2 action sheet is unavailable")
        source = read_png(sheet_path)
        components = alpha_components(source)
        self.assertEqual(sum(item.pixel_count >= 10_000 for item in components), 18)
        cells, mains = split_component_sheet(source)
        self.assertEqual(len(mains), 18)
        self.assertTrue(
            all(
                sum(item.pixel_count >= 10_000 for item in alpha_components(cell)) == 1
                for cell in cells
            )
        )
        self.assertEqual(
            sum(alpha > 0 for alpha in source.as_rgba().pixels[3::4]),
            sum(alpha > 0 for cell in cells for alpha in cell.pixels[3::4]),
        )

    def test_checked_in_assets_pass_lint(self) -> None:
        project_root = Path(__file__).resolve().parents[1]
        asset_root = project_root / "assets"
        if not (asset_root / "manifest/assets.json").exists():
            self.skipTest("production assets have not been generated")
        errors, warnings = lint_assets(asset_root)
        self.assertEqual(warnings, [])
        self.assertEqual(errors, [])


if __name__ == "__main__":
    unittest.main()
