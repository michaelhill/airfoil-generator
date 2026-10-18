// Test harness appended to airfoil_wing.fs by tools/make_test_code.py and run with the Onshape MCP `test_feature`.
// Results come back in the regenError message, so keep the output short (the message is cut off at roughly 300 characters).

// UIUC naca2412.dat (Selig), goe387.dat and sc2110.dat (Lednicer) with the line breaks removed, as pasted into a
// text field. sc2110 has a blunt trailing edge whose surfaces spread apart at the end.
const NACA2412_DAT = "NACA 2412 1.0000 0.0013 0.9500 0.0114 0.9000 0.0208 0.8000 0.0375 0.7000 0.0518 0.6000 0.0636 0.5000 0.0724 0.4000 0.0780 0.3000 0.0788 0.2500 0.0767 0.2000 0.0726 0.1500 0.0661 0.1000 0.0563 0.0750 0.0496 0.0500 0.0413 0.0250 0.0299 0.0125 0.0215 0.0000 0.0000 0.0125 -0.0165 0.0250 -0.0227 0.0500 -0.0301 0.0750 -0.0346 0.1000 -0.0375 0.1500 -0.0410 0.2000 -0.0423 0.2500 -0.0422 0.3000 -0.0412 0.4000 -0.0380 0.5000 -0.0334 0.6000 -0.0276 0.7000 -0.0214 0.8000 -0.0150 0.9000 -0.0082 0.9500 -0.0048 1.0000 -0.0013";
const GOE387_DAT = "GOE 387 AIRFOIL 17. 17.  0.0000000 0.0000000 0.0125000 0.0308800 0.0250000 0.0452600 0.0500000 0.0645200 0.0750000 0.0787900 0.1000000 0.0905500 0.1500000 0.1065800 0.2000000 0.1181000 0.3000000 0.1276500 0.4000000 0.1262000 0.5000000 0.1167500 0.6000000 0.0998000 0.7000000 0.0783500 0.8000000 0.0539000 0.9000000 0.0279500 0.9500000 0.0144700 1.0000000 0.0000000  0.0000000 0.0000000 0.0125000 -.0186200 0.0250000 -.0217400 0.0500000 -.0249700 0.0750000 -.0272100 0.1000000 -.0279500 0.1500000 -.0274300 0.2000000 -.0259000 0.3000000 -.0208500 0.4000000 -.0158000 0.5000000 -.0122500 0.6000000 -.0087000 0.7000000 -.0061500 0.8000000 -.0046000 0.9000000 -.0030500 0.9500000 -.0025200 1.0000000 0.0000000";

const SC2110_DAT = "SIKORSKY SC2110 AIRFOIL 41. 47. 0.0 0.0000000 0.0041792 0.0122162 0.0065891 0.0158434 0.0114686 0.0216807 0.0238180 0.0321074 0.0486772 0.0449249 0.0736368 0.0526535 0.0986465 0.0576708 0.1487162 0.0633128 0.1988561 0.0658160 0.2490259 0.0667050 0.2992060 0.0667340 0.3375290 0.0664244 0.3774060 0.0658768 0.4172812 0.0650583 0.4372171 0.0645115 0.4571522 0.0638477 0.4770852 0.0630549 0.4970183 0.0621352 0.5368804 0.0599266 0.5568104 0.0586409 0.5767395 0.0572281 0.5966676 0.0556763 0.6365188 0.0521118 0.6564451 0.0500760 0.6763662 0.0478612 0.6962884 0.0454614 0.7361247 0.0401158 0.7560418 0.0371861 0.7759559 0.0341053 0.7958711 0.0308945 0.8356976 0.0241659 0.8556108 0.0206912 0.8755220 0.0171784 0.8954352 0.0136696 0.9352626 0.0070730 0.9551798 0.0043813 0.9751059 0.0025825 0.9850739 0.0022181 0.9950449 0.0023647 1.0000000 0.0026760 0.0 0.0000000 0.0022484 -.0061159 0.0048475 -.0081437 0.0104667 -.0104373 0.0226118 -.0130796 0.0347099 -.0149399 0.0407510 -.0157545 0.0528190 -.0171948 0.0789273 -.0196542 0.0989823 -.0211369 0.1290354 -.0229861 0.1573386 -.0245304 0.1722866 -.0253165 0.2021857 -.0268727 0.2320818 -.0283878 0.2470309 -.0291099 0.2769289 -.0304381 0.3068300 -.0315753 0.3367340 -.0324964 0.3766071 -.0333630 0.4164851 -.0337986 0.4364251 -.0338504 0.4563661 -.0337911 0.4763082 -.0336169 0.4962491 -.0333226 0.5361381 -.0323672 0.5560830 -.0317080 0.5760300 -.0309348 0.5959779 -.0300546 0.6358737 -.0279932 0.6558247 -.0268179 0.6757756 -.0255517 0.6957266 -.0241985 0.7356305 -.0212521 0.7555832 -.0196779 0.7755351 -.0180517 0.7954900 -.0163834 0.8353968 -.0129550 0.8553527 -.0112028 0.8753066 -.0094236 0.8952615 -.0076154 0.9351713 -.0040499 0.9551232 -.0024657 0.9750731 -.0012615 0.9850470 -.0009119 0.9950181 -.0008203 1.0000000 -.0008990";

function stn(position is number, designation is string, chord is ValueWithUnits) returns map
{
    return { "position" : position, "source" : AirfoilSource.NACA, "label" : "NACA " ~ designation, "coordinates" : "",
            "scaleThickness" : false, "coordThickness" : 12, "designation" : designation, "custom" : false, "family" : AirfoilFamily.NACA4, "reflex" : false,
            "camber" : 2, "camberPos" : 40, "designCl" : 0.3, "posIndex" : 3, "thickness" : 12, "chord" : chord,
            "origin" : 25, "aoa" : 0 * degree, "sweep" : 0 * millimeter, "vertical" : 0 * millimeter };
}

function r1(v is number) returns string
{
    return "" ~ roundToPrecision(v, 1);
}

function build(context is Context, id is Id, stations is array, overrides is map) returns Query
{
    var definition = {
            "plane" : qCreatedBy(makeId("Front"), EntityType.FACE), "flip" : false, "span" : 1000 * millimeter,
            "stations" : stations, "closedTE" : true, "pointsPerSide" : 40, "tip" : WingtipTreatment.FLAT,
            "tipLength" : 40 * millimeter, "tipSections" : 6, "tipChordRatio" : 0.3, "mirror" : false,
            "taper" : StationTaper.STRAIGHT
        };
    for (var entry in overrides)
        definition[entry.key] = entry.value;
    airfoilWing(context, id, definition);
    return qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SOLID);
}

/** Builds a wing and reports body count, volume (cm^3) and the Y extent (mm). */
function report(context is Context, id is Id, label is string, stations is array, overrides is map) returns string
{
    try
    {
        const q = build(context, id, stations, overrides);
        const bb = evBox3d(context, { "topology" : q });
        return label ~ " n" ~ size(evaluateQuery(context, q)) ~ " v" ~ r1(evVolume(context, { "entities" : q }) / (centimeter ^ 3))
            ~ " y" ~ r1(bb.minCorner[1] / millimeter) ~ ".." ~ r1(bb.maxCorner[1] / millimeter);
    }
    catch (e)
    {
        return label ~ " ERR " ~ (e is map ? e.message : "" ~ e);
    }
}

/** Slice (1 mm slab centred on y, in mm) of a wing, returned as [xMin, xMax, zMin, zMax] in mm. */
function slabBox(context is Context, id is Id, stations is array, overrides is map, y is number) returns array
{
    const q = build(context, id + "w", stations, overrides);
    fCuboid(context, id + "box", { "corner1" : vector(-2000, y - 0.5, -2000) * millimeter, "corner2" : vector(2000, y + 0.5, 2000) * millimeter });
    opBoolean(context, id + "cut", {
                "tools" : qUnion([q, qCreatedBy(id + "box", EntityType.BODY)]),
                "operationType" : BooleanOperationType.INTERSECTION
            });
    const bb = evBox3d(context, { "topology" : qCreatedBy(id + "cut", EntityType.BODY) });
    return [bb.minCorner[0] / millimeter, bb.maxCorner[0] / millimeter, bb.minCorner[2] / millimeter, bb.maxCorner[2] / millimeter];
}

/**
 * Twist check: lofts a root (0 deg, chord 200) to a tip with the given angle of attack, sweep and vertical offset,
 * then compares the mid-span slice with a constant-section wing built from the halfway parameters.
 * Returns the differences (loft minus reference) in mm.
 */
function twistCase(context is Context, id is Id, label is string, tipAoa is ValueWithUnits, tipSweep is ValueWithUnits, tipVertical is ValueWithUnits) returns string
{
    try
    {
        var root = stn(0, "2412", 200 * millimeter);
        var tipStation = stn(100, "2412", 120 * millimeter);
        tipStation.aoa = tipAoa;
        tipStation.sweep = tipSweep;
        tipStation.vertical = tipVertical;
        var midA = stn(0, "2412", 160 * millimeter);
        var midB = stn(100, "2412", 160 * millimeter);
        midA.aoa = tipAoa / 2;
        midA.sweep = tipSweep / 2;
        midA.vertical = tipVertical / 2;
        midB.aoa = tipAoa / 2;
        midB.sweep = tipSweep / 2;
        midB.vertical = tipVertical / 2;
        const lofted = slabBox(context, id + "a", [root, tipStation], {}, -500);
        const reference = slabBox(context, id + "b", [midA, midB], {}, -500);
        var out = label ~ " d";
        for (var i = 0; i < 4; i += 1)
            out = out ~ " " ~ r1(lofted[i] - reference[i]);
        return out;
    }
    catch (e)
    {
        return label ~ " ERR " ~ (e is map ? e.message : "" ~ e);
    }
}

annotation { "Feature Type Name" : "Wing test" }
export const wingTest = defineFeature(function(context is Context, id is Id, definition is map)
    precondition {}
    {
        if (SUITE == "tangent")
        {
            // Constant 150 mm chord, leading-edge sweep offsets 0 / 150 / 200 mm at 0 / 60 / 100 % of 1000 mm, raked 80 mm tip.
            // Slope of the leading-edge x position just inside (y = -980, -990) and just outside (y = -1010, -1020) the tip
            // station at y = -1000. Smooth: expect about 0.075 on both sides (end slope of the shape-preserving cubic);
            // straight: 0.125 on both sides (slope of the last segment).
            var out = "";
            for (var m = 0; m < 2; m += 1)
            {
                try
                {
                    var s1 = stn(60, "0012", 150 * millimeter);
                    s1.sweep = 150 * millimeter;
                    var s2 = stn(100, "0012", 150 * millimeter);
                    s2.sweep = 200 * millimeter;
                    const stations = [stn(0, "0012", 150 * millimeter), s1, s2];
                    const overrides = { "taper" : m == 0 ? StationTaper.SMOOTH : StationTaper.STRAIGHT, "tip" : WingtipTreatment.RAKED, "tipLength" : 80 * millimeter };
                    var xs = [];
                    for (var y in [-980, -990, -1010, -1020])
                        xs = append(xs, slabBox(context, id + ("t" ~ m ~ "_" ~ (-y)), stations, overrides, y)[0]);
                    out = out ~ (m == 0 ? "SMOOTH" : "STRAIGHT") ~ " in " ~ roundToPrecision((xs[1] - xs[0]) / 10, 3) ~ " out " ~ roundToPrecision((xs[3] - xs[2]) / 10, 3) ~ " | ";
                }
                catch (e)
                {
                    out = out ~ (m == 0 ? "SMOOTH" : "STRAIGHT") ~ " ERR " ~ e.message ~ " | ";
                }
            }
            throw regenError(out ~ "expect ~0.075 both | 0.125 both");
        }
        if (SUITE == "tiptaper")
        {
            // 0012, chord 200 -> 120 mm over 1000 mm (taper -0.08 mm/mm), 40 mm tip. Rate of chord loss just inside
            // (y = -985, -995) and just outside (y = -1005, -1015) the tip station at y = -1000.
            // Rounded with the taper continued: about 0.08 on both sides; without: 0.08 inside, about 0 outside.
            const stations = [stn(0, "0012", 200 * millimeter), stn(100, "0012", 120 * millimeter)];
            var out = "";
            const cases = [["RND+", WingtipTreatment.ROUNDED, true], ["RND-", WingtipTreatment.ROUNDED, false], ["ELL+", WingtipTreatment.ELLIPTICAL, true]];
            for (var c = 0; c < size(cases); c += 1)
            {
                try
                {
                    const overrides = { "tip" : cases[c][1], "tipLength" : 40 * millimeter, "tipContinueTaper" : cases[c][2] };
                    var w = [];
                    for (var y in [-985, -995, -1005, -1015])
                    {
                        const slab = slabBox(context, id + ("c" ~ c ~ "_" ~ (-y)), stations, overrides, y);
                        w = append(w, slab[1] - slab[0]);
                    }
                    out = out ~ cases[c][0] ~ " in " ~ roundToPrecision((w[0] - w[1]) / 10, 3) ~ " out " ~ roundToPrecision((w[2] - w[3]) / 10, 3) ~ " | ";
                }
                catch (e)
                {
                    out = out ~ cases[c][0] ~ " ERR " ~ e.message ~ " | ";
                }
            }
            throw regenError(out ~ "expect .08/.08 | .08/~0 | .08/>.08");
        }
        if (SUITE == "tiptwist")
        {
            // Root 0 deg, tip -6 deg over 1000 mm, raked tip 60 mm long: the last cap section's angle of attack.
            // Rate -0.006 deg/mm, so continuing for 60 mm gives -6.36; extra +4 deg at the end of the tip adds 4.
            var rootT = stn(0, "0012", 200 * millimeter);
            var tipT = stn(100, "0012", 120 * millimeter);
            tipT.aoa = -6 * degree;
            var out = "";
            const cases = [["NONE", false, 0], ["CONT", true, 0], ["EXTRA", false, 4], ["BOTH", true, 4]];
            for (var c = 0; c < size(cases); c += 1)
            {
                var defn = { "stations" : [rootT, tipT], "span" : 1000 * millimeter, "tip" : WingtipTreatment.RAKED, "tipLength" : 60 * millimeter,
                        "tipSections" : 6, "tipChordRatio" : 0.3, "tipContinue" : false, "tipSweep" : 0 * degree,
                        "tipContinueTwist" : cases[c][1], "tipTwist" : cases[c][2] * degree };
                const specs = buildSpecs(defn).specs;
                out = out ~ cases[c][0] ~ " " ~ r1(specs[size(specs) - 1].aoa / degree) ~ " | ";
                try
                {
                    build(context, id + ("b" ~ c), [rootT, tipT], { "tip" : WingtipTreatment.RAKED, "tipLength" : 60 * millimeter,
                                "tipContinueTwist" : cases[c][1], "tipTwist" : cases[c][2] * degree });
                }
                catch (e)
                {
                    out = out ~ "BUILD ERR | ";
                }
            }
            throw regenError(out ~ "expect -6.0 / -6.4 / -2.0 / -2.4");
        }
        if (SUITE == "readout")
        {
            // Plain wing (info message shows up in the notices), then a swept/raised tip checked through wingStats.
            const plain = build(context, id + "p", [stn(0, "0012", 200 * millimeter), stn(100, "0012", 120 * millimeter)], {});
            var tipS = stn(100, "0012", 120 * millimeter);
            tipS.sweep = 100 * millimeter;
            tipS.vertical = 50 * millimeter;
            const defn = { "stations" : [stn(0, "0012", 200 * millimeter), tipS], "span" : 1000 * millimeter, "tip" : WingtipTreatment.FLAT };
            throw regenError(wingReadout(wingStats(buildSpecs(defn)), undefined) ~ " expect LE 6.8 c/4 5.7 dih 2.9 AR 12.5 MAC 163.3");
        }
        if (SUITE == "tipsweep")
        {
            // Raked tip, 60 mm long, tip station swept 300 mm aft and raised 50 mm; slice 55 mm into the cap.
            var rootS = stn(0, "0012", 200 * millimeter);
            var tipS = stn(100, "0012", 120 * millimeter);
            tipS.sweep = 300 * millimeter;
            tipS.vertical = 50 * millimeter;
            var out = "";
            const cases = [["NONE", false, 0], ["CONT", true, 0], ["EX20", false, 20], ["BOTH", true, 20]];
            for (var c = 0; c < size(cases); c += 1)
            {
                const slab = slabBox(context, id + ("s" ~ c), [rootS, tipS], { "tip" : WingtipTreatment.RAKED, "tipLength" : 60 * millimeter,
                            "tipContinue" : cases[c][1], "tipSweep" : cases[c][2] * degree }, -1055);
                out = out ~ cases[c][0] ~ " x" ~ r1(slab[0]) ~ " z" ~ r1(slab[2]) ~ " | ";
            }
            throw regenError(out ~ "expect x 270/286.5/290/306.5");
        }
        if (SUITE == "six")
        {
            // Designation parsing (kind and family), section y at cosine indices 8, 20, 36 of 40 (closed TE) against
            // tools/naca_reference.py section(), then a 652-415 -> 64A010 smooth wing with a rounded tip and a
            // 16-212 -> 0010-64 straight wing.
            var out = "";
            for (var d in ["63(2)-415", "632-415", "64A010", "16-212", "0010-64", "6409", "65-410 a=0.5", "68-412", "64A010 a=2"])
            {
                const parsed = parseNaca(d);
                out = out ~ (parsed.ok ? parsed.kind ~ (parsed.sixFamily != undefined && parsed.sixFamily >= 0 ? SIX_FAMILIES[parsed.sixFamily] : "") : "X") ~ " ";
            }
            for (var d in ["63-412", "64A410", "65-410 a=0.5", "16-212", "0010-64"])
            {
                const naca = parseNaca(d);
                var spec = { "family" : naca.family, "camber" : naca.camber / 100, "camberPos" : naca.camberPos / 100,
                        "designCl" : naca.designCl, "posIndex" : naca.posIndex, "reflex" : naca.reflex, "thickness" : naca.thickness / 100,
                        "meanA" : naca.family == AirfoilFamily.NACA6 ? naca.meanA : 1 };
                if (naca.kind == "4M" || naca.kind == "16")
                    spec.fourModified = fourModifiedCoefficients(naca.thickness / 100, naca.leIndex, naca.xMaxThickness);
                if (naca.kind == "6" || naca.kind == "6A")
                    spec.sixThickness = sixThickness(naca.sixFamily, naca.thickness / 100);
                const surfaces = airfoilSurfaces(spec, 40, true);
                out = out ~ "| " ~ d;
                for (var i in [8, 20, 36])
                    out = out ~ " " ~ roundToPrecision(surfaces.upper[i][1], 5) ~ "/" ~ roundToPrecision(surfaces.lower[i][1], 5);
            }
            out = out ~ " | " ~ report(context, id + "a", "SM", [stn(0, "652-415", 200 * millimeter), stn(100, "64A010", 120 * millimeter)],
                    { "taper" : StationTaper.SMOOTH, "tip" : WingtipTreatment.ROUNDED });
            out = out ~ " " ~ report(context, id + "b", "ST", [stn(0, "16-212", 200 * millimeter), stn(100, "0010-64", 120 * millimeter)], {});
            throw regenError(out);
        }
        if (SUITE == "coords")
        {
            // Pasted coordinates (line breaks removed, as from a one-line text field): a Selig file (UIUC naca2412) and
            // a Lednicer file (goe387, with "-.0186" style numbers). Name, points per side and section y at cosine
            // indices 8, 20, 36 of 40 (closed TE) against tools/dat_reference.py; then volumes: the naca2412 file vs
            // the calculated NACA 2412 (should be close), goe387 -> 0012 smooth with a rounded tip, and sc2110 -> 0012
            // closed (needs the thickness-ramp trailing-edge closing) and open.
            var out = "";
            for (var text in [NACA2412_DAT, GOE387_DAT, SC2110_DAT])
            {
                const coords = parseCoordinates(text);
                if (!coords.ok)
                {
                    out = out ~ "ERR " ~ coords.error ~ " | ";
                    continue;
                }
                const surfaces = coordinateSurfaces(coords, 40, true, 1);
                out = out ~ coords.name ~ " " ~ size(coords.upper) ~ "/" ~ size(coords.lower) ~ (coords.closeRamp ? " ramp" : " pinch");
                for (var i in [8, 20, 36])
                    out = out ~ " " ~ roundToPrecision(surfaces.upper[i][1], 5) ~ "/" ~ roundToPrecision(surfaces.lower[i][1], 5);
                out = out ~ " | ";
            }
            var datRoot = stn(0, "2412", 200 * millimeter);
            datRoot.source = AirfoilSource.COORDINATES;
            datRoot.coordinates = NACA2412_DAT;
            var datTip = datRoot;
            datTip.position = 100;
            datTip.chord = 120 * millimeter;
            out = out ~ report(context, id + "a", "DAT", [datRoot, datTip], {});
            out = out ~ " " ~ report(context, id + "b", "NACA", [stn(0, "2412", 200 * millimeter), stn(100, "2412", 120 * millimeter)], {});
            var goe = datRoot;
            goe.coordinates = GOE387_DAT;
            out = out ~ " | " ~ report(context, id + "c", "GOE", [goe, stn(50, "2412", 160 * millimeter), stn(100, "0012", 120 * millimeter)],
                    { "taper" : StationTaper.SMOOTH, "tip" : WingtipTreatment.ROUNDED });
            var sc = datRoot;
            sc.coordinates = SC2110_DAT;
            out = out ~ " | " ~ report(context, id + "d", "SC", [sc, stn(100, "0012", 120 * millimeter)], {});
            out = out ~ " " ~ report(context, id + "e", "SCopen", [sc, stn(100, "0012", 120 * millimeter)], { "closedTE" : false });
            throw regenError(out ~ " | expect pinch pinch ramp, 18/18 .05623/-.03739 .07024/-.03165 .00303/-.00105, 17/17 .09066/-.02795 .11402/-.01152 .00431/-.00098");
        }
        if (SUITE == "tscale")
        {
            // Thickness scaling of pasted coordinates: file thickness (%) and scaled section y at cosine indices 20, 36
            // of 40 (closed TE) against tools/dat_reference.py --scale (naca2412 -> 15, goe387 -> 10, sc2110 -> 14,
            // the last with the thickness-ramp closing); the editing logic filling in the file's thickness and the
            // label; then volumes: naca2412.dat scaled to 15 % vs the calculated NACA 2415 wing (expect 98.8 %, as unscaled).
            var out = "";
            const cases = [[NACA2412_DAT, 15], [GOE387_DAT, 10], [SC2110_DAT, 14]];
            for (var c in cases)
            {
                const coords = parseCoordinates(c[0]);
                const surfaces = coordinateSurfaces(coords, 40, true, c[1] / 100 / coords.thickness);
                out = out ~ "t" ~ roundToPrecision(coords.thickness * 100, 3);
                for (var i in [20, 36])
                    out = out ~ " " ~ roundToPrecision(surfaces.upper[i][1], 5) ~ "/" ~ roundToPrecision(surfaces.lower[i][1], 5);
                out = out ~ " | ";
            }
            var datRoot = stn(0, "2412", 200 * millimeter);
            datRoot.source = AirfoilSource.COORDINATES;
            datRoot.coordinates = NACA2412_DAT;
            var ticked = datRoot;
            ticked.scaleThickness = true;
            const edited = airfoilWingEditLogic(context, id, { "stations" : [datRoot] }, { "stations" : [ticked] }, false);
            out = out ~ edited.stations[0].coordThickness ~ " '" ~ edited.stations[0].label ~ "' | ";
            ticked.coordThickness = 15;
            var datTip = ticked;
            datTip.position = 100;
            datTip.chord = 120 * millimeter;
            out = out ~ report(context, id + "a", "DAT15", [ticked, datTip], {});
            out = out ~ " " ~ report(context, id + "b", "N2415", [stn(0, "2415", 200 * millimeter), stn(100, "2415", 120 * millimeter)], {});
            // Unscaled root, scaled tip: 12 -> 15 %.
            out = out ~ " " ~ report(context, id + "c", "MIX", [datRoot, datTip], {});
            throw regenError(out);
        }
        if (SUITE == "reflex")
        {
            // Parsing, then section points (upper/lower y at cosine indices 8, 20, 36 of 40) against the Python
            // reference, then a 23112 -> 25112 wing (straight and smooth).
            var out = "";
            for (var d in ["23112", "21112", "23212"])
            {
                const parsed = parseNaca(d);
                out = out ~ d ~ (parsed.ok ? (parsed.reflex ? " R " : " S ") : " X ");
            }
            for (var d in ["23112", "25112"])
            {
                const naca = parseNaca(d);
                const surfaces = airfoilSurfaces({ "family" : naca.family, "designCl" : naca.designCl, "posIndex" : naca.posIndex,
                            "reflex" : naca.reflex, "thickness" : naca.thickness / 100 }, 40, true);
                out = out ~ "| " ~ d;
                for (var i in [8, 20, 36])
                    out = out ~ " " ~ roundToPrecision(surfaces.upper[i][1], 5) ~ "/" ~ roundToPrecision(surfaces.lower[i][1], 5);
            }
            const stations = [stn(0, "23112", 200 * millimeter), stn(100, "25112", 120 * millimeter), stn(50, "23012", 150 * millimeter)];
            out = out ~ " | " ~ report(context, id + "a", "STR", stations, {});
            out = out ~ " | " ~ report(context, id + "b", "SM", stations, { "taper" : StationTaper.SMOOTH });
            throw regenError(out);
        }
        if (SUITE == "twiststep")
        {
            // Large twist with extra sections every 10 deg or less: the mid-span slice should match the halfway wing
            // (before, T30 came out about 5.5 mm short in chord). Angle of attack is bounded to +-45 deg. Then untwisted volumes,
            // which must not change: straight 3-station 1449.6, smooth 1253.8.
            var out = twistCase(context, id + "t1", "W8", -8 * degree, 200 * millimeter, 0 * millimeter);
            out = out ~ " | " ~ twistCase(context, id + "t2", "T30", 30 * degree, 400 * millimeter, 100 * millimeter);
            out = out ~ " | " ~ twistCase(context, id + "t3", "T45", -45 * degree, 600 * millimeter, -100 * millimeter);
            const stations = [stn(0, "0012", 200 * millimeter), stn(50, "0012", 100 * millimeter), stn(100, "0012", 120 * millimeter)];
            out = out ~ " | " ~ report(context, id + "a", "STR", stations, {});
            out = out ~ " | " ~ report(context, id + "b", "SM", stations, { "taper" : StationTaper.SMOOTH });
            throw regenError(out);
        }
        if (SUITE == "twist")
        {
            var out = twistCase(context, id + "t1", "W8", -8 * degree, 200 * millimeter, 0 * millimeter);
            out = out ~ " | " ~ twistCase(context, id + "t2", "T30", 30 * degree, 400 * millimeter, 100 * millimeter);
            out = out ~ " | " ~ twistCase(context, id + "t3", "T60", -60 * degree, 600 * millimeter, -100 * millimeter);
            throw regenError(out);
        }
        const root = stn(0, "0012", 200 * millimeter);
        const mid = stn(50, "0012", 100 * millimeter);
        const tipStation = stn(100, "0012", 120 * millimeter);
        var out = "";
        const seeded = airfoilWingEditLogic(context, id, {}, { "stations" : [] }, true);

        out = report(context, id + "a", "STRAIGHT", [root, mid, tipStation], {});
        out = out ~ " | " ~ report(context, id + "b", "SMOOTH", [root, mid, tipStation], { "taper" : StationTaper.SMOOTH });
        out = out ~ " | " ~ report(context, id + "c", "MIRROR", [root, tipStation], { "mirror" : true });
        out = out ~ " | " ~ report(context, id + "d", "MIRROR+TIP", [root, tipStation], { "mirror" : true, "tip" : WingtipTreatment.ROUNDED });
        out = out ~ " | " ~ report(context, id + "e", "MIRROR@10%", [stn(10, "0012", 200 * millimeter), tipStation], { "mirror" : true });
        out = out ~ " | SEED " ~ size(seeded.stations) ~ " " ~ seeded.stations[0].designation ~ "/" ~ seeded.stations[1].designation;
        throw regenError(out);
    }, {});
