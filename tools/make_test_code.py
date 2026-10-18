"""Builds the code string for the Onshape MCP `test_feature` tool: airfoil_wing.fs (with the main feature
un-exported, so the harness is the only exported feature) followed by tools/onshape_test_harness.fs.

Usage: python3 tools/make_test_code.py <output file> [suite]   (suite: basic (default), twist, tipsweep, readout, tiptwist, tangent, tiptaper, twiststep, reflex, coords or six)
"""
import pathlib
import sys

root = pathlib.Path(__file__).resolve().parent.parent
feature = (root / "airfoil_wing.fs").read_text()
harness = (root / "tools" / "onshape_test_harness.fs").read_text()

marker = "export const airfoilWing"
if marker not in feature:
    sys.exit("could not find the exported feature in airfoil_wing.fs")
feature = feature.replace(marker, "const airfoilWing", 1)

suite = sys.argv[2] if len(sys.argv) > 2 else "basic"
pathlib.Path(sys.argv[1]).write_text(feature + "\nconst SUITE = \"" + suite + "\";\n" + harness)
