#!/usr/bin/env python3
"""Read-only checks for the two Kenney TMP fonts; Python standard library only.

Compatibility exception to main 402fc0f: preserve its complete glyph/character
and packing tables, atlas payloads and materials, but restore m_SourceFontFile,
m_AtlasPopulationMode=1 and texture m_IsReadable=1. This retains 463de47's dynamic
UI coverage instead of main's incomplete static fonts. Dynamic population can
produce future asset diffs; do not merge generated tables separately from atlases.

Run: python -B Tools/FontAssetChecks.py --self-test
No Unity, Git or Library baseline is required. This validates serialized structure
and source glyph availability, not native TMP generation, rendering or atlas capacity.
"""

import argparse
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FONTS = ROOT / "Assets/Vampire Survival Assets/Fonts/Kenney Fonts"
SOURCE_GUID = "10798a15cd36cc246b48464e7090bc5b"
# Stable asset GUID, embedded material ID, embedded atlas ID.
CONTRACTS = {
    "Kenney Pixel SDF - Outline.asset": (
        "fa71b087cd1e24740b10e253dbb30e69", 8376728651533316954, 8642559553099674065
    ),
    "Kenney Pixel SDF.asset": (
        "bc9cbd0f250094743ae9107a9c46fcb1", 8396591170515024485, -7052331985296024483
    ),
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def field(text, name, indent=2):
    values = re.findall(r"^" + " " * indent + re.escape(name) + r": ([^\r\n]*)", text, re.MULTILINE)
    require(len(values) == 1, f"Expected one {name}")
    return values[0]


def section(text, name):
    match = re.search(r"^  " + re.escape(name) + r":.*?(?=^  \w|\Z)", text, re.MULTILINE | re.DOTALL)
    require(match is not None, f"Missing {name}")
    return match[0]


def source_cmap():
    """Read this source TTF's Unicode format-4 cmap, without importing a font library."""
    source = FONTS / "Kenney Pixel.ttf"
    data = source.read_bytes()
    require(data[:4] == b"\x00\x01\x00\x00", "Source must be a real TrueType payload, not an LFS pointer")
    meta = source.with_suffix(".ttf.meta").read_text(encoding="utf-8-sig")
    require(field(meta, "guid", 0) == SOURCE_GUID, "Source TTF GUID changed")
    require(field(meta, "includeFontData") == "1", "Player build needs embedded source font data")
    u16 = lambda offset: struct.unpack_from(">H", data, offset)[0]
    u32 = lambda offset: struct.unpack_from(">I", data, offset)[0]
    tables = {}
    for offset in range(12, 12 + 16 * u16(4), 16):
        start, size = u32(offset + 8), u32(offset + 12)
        require(start + size <= len(data), "Truncated TTF table")
        tables[data[offset:offset + 4]] = start
    cmap, glyph_count = tables[b"cmap"], u16(tables[b"maxp"] + 4)
    mapping = {}
    for index in range(u16(cmap + 2)):
        record = cmap + 4 + 8 * index
        platform, encoding = u16(record), u16(record + 2)
        offset = cmap + u32(record + 4)
        if not (platform == 0 or (platform == 3 and encoding == 1)) or u16(offset) != 4:
            continue
        count = u16(offset + 6) // 2
        ends = offset + 14
        starts = ends + 2 * count + 2
        deltas, ranges = starts + 2 * count, starts + 4 * count
        for segment in range(count):
            start, end = u16(starts + 2 * segment), u16(ends + 2 * segment)
            delta, relative = u16(deltas + 2 * segment), u16(ranges + 2 * segment)
            for code in range(start, min(end + 1, 65535)):
                glyph = u16(ranges + 2 * segment + relative + 2 * (code - start)) if relative else code
                glyph = (glyph + delta) % 65536 if glyph or not relative else 0
                if glyph:
                    require(glyph < glyph_count, "TTF cmap points outside glyph table")
                    require(code not in mapping or mapping[code] == glyph, "Unicode cmaps disagree")
                    mapping[code] = glyph
    require(mapping, "Source has no supported Unicode format-4 cmap")
    required = set(range(32, 127)) | {0x2026}
    require(required <= mapping.keys(), f"Source missing UI repertoire: {sorted(required - mapping.keys())}")
    return mapping, glyph_count


def check_font(text, contract, mapping, glyph_count):
    require(not re.search(r"(?m)^(?:<{7}|={7}|>{7}|\|{7})", text), "Conflict markers")
    headers = list(re.finditer(r"^--- !u!(\d+) &(-?\d+)\s*$", text, re.MULTILINE))
    docs, types = {}, {}
    for index, match in enumerate(headers):
        ident = int(match[2])
        require(ident not in docs, "Duplicate subasset ID")
        docs[ident] = text[match.end():headers[index + 1].start() if index + 1 < len(headers) else len(text)]
        types[ident] = int(match[1])
    _, material_id, atlas_id = contract
    require(types == {11400000: 114, material_id: 21, atlas_id: 28}, "Font/material/atlas identity changed")
    local_refs = {int(value) for value in re.findall(r"\{fileID: (-?\d+)\}", text)} - {0}
    require(local_refs <= docs.keys(), f"Dangling local references: {local_refs - docs.keys()}")
    font, material, texture = docs[11400000], docs[material_id], docs[atlas_id]
    require(field(font, "m_SourceFontFile") == f"{{fileID: 12800000, guid: {SOURCE_GUID}, type: 3}}", "Dynamic source TTF not bound")
    require(field(font, "m_SourceFontFileGUID") == SOURCE_GUID, "Source GUID metadata mismatch")
    require(field(font, "m_AtlasPopulationMode") == "1", "Dynamic UI compatibility disabled")
    require(field(texture, "m_IsReadable") == "1", "Dynamic atlas must be readable")
    require(field(font, "m_Material") == f"{{fileID: {material_id}}}", "Font material binding changed")
    require(re.findall(r"\{fileID: (-?\d+)\}", section(font, "m_AtlasTextures")) == [str(atlas_id)], "Unexpected atlas binding")
    require(re.search(r"- _MainTex:\s+ m_Texture: \{fileID: " + str(atlas_id) + r"\}", material), "Material uses wrong atlas")
    require(field(font, "m_AtlasTextureIndex") == "0", "Unexpected active atlas index")
    width, height = int(field(texture, "m_Width")), int(field(texture, "m_Height"))
    target = (int(field(font, "m_AtlasWidth")), int(field(font, "m_AtlasHeight")))
    require(width > 0 and height > 0, "Invalid texture size")
    require(field(texture, "m_TextureFormat") == "1" and field(texture, "m_MipCount") == "1", "Expected single-mip Alpha8 atlas")
    payload = bytes.fromhex(field(texture, "_typelessdata"))
    require(len(payload) == width * height == int(field(texture, "m_CompleteImageSize")) == int(field(texture, "image data")), "Atlas dimensions/payload mismatch")
    glyphs = {}
    for record in re.split(r"(?m)^  - m_Index: ", section(font, "m_GlyphTable"))[1:]:
        ident = int(record.splitlines()[0])
        require(ident not in glyphs and 0 < ident < glyph_count, "Duplicate or invalid glyph index")
        require(field(record, "m_AtlasIndex", 4) == "0", "Glyph refers to absent atlas")
        rect = record.split("    m_GlyphRect:", 1)[1].split("    m_Scale:", 1)[0]
        x, y, w, h = (int(field(rect, name, 6)) for name in ("m_X", "m_Y", "m_Width", "m_Height"))
        require(min(x, y, w, h) >= 0 and x + w <= width and y + h <= height, "Glyph rectangle outside texture")
        glyphs[ident] = (x, y, w, h)
    chars = {}
    for record in re.split(r"(?m)^  - m_ElementType: ", section(font, "m_CharacterTable"))[1:]:
        code, glyph = int(field(record, "m_Unicode", 4)), int(field(record, "m_GlyphIndex", 4))
        require(code not in chars, "Duplicate character mapping")
        require(glyph in glyphs, "Character references missing glyph")
        require(mapping.get(code) == glyph, "Character/glyph mapping disagrees with source TTF")
        chars[code] = glyph
    require(bool(glyphs) == bool(chars), "Incomplete glyph/character cache")
    # TMP legitimately stores a 1x1 empty dynamic atlas until its first population.
    require((width, height) == target or (not glyphs and (width, height) == (1, 1) and payload == b"\x00"), "Unexpected atlas dimensions")
    for name in ("m_UsedGlyphRects", "m_FreeGlyphRects"):
        for record in re.split(r"(?m)^  - m_X: ", section(font, name))[1:]:
            x = int(record.splitlines()[0])
            y, w, h = (int(field(record, key, 4)) for key in ("m_Y", "m_Width", "m_Height"))
            require(min(x, y, w, h) >= 0 and x + w <= target[0] and y + h <= target[1], "Packing rectangle outside configured atlas")
    return f"{len(glyphs)} glyphs / {len(chars)} characters; {width}x{height} / {len(payload)} bytes"


def self_test(text, contract, mapping, glyph_count):
    mutations = {
        "conflict marker": text + "\n<<<<<<< test\n",
        "static font": text.replace("m_AtlasPopulationMode: 1", "m_AtlasPopulationMode: 0"),
        "unreadable atlas": text.replace("m_IsReadable: 1", "m_IsReadable: 0"),
        "bad payload size": text.replace("m_CompleteImageSize: 1048576", "m_CompleteImageSize: 1"),
        "missing glyph": re.sub(r"m_GlyphIndex: \d+", "m_GlyphIndex: 999999", text, count=1),
        "wrong atlas": text.replace("m_AtlasIndex: 0", "m_AtlasIndex: 1", 1),
    }
    for name, candidate in mutations.items():
        require(candidate != text, f"Self-test did not mutate: {name}")
        try:
            check_font(candidate, contract, mapping, glyph_count)
        except ValueError:
            continue
        raise ValueError(f"Failed to reject: {name}")
    print(f"Self-tests: {len(mutations)} invalid fonts rejected (in memory)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    mapping, glyph_count = source_cmap()
    print(f"TTF: {len(mapping)} Unicode mappings; printable ASCII + U+2026 available")
    for name, contract in CONTRACTS.items():
        path = FONTS / name
        meta = path.with_suffix(".asset.meta").read_text(encoding="utf-8-sig")
        require(field(meta, "guid", 0) == contract[0], f"Asset GUID changed: {name}")
        require(field(meta, "mainObjectFileID") == "11400000", "Main font ID changed")
        text = path.read_text(encoding="utf-8-sig")
        print(f"{name}: PASS; {check_font(text, contract, mapping, glyph_count)}")
        if args.self_test and "Outline" in name:
            self_test(text, contract, mapping, glyph_count)
    print("Static checks PASS; native dynamic generation/rendering not validated")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, IndexError, struct.error) as error:
        raise SystemExit(f"Font checks FAIL: {error}")
