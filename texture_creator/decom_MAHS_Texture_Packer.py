#!/usr/bin/env python3
"""Pack common PBR texture maps into Unity/MAHS-ready outputs."""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    import numpy as np
    from PIL import Image, ImageEnhance
except ImportError:
    print(
        "Missing dependency. Install with:\n"
        "  python -m pip install Pillow numpy\n"
        "Then run this command again.",
        file=sys.stderr,
    )
    raise SystemExit(1)


MAP_KEYWORDS = {
    "albedo": ("basecolor", "base_color", "diffuse", "diff", "albedo", "color", "col"),
    "ao": ("ambientocclusion", "ambient_occlusion", "occlusion", "ao"),
    "roughness": ("roughness", "rough", "rgh"),
    "height": ("displacement", "height", "disp", "displace"),
    "normal": ("normal_gl", "normalgl", "normal", "nor", "nrm"),
    "metalness": ("metalness", "metallic", "metal", "mtl"),
}

IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".tga"}

PRESETS = {
    "none": {
        "albedo_saturation": 1.0,
        "albedo_brightness": 1.0,
        "height_contrast": 1.0,
        "ao_strength": 1.0,
        "roughness_bias": 0.0,
    },
    "forest_floor": {
        "albedo_saturation": 0.85,
        "albedo_brightness": 0.95,
        "height_contrast": 0.65,
        "ao_strength": 0.80,
        "roughness_bias": 0.03,
    },
}


@dataclass(frozen=True)
class Selection:
    albedo: Path | None
    ao: Path | None
    roughness: Path | None
    height: Path | None
    normal: Path | None
    metalness: Path | None


def normalized_stem(path: Path) -> str:
    return path.stem.lower().replace("-", "_").replace(" ", "_")


def score_file(path: Path, map_type: str) -> tuple[int, int, str]:
    stem = normalized_stem(path)
    keywords = MAP_KEYWORDS[map_type]
    best = 0

    for index, keyword in enumerate(keywords):
        if keyword in stem:
            # Earlier keywords are more specific/preferred.
            best = max(best, 100 - index)

    if map_type == "normal":
        if "dx" in stem or "directx" in stem:
            best -= 15
        if "gl" in stem or "opengl" in stem:
            best += 15

    return best, -len(stem), path.name.lower()


def find_maps(input_dir: Path) -> Selection:
    files = [
        path
        for path in input_dir.iterdir()
        if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
    ]
    choices: dict[str, Path | None] = {}

    for map_type in MAP_KEYWORDS:
        ranked = [
            (score_file(path, map_type), path)
            for path in files
            if score_file(path, map_type)[0] > 0
        ]
        ranked.sort(key=lambda item: item[0], reverse=True)
        choices[map_type] = ranked[0][1] if ranked else None

    return Selection(**choices)


def resize_image(image: Image.Image, size: int, mode: str) -> Image.Image:
    if image.mode != mode:
        image = image.convert(mode)
    if image.size != (size, size):
        image = image.resize((size, size), Image.Resampling.LANCZOS)
    return image


def load_rgb(path: Path, size: int) -> Image.Image:
    with Image.open(path) as image:
        return resize_image(image, size, "RGB").copy()


def load_gray(path: Path | None, size: int, default: int) -> np.ndarray:
    if path is None:
        return np.full((size, size), default, dtype=np.uint8)

    with Image.open(path) as image:
        if image.mode == "L":
            gray = resize_image(image, size, "L").copy()
            return np.asarray(gray, dtype=np.uint8)

        source = image.copy()

    array = np.asarray(source)
    if array.ndim == 3:
        gray = source.convert("L")
        if gray.size != (size, size):
            gray = gray.resize((size, size), Image.Resampling.LANCZOS)
        return np.asarray(gray, dtype=np.uint8)

    if np.issubdtype(array.dtype, np.integer):
        max_value = np.iinfo(array.dtype).max
        if max_value > 255:
            array = np.rint((array.astype(np.float32) / max_value) * 255.0)
            array = np.clip(array, 0, 255).astype(np.uint8)
            gray = Image.fromarray(array, "L")
        else:
            gray = Image.fromarray(array.astype(np.uint8), "L")
    else:
        array = np.clip(array.astype(np.float32), 0.0, 1.0) * 255.0
        gray = Image.fromarray(array.astype(np.uint8), "L")

    if gray.size != (size, size):
        gray = gray.resize((size, size), Image.Resampling.LANCZOS)
    return np.asarray(gray, dtype=np.uint8)


def adjust_albedo(image: Image.Image, saturation: float, brightness: float) -> Image.Image:
    if saturation != 1.0:
        image = ImageEnhance.Color(image).enhance(saturation)
    if brightness != 1.0:
        image = ImageEnhance.Brightness(image).enhance(brightness)
    return image


def adjust_contrast_channel(channel: np.ndarray, contrast: float) -> np.ndarray:
    values = channel.astype(np.float32)
    values = 128.0 + ((values - 128.0) * contrast)
    return np.clip(values, 0, 255).astype(np.uint8)


def soften_ao(channel: np.ndarray, strength: float) -> np.ndarray:
    values = channel.astype(np.float32)
    values = 255.0 - ((255.0 - values) * strength)
    return np.clip(values, 0, 255).astype(np.uint8)


def bias_roughness(channel: np.ndarray, bias: float) -> np.ndarray:
    values = channel.astype(np.float32) + (bias * 255.0)
    return np.clip(values, 0, 255).astype(np.uint8)


def pack_mahs(
    metalness: np.ndarray,
    ao: np.ndarray,
    height: np.ndarray,
    roughness: np.ndarray,
    *,
    height_contrast: float,
    ao_strength: float,
    roughness_bias: float,
) -> Image.Image:
    height_out = adjust_contrast_channel(height, height_contrast)
    ao_out = soften_ao(ao, ao_strength)
    rough_out = bias_roughness(roughness, roughness_bias)
    alpha = 255 - rough_out
    packed = np.dstack([metalness, ao_out, height_out, alpha]).astype(np.uint8)
    return Image.fromarray(packed, "RGBA")


def print_selection(selection: Selection) -> None:
    for field in ("albedo", "normal", "metalness", "ao", "height", "roughness"):
        value = getattr(selection, field)
        print(f"{field:10}: {value if value else '(missing; default used if optional)'}")


def process(args: argparse.Namespace) -> int:
    input_dir = Path(args.input)
    output_dir = Path(args.output)
    if not input_dir.is_dir():
        print(f"Input folder does not exist: {input_dir}", file=sys.stderr)
        return 2

    preset_values = PRESETS[args.preset].copy()
    for key in preset_values:
        value = getattr(args, key)
        if value is not None:
            preset_values[key] = value

    selection = find_maps(input_dir)
    if args.verbose or args.dry_run:
        print_selection(selection)

    if selection.albedo is None:
        print("No albedo/diffuse/basecolor map found.", file=sys.stderr)
        return 2
    if selection.normal is None:
        print("No normal map found.", file=sys.stderr)
        return 2
    if selection.ao is None:
        print("No AO map found.", file=sys.stderr)
        return 2
    if selection.roughness is None:
        print("No roughness map found.", file=sys.stderr)
        return 2
    if selection.height is None:
        print("No height/displacement map found.", file=sys.stderr)
        return 2

    if args.dry_run:
        return 0

    output_dir.mkdir(parents=True, exist_ok=True)

    albedo = adjust_albedo(
        load_rgb(selection.albedo, args.size),
        preset_values["albedo_saturation"],
        preset_values["albedo_brightness"],
    )
    normal = load_rgb(selection.normal, args.size)

    metalness = load_gray(selection.metalness, args.size, 0)
    ao = load_gray(selection.ao, args.size, 255)
    height = load_gray(selection.height, args.size, 128)
    roughness = load_gray(selection.roughness, args.size, 255)

    mahs = pack_mahs(
        metalness,
        ao,
        height,
        roughness,
        height_contrast=preset_values["height_contrast"],
        ao_strength=preset_values["ao_strength"],
        roughness_bias=preset_values["roughness_bias"],
    )

    base = args.name
    albedo.save(output_dir / f"{base}_Albedo.png")
    normal.save(output_dir / f"{base}_Normal.png")
    Image.fromarray(height, "L").save(output_dir / f"{base}_Height.png")
    mahs.save(output_dir / f"{base}_MAHS.png")

    if args.verbose:
        print(f"Wrote: {output_dir / f'{base}_Albedo.png'}")
        print(f"Wrote: {output_dir / f'{base}_Normal.png'}")
        print(f"Wrote: {output_dir / f'{base}_Height.png'}")
        print(f"Wrote: {output_dir / f'{base}_MAHS.png'}")

    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Convert a downloaded PBR texture folder into Unity/MAHS texture outputs."
    )
    parser.add_argument("--input", required=True, help="Folder containing source PBR maps.")
    parser.add_argument("--name", required=True, help="Base output asset name.")
    parser.add_argument("--output", required=True, help="Folder for generated textures.")
    parser.add_argument("--size", type=int, default=2048, help="Square output size in pixels.")
    parser.add_argument("--preset", choices=PRESETS.keys(), default="none")
    parser.add_argument("--dry-run", action="store_true", help="Only print detected maps.")
    parser.add_argument("--verbose", action="store_true", help="Print selected inputs and outputs.")
    parser.add_argument("--albedo-saturation", type=float, default=None)
    parser.add_argument("--albedo-brightness", type=float, default=None)
    parser.add_argument("--height-contrast", type=float, default=None)
    parser.add_argument("--ao-strength", type=float, default=None)
    parser.add_argument("--roughness-bias", type=float, default=None)
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    if args.size <= 0:
        print("--size must be greater than 0.", file=sys.stderr)
        return 2

    return process(args)


if __name__ == "__main__":
    raise SystemExit(main())