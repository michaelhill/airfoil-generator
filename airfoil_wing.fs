FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");

/**
 * Airfoil Wing
 *
 * Builds a lofted wing from NACA airfoil sections that are *calculated* (not looked up).
 * Each station is given as a NACA designation (e.g. 2412, 0012, 23012); camber, camber position and
 * thickness are calculated from it, and can be overridden per station with "Customize shape".
 * Sections are defined at any % of the span; each has its own chord, origin (pivot) %, angle of
 * attack and offsets. An optional wingtip cap can be added, and the wing can be mirrored about the root.
 *
 * Conventions (in the selected root plane):
 *   - chord runs along the plane's X direction, leading edge at the smaller X
 *   - thickness/camber is along the plane's Y direction
 *   - span runs along the plane normal (optionally flipped)
 *   - "Origin %" is the chord point that is placed on the stacking axis and that the
 *     section rotates about (25 % = quarter-chord line, the usual choice)
 *   - positive angle of attack pitches the nose up (+Y)
 *
 * Math: NACA 4-digit (Abbott & von Doenhoff), NACA 5-digit standard 2xx camber lines.
 * Reference implementation / UIUC comparison: tools/naca_reference.py
 */

export enum AirfoilFamily
{
    annotation { "Name" : "NACA 4-digit" }
    NACA4,
    annotation { "Name" : "NACA 5-digit (standard, non-reflex)" }
    NACA5
}

export enum WingtipTreatment
{
    annotation { "Name" : "None (flat)" }
    FLAT,
    annotation { "Name" : "Rounded (full round)" }
    ROUNDED,
    annotation { "Name" : "Elliptical (pointed)" }
    ELLIPTICAL,
    annotation { "Name" : "Raked (linear taper)" }
    RAKED
}

export enum StationTaper
{
    annotation { "Name" : "Straight (linear between stations)" }
    STRAIGHT,
    annotation { "Name" : "Smooth (one curve through all stations)" }
    SMOOTH
}

const SPAN_BOUNDS = { (meter) : [0.001, 1, 500], (centimeter) : 100, (millimeter) : 1000, (inch) : 40, (foot) : 3 } as LengthBoundSpec;
const CHORD_BOUNDS = { (meter) : [0.0001, 0.2, 100], (centimeter) : 20, (millimeter) : 200, (inch) : 8, (foot) : 0.6 } as LengthBoundSpec;
const OFFSET_BOUNDS = { (meter) : [-100, 0, 100], (centimeter) : 0, (millimeter) : 0, (inch) : 0, (foot) : 0 } as LengthBoundSpec;
const TIP_LENGTH_BOUNDS = { (meter) : [0.0001, 0.02, 50], (centimeter) : 2, (millimeter) : 20, (inch) : 0.8, (foot) : 0.06 } as LengthBoundSpec;
const SPAN_PCT_BOUNDS = { (unitless) : [0, 0, 100] } as RealBoundSpec;
const CAMBER_BOUNDS = { (unitless) : [0, 2, 9.5] } as RealBoundSpec;
const CAMBER_POS_BOUNDS = { (unitless) : [5, 40, 95] } as RealBoundSpec;
const THICKNESS_BOUNDS = { (unitless) : [1, 12, 40] } as RealBoundSpec;
const DESIGN_CL_BOUNDS = { (unitless) : [0, 0.3, 1.2] } as RealBoundSpec;
const CAMBER_INDEX_BOUNDS = { (unitless) : [1, 3, 5] } as IntegerBoundSpec;
const ORIGIN_PCT_BOUNDS = { (unitless) : [0, 25, 100] } as RealBoundSpec;
const AOA_BOUNDS = { (degree) : [-45, 0, 45] } as AngleBoundSpec;
const POINTS_BOUNDS = { (unitless) : [10, 40, 200] } as IntegerBoundSpec;
const TIP_SECTIONS_BOUNDS = { (unitless) : [2, 6, 20] } as IntegerBoundSpec;
const TIP_RATIO_BOUNDS = { (unitless) : [0.02, 0.3, 1] } as RealBoundSpec;
const TIP_SWEEP_BOUNDS = { (degree) : [-60, 0, 60] } as AngleBoundSpec;

// Smallest scale a cap section is allowed to reach (a loft cannot end on a zero-size section).
const MIN_CAP_SCALE = 0.03;

// NACA 5-digit standard camber-line constants for 210, 220, 230, 240, 250 (design CL = 0.3).
const FIVE_DIGIT_R = [0.0580, 0.1260, 0.2025, 0.2900, 0.3910];
const FIVE_DIGIT_K1 = [361.4, 51.64, 15.957, 6.643, 3.230];

annotation { "Feature Type Name" : "Airfoil wing", "Editing Logic Function" : "airfoilWingEditLogic" }
export const airfoilWing = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Root plane", "Filter" : GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.plane is Query;

        annotation { "Name" : "Flip span direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flip is boolean;

        annotation { "Name" : "Overall length (span)" }
        isLength(definition.span, SPAN_BOUNDS);

        annotation { "Name" : "Mirror about root plane (full wing)" }
        definition.mirror is boolean;

        annotation { "Name" : "Stations (first = root, last = tip)", "Item name" : "station", "Item label template" : "NACA #designation @ #position %" }
        definition.stations is array;
        for (var station in definition.stations)
        {
            annotation { "Name" : "Position along span (%)" }
            isReal(station.position, SPAN_PCT_BOUNDS);

            annotation { "Name" : "NACA designation (4-digit e.g. 2412, 5-digit e.g. 23012)", "Default" : "2412" }
            station.designation is string;

            // Derived from the designation by the editing logic; only used to pick which fields to show.
            annotation { "Name" : "Series", "UIHint" : UIHint.ALWAYS_HIDDEN }
            station.family is AirfoilFamily;

            annotation { "Name" : "Customize shape (start from the designation's values)" }
            station.custom is boolean;

            if (station.custom)
            {
                if (station.family == AirfoilFamily.NACA5)
                {
                    annotation { "Name" : "Design CL" }
                    isReal(station.designCl, DESIGN_CL_BOUNDS);

                    annotation { "Name" : "Camber position index (1-5 = 5-25% chord)" }
                    isInteger(station.posIndex, CAMBER_INDEX_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "Max camber (% chord)" }
                    isReal(station.camber, CAMBER_BOUNDS);

                    annotation { "Name" : "Camber position (% chord)" }
                    isReal(station.camberPos, CAMBER_POS_BOUNDS);
                }

                annotation { "Name" : "Thickness (% chord)" }
                isReal(station.thickness, THICKNESS_BOUNDS);
            }

            annotation { "Name" : "Chord length" }
            isLength(station.chord, CHORD_BOUNDS);

            annotation { "Name" : "Origin (% chord)" }
            isReal(station.origin, ORIGIN_PCT_BOUNDS);

            annotation { "Name" : "Angle of attack" }
            isAngle(station.aoa, AOA_BOUNDS);

            annotation { "Name" : "Sweep offset (along chord)" }
            isLength(station.sweep, OFFSET_BOUNDS);

            annotation { "Name" : "Vertical offset (dihedral / anhedral)" }
            isLength(station.vertical, OFFSET_BOUNDS);
        }

        annotation { "Name" : "Taper between stations" }
        definition.taper is StationTaper;

        annotation { "Name" : "Closed (sharp) trailing edge", "Default" : true }
        definition.closedTE is boolean;

        annotation { "Name" : "Points per surface" }
        isInteger(definition.pointsPerSide, POINTS_BOUNDS);

        annotation { "Name" : "Wingtip treatment" }
        definition.tip is WingtipTreatment;

        if (definition.tip != WingtipTreatment.FLAT)
        {
            annotation { "Name" : "Wingtip length (beyond tip station)" }
            isLength(definition.tipLength, TIP_LENGTH_BOUNDS);

            annotation { "Name" : "Wingtip sections" }
            isInteger(definition.tipSections, TIP_SECTIONS_BOUNDS);

            annotation { "Name" : "Continue sweep and dihedral of the last segment", "Default" : true }
            definition.tipContinue is boolean;

            annotation { "Name" : "Extra tip sweep angle (aft positive)" }
            isAngle(definition.tipSweep, TIP_SWEEP_BOUNDS);

            annotation { "Name" : "Continue twist (angle of attack change) of the last segment" }
            definition.tipContinueTwist is boolean;

            annotation { "Name" : "Extra twist reached at the end of the tip (nose up positive)" }
            isAngle(definition.tipTwist, AOA_BOUNDS);

            if (definition.tip == WingtipTreatment.RAKED)
            {
                annotation { "Name" : "Tip chord ratio" }
                isReal(definition.tipChordRatio, TIP_RATIO_BOUNDS);
            }
        }
    }
    {
        if (size(definition.stations) < 2)
            throw regenError("Add at least two stations (root and tip).", ["stations"]);

        const planeResult = try silent(evPlane(context, { "face" : definition.plane }));
        if (planeResult == undefined)
            throw regenError("Select a plane or planar face for the root section.", ["plane"]);

        const spanDir = definition.flip ? -planeResult.normal : planeResult.normal;
        const specs = buildSpecs(definition);

        if (definition.mirror && specs[0].zLen > 1e-6 * meter)
            throw regenError("Mirroring needs the first station at 0 % span (the root plane).", ["stations"]);

        const halfCount = definition.mirror ? 2 : 1;
        for (var h = 0; h < halfCount; h += 1)
        {
            buildHalf(context, id + ("half" ~ h), definition, specs, planeResult, (h == 0) ? spanDir : -spanDir);
        }

        if (definition.mirror)
        {
            opBoolean(context, id + "mirrorJoin", {
                        "tools" : qUnion([qBodyType(qCreatedBy(id + "half0", EntityType.BODY), BodyType.SOLID),
                                qBodyType(qCreatedBy(id + "half1", EntityType.BODY), BodyType.SOLID)]),
                        "operationType" : BooleanOperationType.UNION
                    });
        }

        reportFeatureInfo(context, id, wingReadout(wingStats(definition, specs), try silent(evVolume(context,
                    { "entities" : qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SOLID) }))));
    }, { "pointsPerSide" : 40, "closedTE" : true, "tip" : WingtipTreatment.FLAT, "tipSections" : 6, "tipChordRatio" : 0.3, "flip" : false,
            "mirror" : false, "taper" : StationTaper.STRAIGHT, "tipContinue" : true, "tipSweep" : 0 * degree,
            "tipContinueTwist" : false, "tipTwist" : 0 * degree });

/**
 * Builds one half wing running from the root plane along spanDir: one sketch per section, lofted either as a
 * single smooth loft or as straight segments between stations, plus a separate loft for the tip cap, all joined.
 * The wing and the cap are lofted separately because one loft through sections that are 1000 mm apart and then
 * 5 mm apart interpolates smoothly and overshoots wildly.
 */
function buildHalf(context is Context, id is Id, definition is map, specs is array, planeResult is Plane, spanDir is Vector)
{
    var profiles = [];
    for (var i = 0; i < size(specs); i += 1)
    {
        const spec = specs[i];
        const sketchId = id + ("section" ~ i);
        const sketchPlane = plane(planeResult.origin + spanDir * spec.zLen, planeResult.normal, planeResult.x);
        var sketch = newSketchOnPlane(context, sketchId, { "sketchPlane" : sketchPlane });

        const surfaces = airfoilSurfaces(spec, definition.pointsPerSide, definition.closedTE);
        var upper = [];
        var lower = [];
        for (var p in surfaces.upper)
            upper = append(upper, toSketch(p, spec));
        for (var p in surfaces.lower)
            lower = append(lower, toSketch(p, spec));

        skFitSpline(sketch, "upper", { "points" : upper });
        skFitSpline(sketch, "lower", { "points" : lower });
        if (!definition.closedTE)
        {
            skLineSegment(sketch, "trailingEdge", { "start" : upper[size(upper) - 1], "end" : lower[size(lower) - 1] });
        }
        skSolve(sketch);

        profiles = append(profiles, qSketchRegion(sketchId));
    }

    // Each group is a list of section indices that are lofted together.
    const userCount = size(definition.stations);
    var groups = [];
    if (definition.taper == StationTaper.SMOOTH || userCount == 2)
    {
        var all = [];
        for (var i = 0; i < userCount; i += 1)
            all = append(all, i);
        groups = append(groups, all);
    }
    else
    {
        for (var i = 0; i < userCount - 1; i += 1)
            groups = append(groups, [i, i + 1]);
    }
    if (size(specs) > userCount)
    {
        var cap = [];
        for (var i = userCount - 1; i < size(specs); i += 1)
            cap = append(cap, i);
        groups = append(groups, cap);
    }

    var loftBodies = [];
    for (var g = 0; g < size(groups); g += 1)
    {
        var groupProfiles = [];
        for (var k in groups[g])
            groupProfiles = append(groupProfiles, profiles[k]);
        const loftId = id + ("loft" ~ g);
        opLoft(context, loftId, { "profileSubqueries" : groupProfiles, "bodyType" : ToolBodyType.SOLID });
        loftBodies = append(loftBodies, qCreatedBy(loftId, EntityType.BODY));
    }
    if (size(loftBodies) > 1)
        opBoolean(context, id + "join", { "tools" : qUnion(loftBodies), "operationType" : BooleanOperationType.UNION });

    var sketchBodies = [];
    for (var i = 0; i < size(specs); i += 1)
        sketchBodies = append(sketchBodies, qCreatedBy(id + ("section" ~ i), EntityType.BODY));
    opDeleteBodies(context, id + "deleteSections", { "entities" : qUnion(sketchBodies) });
}

/**
 * Planform numbers for the readout, all in millimetres / degrees. Everything is for the half wing built from the
 * given sections (tip cap included), then doubled for the full-wing figures, since the wing is symmetric when mirrored
 * and the usual aspect ratio is defined for the full wing anyway.
 */
function wingStats(definition is map, specs is array) returns map
{
    var halfArea = 0;
    var chordSquared = 0;
    for (var i = 0; i < size(specs) - 1; i += 1)
    {
        const c1 = specs[i].chord / millimeter;
        const c2 = specs[i + 1].chord / millimeter;
        const dz = (specs[i + 1].zLen - specs[i].zLen) / millimeter;
        halfArea += (c1 + c2) / 2 * dz;
        chordSquared += dz / 3 * (c1 * c1 + c1 * c2 + c2 * c2);
    }

    const userCount = size(definition.stations);
    const rootSpec = specs[0];
    const tipSpec = specs[userCount - 1];
    const halfSpan = (specs[size(specs) - 1].zLen - rootSpec.zLen) / millimeter;
    const stationRun = tipSpec.zLen - rootSpec.zLen;
    return {
            "fullSpan" : 2 * halfSpan,
            "fullArea" : 2 * halfArea,
            "aspectRatio" : 4 * halfSpan * halfSpan / (2 * halfArea),
            "meanChord" : halfArea / halfSpan,
            "mac" : chordSquared / halfArea,
            "rootChord" : rootSpec.chord / millimeter,
            "tipChord" : tipSpec.chord / millimeter,
            "taper" : tipSpec.chord / rootSpec.chord,
            "leSweep" : atan2(leadingEdgeX(tipSpec, 0) - leadingEdgeX(rootSpec, 0), stationRun) / degree,
            "quarterSweep" : atan2(leadingEdgeX(tipSpec, 0.25) - leadingEdgeX(rootSpec, 0.25), stationRun) / degree,
            "dihedral" : atan2(tipSpec.yOff - rootSpec.yOff, stationRun) / degree
        };
}

/** X position of the point at the given fraction of the chord (0 = leading edge) of a section spec. */
function leadingEdgeX(spec is map, chordFraction is number) returns ValueWithUnits
{
    return spec.xOff + (chordFraction - spec.origin) * spec.chord * cos(spec.aoa);
}

/** Formats wingStats (and the solid's volume, if it could be measured) for reportFeatureInfo. */
function wingReadout(stats is map, volume) returns string
{
    var text = "Full span " ~ roundToPrecision(stats.fullSpan, 1) ~ " mm, area " ~ roundToPrecision(stats.fullArea / 100, 1)
        ~ " cm^2, aspect ratio " ~ roundToPrecision(stats.aspectRatio, 2)
        ~ "\nRoot/tip chord " ~ roundToPrecision(stats.rootChord, 1) ~ "/" ~ roundToPrecision(stats.tipChord, 1)
        ~ " mm (taper " ~ roundToPrecision(stats.taper, 2) ~ "), mean chord " ~ roundToPrecision(stats.meanChord, 1)
        ~ " mm, MAC " ~ roundToPrecision(stats.mac, 1) ~ " mm"
        ~ "\nSweep: leading edge " ~ roundToPrecision(stats.leSweep, 1) ~ " deg, quarter chord " ~ roundToPrecision(stats.quarterSweep, 1)
        ~ " deg; dihedral " ~ roundToPrecision(stats.dihedral, 1) ~ " deg";
    if (volume != undefined)
        text = text ~ "\nVolume " ~ roundToPrecision(volume / (centimeter ^ 3), 1) ~ " cm^3";
    return text;
}

/** A station map with every field the precondition needs, used to seed a new feature. */
function seedStation(position is number, designation is string, chord is ValueWithUnits) returns map
{
    return { "position" : position, "designation" : designation, "family" : AirfoilFamily.NACA4, "custom" : false,
            "chord" : chord, "origin" : 25, "aoa" : 0 * degree, "sweep" : 0 * millimeter, "vertical" : 0 * millimeter };
}

/** Keeps only the digits of a designation, so "NACA 2412" and "2412" are equivalent. */
function nacaDigits(designation is string) returns string
{
    var digits = "";
    for (var i = 0; i < length(designation); i += 1)
    {
        const c = substring(designation, i, i + 1);
        if (indexOf("0123456789", c) >= 0)
            digits = digits ~ c;
    }
    return digits;
}

/**
 * Decodes a NACA designation into shape values (percentages for camber, camberPos, thickness).
 *   4-digit MPTT : max camber M %, at P * 10 % chord, thickness TT %
 *   5-digit LPSTT: design CL = 0.15 * L, camber position index P (P * 5 % chord), S = 0 (standard), thickness TT %
 * Returns { ok, error, family, camber, camberPos, designCl, posIndex, thickness }.
 */
function parseNaca(designation is string) returns map
{
    const d = nacaDigits(designation);
    const n = length(d);
    if (n != 4 && n != 5)
        return { "ok" : false, "error" : "NACA designation must have 4 or 5 digits (got \"" ~ designation ~ "\")." };

    const thickness = stringToNumber(substring(d, n - 2, n));
    if (thickness < 1 || thickness > 40)
        return { "ok" : false, "error" : "Thickness in the designation must be 01 to 40 (got " ~ thickness ~ ")." };

    if (n == 4)
    {
        const camber = stringToNumber(substring(d, 0, 1));
        const posDigit = stringToNumber(substring(d, 1, 2));
        if (camber > 0 && posDigit == 0)
            return { "ok" : false, "error" : "A cambered 4-digit airfoil needs a non-zero camber position digit." };
        return {
                "ok" : true, "family" : AirfoilFamily.NACA4,
                "camber" : camber, "camberPos" : (posDigit == 0) ? 40 : posDigit * 10,
                "designCl" : 0.3, "posIndex" : 3, "thickness" : thickness
            };
    }

    const liftDigit = stringToNumber(substring(d, 0, 1));
    const posIndex = stringToNumber(substring(d, 1, 2));
    const reflex = stringToNumber(substring(d, 2, 3));
    if (reflex != 0)
        return { "ok" : false, "error" : "Reflexed 5-digit airfoils (third digit 1) are not supported yet." };
    if (posIndex < 1 || posIndex > 5)
        return { "ok" : false, "error" : "5-digit camber position digit must be 1 to 5 (210 to 250 series)." };
    if (liftDigit > 8)
        return { "ok" : false, "error" : "5-digit design lift digit must be 0 to 8." };
    return {
            "ok" : true, "family" : AirfoilFamily.NACA5,
            "camber" : 2, "camberPos" : 40,
            "designCl" : 0.15 * liftDigit, "posIndex" : posIndex, "thickness" : thickness
        };
}

/**
 * Editing logic: seeds a new feature with a root and a tip station, keeps each station's hidden series
 * (4/5-digit) in step with its designation, and when "Customize shape" is switched on, or the designation
 * changes while customizing, fills the editable values from the designation.
 */
export function airfoilWingEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean) returns map
{
    if (isCreating && (definition.stations == undefined || size(definition.stations) == 0))
        definition.stations = [seedStation(0, "2412", 200 * millimeter), seedStation(100, "0012", 120 * millimeter)];
    if (definition.stations == undefined)
        return definition;
    for (var i = 0; i < size(definition.stations); i += 1)
    {
        const st = definition.stations[i];
        const parsed = parseNaca(st.designation);
        if (parsed.ok)
            definition.stations[i].family = parsed.family;
        if (!st.custom)
            continue;
        var old = undefined;
        if (oldDefinition.stations != undefined && i < size(oldDefinition.stations))
            old = oldDefinition.stations[i];
        if (old != undefined && old.custom == true && old.designation == st.designation)
            continue;
        const naca = parseNaca(st.designation);
        if (!naca.ok)
            continue;
        definition.stations[i].thickness = naca.thickness;
        if (naca.family == AirfoilFamily.NACA5)
        {
            definition.stations[i].designCl = naca.designCl;
            definition.stations[i].posIndex = naca.posIndex;
        }
        else
        {
            definition.stations[i].camber = naca.camber;
            definition.stations[i].camberPos = naca.camberPos;
        }
    }
    return definition;
}

/** Turns the user's stations (plus generated wingtip sections) into a flat list of section specs. */
function buildSpecs(definition is map) returns array
{
    const sorted = sort(definition.stations, function(a, b)
        {
            return a.position - b.position;
        });

    var specs = [];
    for (var i = 0; i < size(sorted); i += 1)
    {
        const st = sorted[i];
        if (i > 0 && st.position - sorted[i - 1].position < 1e-6)
            throw regenError("Stations must have different span positions.", ["stations"]);

        const naca = parseNaca(st.designation);
        if (!naca.ok)
            throw regenError("Station at " ~ st.position ~ " %: " ~ naca.error, ["stations"]);
        // The designation supplies the defaults; "Customize shape" replaces them with the edited values.
        const five = naca.family == AirfoilFamily.NACA5;

        specs = append(specs, {
                    "zLen" : definition.span * st.position / 100,
                    "family" : naca.family,
                    "camber" : five ? 0 : (st.custom ? st.camber : naca.camber) / 100,
                    "camberPos" : five ? 0 : (st.custom ? st.camberPos : naca.camberPos) / 100,
                    "designCl" : five ? (st.custom ? st.designCl : naca.designCl) : 0,
                    "posIndex" : five ? (st.custom ? st.posIndex : naca.posIndex) : 1,
                    "thickness" : (st.custom ? st.thickness : naca.thickness) / 100,
                    "chord" : st.chord,
                    "origin" : st.origin / 100,
                    "aoa" : st.aoa,
                    "xOff" : st.sweep,
                    "yOff" : st.vertical,
                    "yScale" : 1
                });
    }

    if (definition.tip != WingtipTreatment.FLAT)
    {
        const tipSpec = specs[size(specs) - 1];
        const n = definition.tipSections;
        const maxAngle = acos(MIN_CAP_SCALE);   // elliptical cap runs from 0 to just short of 90 degrees
        // The last segment's rates of change, carried through the cap by the options below.
        const prevSpec = specs[size(sorted) - 2];
        const run = tipSpec.zLen - prevSpec.zLen;
        for (var j = 1; j <= n; j += 1)
        {
            const u = j / n;
            var cap = tipSpec;
            if (definition.tip == WingtipTreatment.ROUNDED)
            {
                // Thickness and camber shrink as cos(theta); span advances as sin(theta): elliptical cap.
                const theta = maxAngle * u;
                cap.zLen = tipSpec.zLen + definition.tipLength * sin(theta);
                cap.yScale = cos(theta);
            }
            else if (definition.tip == WingtipTreatment.ELLIPTICAL)
            {
                // Whole section (chord and thickness) shrinks about the origin point.
                const theta = maxAngle * u;
                cap.zLen = tipSpec.zLen + definition.tipLength * sin(theta);
                cap.chord = tipSpec.chord * cos(theta);
            }
            else
            {
                // Raked: chord tapers linearly to the tip chord ratio, leading edge stays put.
                const scale = 1 - (1 - definition.tipChordRatio) * u;
                const oldOrigin = tipSpec.origin;
                cap.zLen = tipSpec.zLen + definition.tipLength * u;
                cap.chord = tipSpec.chord * scale;
                cap.origin = 0;
                // Move the stacking point from the old origin % to the leading edge so the LE does not shift.
                const d = -oldOrigin * tipSpec.chord;
                cap.xOff = tipSpec.xOff + d * cos(tipSpec.aoa);
                cap.yOff = tipSpec.yOff - d * sin(tipSpec.aoa);
            }
            // Carry the sweep and dihedral of the last segment on through the cap, plus any extra tip sweep, so the
            // cap blends with the tip station instead of kinking back to a straight-span tip.
            const dz = cap.zLen - tipSpec.zLen;
            var slopeX = tan(definition.tipSweep);
            var slopeY = 0;
            if (definition.tipContinue)
            {
                slopeX += (tipSpec.xOff - prevSpec.xOff) / run;
                slopeY += (tipSpec.yOff - prevSpec.yOff) / run;
            }
            cap.xOff += slopeX * dz;
            cap.yOff += slopeY * dz;

            // Twist: the last segment's rate of change of angle of attack, plus extra twist spread over the cap.
            // (The raked cap pivots about its leading edge, the others about their origin point.)
            var twist = definition.tipTwist * u;
            if (definition.tipContinueTwist)
                twist += (tipSpec.aoa - prevSpec.aoa) / run * dz;
            cap.aoa += twist;
            specs = append(specs, cap);
        }
    }
    return specs;
}

/**
 * Unit-chord surface points, leading edge to trailing edge, cosine spaced.
 * Returns { upper : [[x, y]...], lower : [[x, y]...] } with x, y as plain numbers (fraction of chord).
 */
function airfoilSurfaces(spec is map, n is number, closedTE is boolean) returns map
{
    var upper = [];
    var lower = [];
    for (var i = 0; i < n; i += 1)
    {
        const beta = PI * radian * (i / (n - 1));
        const x = 0.5 * (1 - cos(beta));
        const yt = thicknessDistribution(x, spec.thickness, closedTE);

        var yc = 0;
        var dyc = 0;
        if (spec.family == AirfoilFamily.NACA4)
        {
            const m = spec.camber;
            const p = spec.camberPos;
            if (m > 1e-9 && p > 1e-9)
            {
                if (x < p)
                {
                    yc = m / (p * p) * (2 * p * x - x * x);
                    dyc = 2 * m / (p * p) * (p - x);
                }
                else
                {
                    yc = m / ((1 - p) * (1 - p)) * ((1 - 2 * p) + 2 * p * x - x * x);
                    dyc = 2 * m / ((1 - p) * (1 - p)) * (p - x);
                }
            }
        }
        else
        {
            const r = FIVE_DIGIT_R[spec.posIndex - 1];
            const k1 = FIVE_DIGIT_K1[spec.posIndex - 1] * spec.designCl / 0.3;
            if (x < r)
            {
                yc = k1 / 6 * (x * x * x - 3 * r * x * x + r * r * (3 - r) * x);
                dyc = k1 / 6 * (3 * x * x - 6 * r * x + r * r * (3 - r));
            }
            else
            {
                yc = k1 * r * r * r / 6 * (1 - x);
                dyc = -k1 * r * r * r / 6;
            }
        }

        const theta = atan(dyc);
        upper = append(upper, [x - yt * sin(theta), yc + yt * cos(theta)]);
        lower = append(lower, [x + yt * sin(theta), yc - yt * cos(theta)]);
    }

    // The leading edge is shared and, for a closed trailing edge, so is the trailing edge.
    lower[0] = upper[0];
    if (closedTE)
        lower[n - 1] = upper[n - 1];
    return { "upper" : upper, "lower" : lower };
}

/** NACA four-digit half-thickness. The x^4 coefficient -0.1036 closes the trailing edge exactly; -0.1015 leaves it open. */
function thicknessDistribution(x is number, t is number, closedTE is boolean) returns number
{
    const a4 = closedTE ? -0.1036 : -0.1015;
    return 5 * t * (0.2969 * sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x * x * x + a4 * x * x * x * x);
}

/** Maps a unit-chord point to sketch coordinates: scale, pivot about the origin %, rotate by angle of attack, offset. */
function toSketch(point is array, spec is map) returns Vector
{
    const fx = (point[0] - spec.origin) * spec.chord;
    const fy = point[1] * spec.yScale * spec.chord;
    const ca = cos(spec.aoa);
    const sa = sin(spec.aoa);
    // Positive angle of attack pitches the nose (smaller x) up.
    return vector(spec.xOff + fx * ca + fy * sa, spec.yOff - fx * sa + fy * ca);
}
