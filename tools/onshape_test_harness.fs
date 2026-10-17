// Test harness appended to airfoil_wing.fs by tools/make_test_code.py and run with the Onshape MCP `test_feature`.
// Results come back in the regenError message, so keep the output short (the message is cut off at roughly 300 characters).

function stn(position is number, designation is string, chord is ValueWithUnits) returns map
{
    return { "position" : position, "designation" : designation, "custom" : false, "family" : AirfoilFamily.NACA4,
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
        return label ~ " ERR " ~ e.message;
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
        return label ~ " ERR " ~ e.message;
    }
}

annotation { "Feature Type Name" : "Wing test" }
export const wingTest = defineFeature(function(context is Context, id is Id, definition is map)
    precondition {}
    {
        if (SUITE == "readout")
        {
            // Plain wing (info message shows up in the notices), then a swept/raised tip checked through wingStats.
            const plain = build(context, id + "p", [stn(0, "0012", 200 * millimeter), stn(100, "0012", 120 * millimeter)], {});
            var tipS = stn(100, "0012", 120 * millimeter);
            tipS.sweep = 100 * millimeter;
            tipS.vertical = 50 * millimeter;
            const defn = { "stations" : [stn(0, "0012", 200 * millimeter), tipS], "span" : 1000 * millimeter, "tip" : WingtipTreatment.FLAT };
            throw regenError(wingReadout(wingStats(defn, buildSpecs(defn)), undefined) ~ " expect LE 6.8 c/4 5.7 dih 2.9 AR 12.5 MAC 163.3");
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
