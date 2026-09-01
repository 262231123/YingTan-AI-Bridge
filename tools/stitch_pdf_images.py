from __future__ import annotations

import io
import re
import sys
from pathlib import Path

from PIL import Image
from pypdf import PdfReader


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: stitch_pdf_images.py <input.pdf> <output-dir>", file=sys.stderr)
        return 2

    pdf_path = Path(sys.argv[1])
    out_dir = Path(sys.argv[2])
    out_dir.mkdir(parents=True, exist_ok=True)

    page = PdfReader(str(pdf_path)).pages[0]
    xobjects = page["/Resources"]["/XObject"]

    images: list[tuple[int, Image.Image]] = []
    for name, obj in xobjects.items():
        match = re.search(r"Image(\d+)", str(name))
        if not match:
            continue

        number = int(match.group(1))
        image = Image.open(io.BytesIO(obj.get_object().get_data())).convert("RGB")
        images.append((number, image))

    if not images:
        raise RuntimeError("No image XObjects found in PDF.")

    images.sort(key=lambda item: item[0])
    columns = 5
    rows: list[Image.Image] = []

    for row_index in range(0, len(images), columns):
        row_images = [image for _, image in images[row_index : row_index + columns]]
        row_width = sum(image.width for image in row_images)
        row_height = max(image.height for image in row_images)
        row = Image.new("RGB", (row_width, row_height), "white")
        x = 0

        for image in row_images:
            row.paste(image, (x, 0))
            x += image.width

        rows.append(row)

    canvas_width = max(row.width for row in rows)
    canvas_height = sum(row.height for row in rows)
    canvas = Image.new("RGB", (canvas_width, canvas_height), "white")
    y = 0

    for row in rows:
        canvas.paste(row, (0, y))
        y += row.height

    stitched_path = out_dir / "floorplan_stitched.png"
    preview_path = out_dir / "floorplan_preview.png"

    canvas.save(stitched_path)
    preview = canvas.copy()
    preview.thumbnail((2400, 1700))
    preview.save(preview_path)

    print(f"stitched={stitched_path}")
    print(f"preview={preview_path}")
    print(f"size={canvas.width}x{canvas.height}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
