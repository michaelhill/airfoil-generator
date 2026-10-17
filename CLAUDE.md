# Airfoil generator (Onshape FeatureScript)

## Goal
An Onshape FeatureScript that generates wings by lofting calculated (not looked-up) NACA airfoil sections.
Target document: https://cad.onshape.com/documents/069d976f6440003531b71da3/w/52bd80eb66c2f92abde3e200/e/70610dff99f07cb1c8c0fe9e
(document `069d976f6440003531b71da3`, workspace `52bd80eb66c2f92abde3e200`, element `70610dff99f07cb1c8c0fe9e`)

User requirements:
- Overall length (span) set by the user; airfoil profile definable at the root, the tip, and at any % along the span.
- Per profile: chord length, camber, thickness, origin %, angle of attack (plus any extra parameters that make sense).
- Loft all profiles into solid geometry.
- Standard wingtip treatments, including "none" (flat).
- Start with NACA; compare results against select UIUC airfoils (https://m-selig.ae.illinois.edu/ads/coord_database.html) during development. More airfoil families may come later.

## Files
- `airfoil_wing.fs`: the FeatureScript (feature "Airfoil wing"). Supports NACA 4-digit and 5-digit (standard 210-250), per-station params, closed/open TE, tips: none / rounded / elliptical / raked. Sections are sketches (`skFitSpline` upper + lower, TE line if open) lofted with `opLoft`; sketches are deleted afterwards.
- `tools/naca_reference.py`: Python mirror of the airfoil math plus UIUC comparison. Run `python3 -I tools/naca_reference.py <dir of UIUC .dat files>`. Download files from `https://m-selig.ae.illinois.edu/ads/coord/<name>.dat` (e.g. naca2412, naca4412, naca4415, naca23012, naca0010, naca0015; naca0012/naca0009 names 404, and `n0012.dat` holds several airfoils). Keep downloads out of the repo.

## Status
- Math validated: open-TE results match UIUC within ~0.0002 chord (4415 file: 0.0018; closed-TE variant ~0.001-0.0016 by design).
- **Verified in Onshape (via MCP `test_feature`)**: the file compiles at std version 3095; a 3-station NACA4 wing (2412-ish root 200 mm -> 0012 tip 120 mm, span 1000 mm, closed TE, flat tip) lofts to one solid with volume 2136 cm3 (matches the ~2150 cm3 estimate). `skFitSpline`, `opLoft` with `qSketchRegion`, `sort`, `evPlane` all work.
- **Also verified in Onshape (test_feature harness)**: open TE (n1 solid, slightly larger vol), flip (span 0..+1000), sweep/vertical offsets (tip TE x=190, top z=57.2 for 100/50 mm), AoA +10deg lifts the LE (nose up), 2412 section upper surface at 30% chord = 15.8 mm (hand calc 15.75), all three tips (rounded/elliptical/raked) give sane volumes and the raked tip keeps the LE fixed at x=-30. NACA5 (23012) builds. **Section vs UIUC in Onshape (done)**: built NACA 2412/4415/23012/0015 as 1000 mm-chord open-TE constant sections, ray-probed upper+lower z at x = 1.25/5/15/30/50/70/90 % chord and compared with UIUC files: max diff 0.0001 chord for 2412, 0015 and 23012 (aside from 0.0006 at the 1.25 % nose point), 0.0008 on 4415 away from the nose; the largest single diff, 0.0028 (4415 upper at 1.25 % chord), is at the steep nose where linear interpolation of the sparse UIUC points is the weak link.
- Loft lesson: lofting wing + tip-cap sections in ONE opLoft overshoots badly (sections 1000 mm apart then 5 mm apart: tip volumes 20x too big). The wing and the cap are now lofted separately and joined with opBoolean UNION. Front plane normal is -Y so span runs toward -Y there.
- **Deployed**: Feature Studio "Airfoil wing" (element `cca4ef4401f526448b8ed59c`) in the target document holds the current airfoil_wing.fs; user confirmed it builds cleanly and a wing was created in the Part Studio. Stations are now NACA designations (4-digit, standard 5-digit) with an optional per-station "Customize shape" that pre-fills values via editing logic.
- Gotchas: preconditions can only call predicates, and predicate conditions must compare a parameter directly (no computed expressions) - hence the hidden `family` parameter set by the editing logic. The MCP `put_featurescript`/`test_feature` do NOT report precondition-analysis errors; only the Onshape UI does. Bounds maps: only the first unit entry may have [min, default, max]; others plain defaults.
- Gotchas: in std 3095 unquoted keys in the defineFeature defaults map (`{ flip : false }`) make the feature vanish ("no features found"); use quoted keys. `substring` with 3 args is missing at std 2180.
- Onshape MCP is registered as `onshape` (`https://fs-mcp.labs.onshape.app/mcp`); if its tools are not visible, the user must authenticate via `/mcp` and reload. Tools: test_feature (throw regenError(out) in a harness feature to read values back, keep under ~280 chars), create_geometry, put_featurescript. Calls consume the user's Onshape API allocation.

- **Added and verified in Onshape**: seeded root/tip stations on creation (editing logic, isCreating), `Taper between stations` (straight = one loft per segment, default; smooth = one loft through all stations; 3 stations 0012 chords 200/100/120 -> 1449.6 vs 1298.3 cm3, hand estimate for straight 1458), `Mirror about root plane` (builds second half on the other side and unions; 2412-less 0012 test: 4271 cm3 = 2 x 2136, span +-1000, errors if first station is not at 0 %). Test harness for these lives in `tools/onshape_test_harness.fs`; `python3 -I tools/make_test_code.py <out>` joins it with airfoil_wing.fs into the code string to paste into the MCP `test_feature`.

- **Twist check (done, `tools/make_test_code.py <out> twist`)**: mid-span 1 mm slice of a lofted wing vs a constant-section wing built from the halfway parameters (chord 160, half the AoA/sweep/vertical). 8 deg washout + 200 mm sweep: slice bbox within 0.6 mm (no twist, no flip). 30 deg twist + 400 mm sweep + 100 mm vertical: within 0.4/1.8 mm in x but 8.5 mm in zMin; the loft blends corresponding points linearly, so a big twist between two stations shortens the mid chord (about cos(half the angle)). Not a loft twist, just linear blending; fix if needed by adding intermediate stations or auto-inserting sections when the AoA change per segment is large. AoA is bounded to +-45 deg.

## Next steps
1. Possible follow-ups: optional auto-inserted intermediate sections for large twist, readouts (aspect ratio, mean chord), reflexed 5-digit, guide curves / LE matching if the loft twists, single closed spline instead of upper/lower splines, NACA 6-series or UIUC .dat import, winglet tip.

## Conventions / interpretation notes
- "Camber radius" was interpreted as max camber (% chord) + camber position.
- Chord along root plane X, thickness along plane Y, span along plane normal. Origin % is both the stacking point and rotation pivot.
- Git: single `main` branch, nothing committed beyond the initial commit yet. Commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
