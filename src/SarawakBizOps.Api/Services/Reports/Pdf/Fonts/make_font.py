"""Rebuilds NotoSansSC-Regular.ttf, the font the service report embeds for text Helvetica cannot show.

Source: Noto Sans SC, the variable TrueType from https://github.com/google/fonts/tree/main/ofl/notosanssc
(SIL Open Font License 1.1, see OFL.txt next to this script). Needs fontTools (pip install fonttools).

    python make_font.py "NotoSansSC[wght].ttf"

It pins the weight axis at Regular (400), drops the variation, layout (GSUB/GPOS) and vertical tables the
PDF writer does not use, and removes the hinting, which keeps the outlines and the per-report subsets smaller.
"""
import sys

from fontTools.ttLib import TTFont
from fontTools.ttLib.tables import ttProgram
from fontTools.varLib import instancer

source = sys.argv[1] if len(sys.argv) > 1 else "NotoSansSC[wght].ttf"
font = instancer.instantiateVariableFont(TTFont(source), {"wght": 400}, updateFontNames=True)

for tag in ["GSUB", "GPOS", "GDEF", "BASE", "vhea", "vmtx", "STAT", "HVAR", "MVAR", "gvar", "avar", "fvar",
            "gasp", "prep", "cvt ", "fpgm"]:
    if tag in font:
        del font[tag]

font["post"].formatType = 3.0
glyf = font["glyf"]
for name in font.getGlyphOrder():
    glyph = glyf[name]
    if hasattr(glyph, "program"):
        glyph.program = ttProgram.Program()
        glyph.program.fromBytecode(b"")
font["maxp"].maxSizeOfInstructions = 0

font.save("NotoSansSC-Regular.ttf")
