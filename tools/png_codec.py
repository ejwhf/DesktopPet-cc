"""Small, dependency-free PNG reader/writer used by the asset pipeline.

The project deliberately keeps its verification tooling runnable on a clean
Python installation.  This codec is not meant to replace Pillow generally; it
supports the subset needed by our authored assets: non-interlaced, 8-bit PNGs
in L, LA, RGB, RGBA, or indexed colour.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import struct
import zlib


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


class PngError(ValueError):
    """Raised when a PNG is malformed or uses an unsupported encoding."""


@dataclass(frozen=True)
class PngImage:
    width: int
    height: int
    mode: str
    pixels: bytes
    color_type: int
    bit_depth: int = 8

    @property
    def channels(self) -> int:
        return {"L": 1, "LA": 2, "RGB": 3, "RGBA": 4}[self.mode]

    def as_rgba(self) -> "PngImage":
        if self.mode == "RGBA":
            return self
        output = bytearray(self.width * self.height * 4)
        channels = self.channels
        for index in range(self.width * self.height):
            source = index * channels
            target = index * 4
            if self.mode == "RGB":
                output[target : target + 3] = self.pixels[source : source + 3]
                output[target + 3] = 255
            elif self.mode == "L":
                value = self.pixels[source]
                output[target : target + 4] = bytes((value, value, value, 255))
            elif self.mode == "LA":
                value, alpha = self.pixels[source : source + 2]
                output[target : target + 4] = bytes((value, value, value, alpha))
        return PngImage(self.width, self.height, "RGBA", bytes(output), 6, 8)


def _paeth(left: int, above: int, upper_left: int) -> int:
    prediction = left + above - upper_left
    left_distance = abs(prediction - left)
    above_distance = abs(prediction - above)
    upper_left_distance = abs(prediction - upper_left)
    if left_distance <= above_distance and left_distance <= upper_left_distance:
        return left
    if above_distance <= upper_left_distance:
        return above
    return upper_left


def _unfilter(raw: bytes, width: int, height: int, channels: int) -> bytes:
    row_bytes = width * channels
    expected = height * (row_bytes + 1)
    if len(raw) != expected:
        raise PngError(f"decompressed data has {len(raw)} bytes; expected {expected}")

    output = bytearray(height * row_bytes)
    source_offset = 0
    for y in range(height):
        filter_type = raw[source_offset]
        source_offset += 1
        row_offset = y * row_bytes
        for x in range(row_bytes):
            value = raw[source_offset + x]
            left = output[row_offset + x - channels] if x >= channels else 0
            above = output[row_offset - row_bytes + x] if y else 0
            upper_left = (
                output[row_offset - row_bytes + x - channels]
                if y and x >= channels
                else 0
            )
            if filter_type == 0:
                reconstructed = value
            elif filter_type == 1:
                reconstructed = value + left
            elif filter_type == 2:
                reconstructed = value + above
            elif filter_type == 3:
                reconstructed = value + ((left + above) // 2)
            elif filter_type == 4:
                reconstructed = value + _paeth(left, above, upper_left)
            else:
                raise PngError(f"unsupported PNG filter {filter_type}")
            output[row_offset + x] = reconstructed & 0xFF
        source_offset += row_bytes
    return bytes(output)


def decode_png(data: bytes) -> PngImage:
    if not data.startswith(PNG_SIGNATURE):
        raise PngError("not a PNG file")

    offset = len(PNG_SIGNATURE)
    header: tuple[int, int, int, int, int, int, int] | None = None
    compressed = bytearray()
    palette: bytes | None = None
    transparency: bytes | None = None
    saw_end = False

    while offset < len(data):
        if offset + 12 > len(data):
            raise PngError("truncated PNG chunk")
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        chunk_type = data[offset + 4 : offset + 8]
        chunk_start = offset + 8
        chunk_end = chunk_start + length
        if chunk_end + 4 > len(data):
            raise PngError("truncated PNG chunk data")
        chunk_data = data[chunk_start:chunk_end]
        expected_crc = struct.unpack(">I", data[chunk_end : chunk_end + 4])[0]
        actual_crc = zlib.crc32(chunk_type)
        actual_crc = zlib.crc32(chunk_data, actual_crc) & 0xFFFFFFFF
        if actual_crc != expected_crc:
            name = chunk_type.decode("ascii", errors="replace")
            raise PngError(f"CRC mismatch in {name} chunk")

        if chunk_type == b"IHDR":
            if header is not None or length != 13:
                raise PngError("invalid IHDR chunk")
            header = struct.unpack(">IIBBBBB", chunk_data)
        elif chunk_type == b"PLTE":
            palette = chunk_data
        elif chunk_type == b"tRNS":
            transparency = chunk_data
        elif chunk_type == b"IDAT":
            compressed.extend(chunk_data)
        elif chunk_type == b"IEND":
            saw_end = True
            break
        offset = chunk_end + 4

    if header is None:
        raise PngError("PNG has no IHDR chunk")
    if not saw_end:
        raise PngError("PNG has no IEND chunk")

    width, height, bit_depth, color_type, compression, filter_method, interlace = header
    if width <= 0 or height <= 0:
        raise PngError("PNG dimensions must be positive")
    if bit_depth != 8:
        raise PngError(f"unsupported bit depth {bit_depth}; expected 8")
    if compression != 0 or filter_method != 0:
        raise PngError("unsupported PNG compression or filter method")
    if interlace != 0:
        raise PngError("interlaced PNGs are not supported")

    channel_counts = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
    if color_type not in channel_counts:
        raise PngError(f"unsupported PNG colour type {color_type}")
    try:
        raw = zlib.decompress(bytes(compressed))
    except zlib.error as error:
        raise PngError(f"invalid IDAT stream: {error}") from error
    unpacked = _unfilter(raw, width, height, channel_counts[color_type])

    if color_type == 3:
        if palette is None or len(palette) % 3:
            raise PngError("indexed PNG has no valid palette")
        rgba = bytearray(width * height * 4)
        palette_entries = len(palette) // 3
        for index, palette_index in enumerate(unpacked):
            if palette_index >= palette_entries:
                raise PngError("pixel references an invalid palette entry")
            target = index * 4
            palette_offset = palette_index * 3
            rgba[target : target + 3] = palette[palette_offset : palette_offset + 3]
            rgba[target + 3] = (
                transparency[palette_index]
                if transparency is not None and palette_index < len(transparency)
                else 255
            )
        return PngImage(width, height, "RGBA", bytes(rgba), color_type, bit_depth)

    mode = {0: "L", 2: "RGB", 4: "LA", 6: "RGBA"}[color_type]
    return PngImage(width, height, mode, unpacked, color_type, bit_depth)


def read_png(path: str | Path) -> PngImage:
    try:
        return decode_png(Path(path).read_bytes())
    except OSError as error:
        raise PngError(str(error)) from error


def _chunk(chunk_type: bytes, payload: bytes) -> bytes:
    crc = zlib.crc32(chunk_type)
    crc = zlib.crc32(payload, crc) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + chunk_type + payload + struct.pack(">I", crc)


def encode_png(
    width: int,
    height: int,
    mode: str,
    pixels: bytes | bytearray,
    *,
    compression_level: int = 9,
) -> bytes:
    color_types = {"L": (1, 0), "LA": (2, 4), "RGB": (3, 2), "RGBA": (4, 6)}
    if mode not in color_types:
        raise PngError(f"unsupported output mode {mode!r}")
    if width <= 0 or height <= 0:
        raise PngError("PNG dimensions must be positive")
    channels, color_type = color_types[mode]
    expected = width * height * channels
    if len(pixels) != expected:
        raise PngError(f"pixel buffer has {len(pixels)} bytes; expected {expected}")
    if not 0 <= compression_level <= 9:
        raise PngError("compression level must be between 0 and 9")

    row_bytes = width * channels
    filtered = bytearray()
    for y in range(height):
        filtered.append(0)
        start = y * row_bytes
        filtered.extend(pixels[start : start + row_bytes])
    header = struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)
    return b"".join(
        (
            PNG_SIGNATURE,
            _chunk(b"IHDR", header),
            _chunk(b"IDAT", zlib.compress(bytes(filtered), compression_level)),
            _chunk(b"IEND", b""),
        )
    )


def write_png(path: str | Path, image: PngImage, *, overwrite: bool = True) -> None:
    target = Path(path)
    if target.exists() and not overwrite:
        raise FileExistsError(f"refusing to overwrite {target}")
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(encode_png(image.width, image.height, image.mode, image.pixels))


def crop(image: PngImage, box: tuple[int, int, int, int]) -> PngImage:
    """Return the half-open ``(left, top, right, bottom)`` region."""
    left, top, right, bottom = box
    if not (0 <= left < right <= image.width and 0 <= top < bottom <= image.height):
        raise PngError(f"crop box {box} is outside {image.width}x{image.height}")
    channels = image.channels
    row_bytes = (right - left) * channels
    output = bytearray((right - left) * (bottom - top) * channels)
    for target_y, source_y in enumerate(range(top, bottom)):
        source = (source_y * image.width + left) * channels
        target = target_y * row_bytes
        output[target : target + row_bytes] = image.pixels[source : source + row_bytes]
    return PngImage(right - left, bottom - top, image.mode, bytes(output), image.color_type)


def resize_rgba(image: PngImage, width: int, height: int) -> PngImage:
    """Bilinear RGBA resize using premultiplied-alpha interpolation."""
    source = image.as_rgba()
    if width <= 0 or height <= 0:
        raise PngError("resize dimensions must be positive")
    if width == source.width and height == source.height:
        return source
    output = bytearray(width * height * 4)
    x_scale = source.width / width
    y_scale = source.height / height
    pixels = source.pixels
    for target_y in range(height):
        source_y = (target_y + 0.5) * y_scale - 0.5
        y0 = max(0, min(source.height - 1, int(source_y)))
        y1 = min(source.height - 1, y0 + 1)
        fy = max(0.0, min(1.0, source_y - y0))
        for target_x in range(width):
            source_x = (target_x + 0.5) * x_scale - 0.5
            x0 = max(0, min(source.width - 1, int(source_x)))
            x1 = min(source.width - 1, x0 + 1)
            fx = max(0.0, min(1.0, source_x - x0))
            weights = (
                ((1.0 - fx) * (1.0 - fy), x0, y0),
                (fx * (1.0 - fy), x1, y0),
                ((1.0 - fx) * fy, x0, y1),
                (fx * fy, x1, y1),
            )
            alpha = 0.0
            premultiplied = [0.0, 0.0, 0.0]
            for weight, sample_x, sample_y in weights:
                offset = (sample_y * source.width + sample_x) * 4
                sample_alpha = pixels[offset + 3] / 255.0
                alpha += weight * sample_alpha
                for channel in range(3):
                    premultiplied[channel] += weight * pixels[offset + channel] * sample_alpha
            target = (target_y * width + target_x) * 4
            if alpha > 0:
                for channel in range(3):
                    output[target + channel] = max(
                        0, min(255, round(premultiplied[channel] / alpha))
                    )
            output[target + 3] = max(0, min(255, round(alpha * 255)))
    return PngImage(width, height, "RGBA", bytes(output), 6)


def paste_rgba(destination: bytearray, width: int, height: int, source: PngImage, x: int, y: int) -> None:
    """Alpha-composite ``source`` over an RGBA destination buffer in place."""
    foreground = source.as_rgba()
    if len(destination) != width * height * 4:
        raise PngError("destination RGBA buffer has the wrong length")
    for source_y in range(foreground.height):
        target_y = y + source_y
        if not 0 <= target_y < height:
            continue
        for source_x in range(foreground.width):
            target_x = x + source_x
            if not 0 <= target_x < width:
                continue
            source_offset = (source_y * foreground.width + source_x) * 4
            target_offset = (target_y * width + target_x) * 4
            source_alpha = foreground.pixels[source_offset + 3] / 255.0
            if source_alpha <= 0:
                continue
            target_alpha = destination[target_offset + 3] / 255.0
            output_alpha = source_alpha + target_alpha * (1.0 - source_alpha)
            for channel in range(3):
                source_value = foreground.pixels[source_offset + channel]
                target_value = destination[target_offset + channel]
                destination[target_offset + channel] = round(
                    (
                        source_value * source_alpha
                        + target_value * target_alpha * (1.0 - source_alpha)
                    )
                    / output_alpha
                )
            destination[target_offset + 3] = round(output_alpha * 255)
