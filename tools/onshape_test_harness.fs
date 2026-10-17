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

annotation { "Feature Type Name" : "Wing test" }
export const wingTest = defineFeature(function(context is Context, id is Id, definition is map)
    precondition {}
    {
        const root = stn(0, "0012", 200 * millimeter);
        const mid = stn(50, "0012", 100 * millimeter);
        const tipStation = stn(100, "0012", 120 * millimeter);
        const seeded = airfoilWingEditLogic(context, id, {}, { "stations" : [] }, true);

        var out = report(context, id + "a", "STRAIGHT", [root, mid, tipStation], {});
        out = out ~ " | " ~ report(context, id + "b", "SMOOTH", [root, mid, tipStation], { "taper" : StationTaper.SMOOTH });
        out = out ~ " | " ~ report(context, id + "c", "MIRROR", [root, tipStation], { "mirror" : true });
        out = out ~ " | " ~ report(context, id + "d", "MIRROR+TIP", [root, tipStation], { "mirror" : true, "tip" : WingtipTreatment.ROUNDED });
        out = out ~ " | " ~ report(context, id + "e", "MIRROR@10%", [stn(10, "0012", 200 * millimeter), tipStation], { "mirror" : true });
        out = out ~ " | SEED " ~ size(seeded.stations) ~ " " ~ seeded.stations[0].designation ~ "/" ~ seeded.stations[1].designation;
        throw regenError(out);
    }, {});
