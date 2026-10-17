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
 * Math: NACA 4-digit (Abbott & von Doenhoff), NACA 5-digit standard (2x0) and reflexed (2x1) camber lines.
 * A station can instead use pasted coordinates (Selig or Lednicer .dat, e.g. from the UIUC database).
 * Reference implementations / UIUC comparison: tools/naca_reference.py, tools/dat_reference.py
 */

export enum AirfoilFamily
{
    annotation { "Name" : "NACA 4-digit" }
    NACA4,
    annotation { "Name" : "NACA 5-digit" }
    NACA5
}

export enum AirfoilSource
{
    annotation { "Name" : "NACA designation" }
    NACA,
    annotation { "Name" : "Pasted coordinates (.dat)" }
    COORDINATES
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

// Pasted coordinates: an open trailing edge is made at least this thick (fraction of chord, as the NACA open edge).
const MIN_OPEN_GAP = 0.0025;
// Pasted coordinates: fine grid on which the linear trailing-edge pinch is checked for crossing, and where the
// fallback closing (thickness tapered to zero) starts.
const CLOSE_CHECK_POINTS = 400;
const CLOSE_RAMP_START = 0.9;

// Smallest scale a cap section is allowed to reach (a loft cannot end on a zero-size section).
const MIN_CAP_SCALE = 0.03;

// NACA 5-digit standard camber-line constants for 210, 220, 230, 240, 250 (design CL = 0.3).
const FIVE_DIGIT_R = [0.0580, 0.1260, 0.2025, 0.2900, 0.3910];
const FIVE_DIGIT_K1 = [361.4, 51.64, 15.957, 6.643, 3.230];
// Reflexed camber lines 221, 231, 241, 251 (no 211): r, k1 and k2/k1, indexed like the above (first entry unused).
// They are designed for zero pitching moment about the quarter chord.
const REFLEX_R = [0, 0.1300, 0.2170, 0.3180, 0.4410];
const REFLEX_K1 = [0, 51.990, 15.793, 6.520, 3.191];
const REFLEX_K21 = [0, 0.000764, 0.00677, 0.0303, 0.1355];

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

        annotation { "Name" : "Stations (first = root, last = tip)", "Item name" : "station", "Item label template" : "#label @ #position %" }
        definition.stations is array;
        for (var station in definition.stations)
        {
            annotation { "Name" : "Position along span (%)" }
            isReal(station.position, SPAN_PCT_BOUNDS);

            annotation { "Name" : "Airfoil source" }
            station.source is AirfoilSource;

            // Set by the editing logic for the list label ("NACA 2412" or the name in the pasted file).
            annotation { "Name" : "Label", "UIHint" : UIHint.ALWAYS_HIDDEN }
            station.label is string;

            if (station.source == AirfoilSource.COORDINATES)
            {
                annotation { "Name" : "Coordinates (paste a whole Selig or Lednicer .dat file)" }
                station.coordinates is string;
            }
            else
            {
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

                        annotation { "Name" : "Reflexed camber line (no pitching moment, index 2-5)" }
                        station.reflex is boolean;
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

            if (definition.tip != WingtipTreatment.RAKED)
            {
                annotation { "Name" : "Continue chord taper of the last segment", "Default" : true }
                definition.tipContinueTaper is boolean;
            }

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
        const built = buildSpecs(definition);

        if (definition.mirror && built.specs[0].zLen > 1e-6 * meter)
            throw regenError("Mirroring needs the first station at 0 % span (the root plane).", ["stations"]);

        const halfCount = definition.mirror ? 2 : 1;
        for (var h = 0; h < halfCount; h += 1)
        {
            buildHalf(context, id + ("half" ~ h), definition, built, planeResult, (h == 0) ? spanDir : -spanDir);
        }

        if (definition.mirror)
        {
            opBoolean(context, id + "mirrorJoin", {
                        "tools" : qUnion([qBodyType(qCreatedBy(id + "half0", EntityType.BODY), BodyType.SOLID),
                                qBodyType(qCreatedBy(id + "half1", EntityType.BODY), BodyType.SOLID)]),
                        "operationType" : BooleanOperationType.UNION
                    });
        }

        reportFeatureInfo(context, id, wingReadout(wingStats(built), try silent(evVolume(context,
                    { "entities" : qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SOLID) }))));
    }, { "pointsPerSide" : 40, "closedTE" : true, "tip" : WingtipTreatment.FLAT, "tipSections" : 6, "tipChordRatio" : 0.3, "flip" : false,
            "mirror" : false, "taper" : StationTaper.STRAIGHT, "tipContinue" : true, "tipSweep" : 0 * degree,
            "tipContinueTwist" : false, "tipTwist" : 0 * degree, "tipContinueTaper" : false });

/**
 * Builds one half wing running from the root plane along spanDir: one sketch per section, lofted either as a
 * single smooth loft or as straight segments between stations, plus a separate loft for the tip cap, all joined.
 * The wing and the cap are lofted separately because one loft through sections that are 1000 mm apart and then
 * 5 mm apart interpolates smoothly and overshoots wildly. built.specs holds the wing sections up to
 * built.tipIndex (the tip station) followed by the cap sections.
 */
function buildHalf(context is Context, id is Id, definition is map, built is map, planeResult is Plane, spanDir is Vector)
{
    const specs = built.specs;
    const tipIndex = built.tipIndex;
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

    // Each group is a list of section indices that are lofted together: the whole wing when smooth, otherwise each
    // segment from one station to the next (with any extra sections added for twist).
    var groups = [];
    var group = [0];
    for (var i = 1; i <= tipIndex; i += 1)
    {
        group = append(group, i);
        if (specs[i].station && definition.taper != StationTaper.SMOOTH)
        {
            groups = append(groups, group);
            group = [i];
        }
    }
    if (size(group) > 1)
        groups = append(groups, group);
    if (size(specs) > tipIndex + 1)
    {
        var cap = [];
        for (var i = tipIndex; i < size(specs); i += 1)
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
function wingStats(built is map) returns map
{
    const specs = built.specs;
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

    const rootSpec = specs[0];
    const tipSpec = specs[built.tipIndex];
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
    return { "position" : position, "source" : AirfoilSource.NACA, "label" : "NACA " ~ designation, "coordinates" : "",
            "designation" : designation, "family" : AirfoilFamily.NACA4, "custom" : false, "chord" : chord, "origin" : 25, "aoa" : 0 * degree, "sweep" : 0 * millimeter, "vertical" : 0 * millimeter };
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
 *   5-digit LPSTT: design CL = 0.15 * L, camber position index P (P * 5 % chord), S = 0 standard or 1 reflexed,
 *                  thickness TT %
 * Returns { ok, error, family, camber, camberPos, designCl, posIndex, reflex, thickness }.
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
                "designCl" : 0.3, "posIndex" : 3, "reflex" : false, "thickness" : thickness
            };
    }

    const liftDigit = stringToNumber(substring(d, 0, 1));
    const posIndex = stringToNumber(substring(d, 1, 2));
    const reflex = stringToNumber(substring(d, 2, 3));
    if (reflex > 1)
        return { "ok" : false, "error" : "5-digit third digit must be 0 (standard) or 1 (reflexed)." };
    if (posIndex < 1 || posIndex > 5)
        return { "ok" : false, "error" : "5-digit camber position digit must be 1 to 5 (210 to 250 series)." };
    if (reflex == 1 && posIndex == 1)
        return { "ok" : false, "error" : "Reflexed 5-digit airfoils exist for 221 to 251 only (no 211)." };
    if (liftDigit > 8)
        return { "ok" : false, "error" : "5-digit design lift digit must be 0 to 8." };
    return {
            "ok" : true, "family" : AirfoilFamily.NACA5,
            "camber" : 2, "camberPos" : 40,
            "designCl" : 0.15 * liftDigit, "posIndex" : posIndex, "reflex" : reflex == 1, "thickness" : thickness
        };
}

/**
 * Editing logic: seeds a new feature with a root and a tip station, keeps each station's hidden list label and
 * series (4/5-digit) in step with its designation or pasted coordinates, and when "Customize shape" is switched on,
 * or the designation changes while customizing, fills the editable values from the designation.
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
        if (st.source == AirfoilSource.COORDINATES)
        {
            const coords = parseCoordinates(st.coordinates == undefined ? "" : st.coordinates);
            definition.stations[i].label = coords.ok ? (coords.name == "" ? "Coordinates" : coords.name) : "Coordinates (not readable)";
            continue;
        }
        definition.stations[i].label = "NACA " ~ nacaDigits(st.designation);
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
            definition.stations[i].reflex = naca.reflex;
        }
        else
        {
            definition.stations[i].camber = naca.camber;
            definition.stations[i].camberPos = naca.camberPos;
        }
    }
    return definition;
}

/** Sections per segment (on average) used to follow the smooth curve when the taper is "Smooth". */
const SMOOTH_SECTIONS_PER_SEGMENT = 8;

/**
 * Largest change in angle of attack between neighbouring sections. A loft blends corresponding points of two sections
 * linearly, so a twisted segment comes out short in chord in the middle by about cos(half the twist); extra sections
 * keep that below cos(5 deg), 0.4 %.
 */
const MAX_TWIST_STEP = 10 * degree;

/**
 * Turns the user's stations (plus generated smooth-curve sections and wingtip sections) into a flat list of section
 * specs. Returns { specs, tipIndex }: specs[0..tipIndex] are the wing (tipIndex is the tip station), anything after
 * that is the wingtip cap.
 */
function buildSpecs(definition is map) returns map
{
    const sorted = sort(definition.stations, function(a, b)
        {
            return a.position - b.position;
        });

    var userSpecs = [];
    for (var i = 0; i < size(sorted); i += 1)
    {
        const st = sorted[i];
        if (i > 0 && st.position - sorted[i - 1].position < 1e-6)
            throw regenError("Stations must have different span positions.", ["stations"]);

        // Placement, the same for both kinds of station.
        const placement = {
                "zLen" : definition.span * st.position / 100,
                "chord" : st.chord,
                "origin" : st.origin / 100,
                "aoa" : st.aoa,
                "xOff" : st.sweep,
                "yOff" : st.vertical,
                "yScale" : 1,
                "station" : true
            };
        if (st.source == AirfoilSource.COORDINATES)
        {
            const coords = parseCoordinates(st.coordinates == undefined ? "" : st.coordinates);
            if (!coords.ok)
                throw regenError("Station at " ~ st.position ~ " %: " ~ coords.error, ["stations"]);
            userSpecs = append(userSpecs, mergeMaps(placement, { "family" : AirfoilFamily.NACA4, "coords" : coords }));
            continue;
        }

        const naca = parseNaca(st.designation);
        if (!naca.ok)
            throw regenError("Station at " ~ st.position ~ " %: " ~ naca.error, ["stations"]);
        // The designation supplies the defaults; "Customize shape" replaces them with the edited values.
        const five = naca.family == AirfoilFamily.NACA5;

        if (five && st.custom && st.reflex == true && st.posIndex == 1)
            throw regenError("Station at " ~ st.position ~ " %: a reflexed camber line needs a camber position index of 2 to 5.", ["stations"]);

        userSpecs = append(userSpecs, mergeMaps(placement, {
                    "family" : naca.family,
                    "camber" : five ? 0 : (st.custom ? st.camber : naca.camber) / 100,
                    "camberPos" : five ? 0 : (st.custom ? st.camberPos : naca.camberPos) / 100,
                    "designCl" : five ? (st.custom ? st.designCl : naca.designCl) : 0,
                    "posIndex" : five ? (st.custom ? st.posIndex : naca.posIndex) : 1,
                    "reflex" : five && (st.custom ? st.reflex == true : naca.reflex),
                    "thickness" : (st.custom ? st.thickness : naca.thickness) / 100
                }));
    }

    // With a smooth taper the placement of the sections (chord, sweep, dihedral, twist) follows one smooth curve through
    // the stations. We evaluate that curve ourselves, so the sections are evenly spaced (a loft through unevenly spaced
    // sections overshoots) and we know the exact slope at the tip station to continue into the wingtip cap.
    const userCount = size(userSpecs);
    const smooth = definition.taper == StationTaper.SMOOTH && userCount > 2;
    var wingSpecs = refineTwist(userSpecs);
    var tipSlopeX = (userSpecs[userCount - 1].xOff - userSpecs[userCount - 2].xOff) / (userSpecs[userCount - 1].zLen - userSpecs[userCount - 2].zLen);
    var tipSlopeY = (userSpecs[userCount - 1].yOff - userSpecs[userCount - 2].yOff) / (userSpecs[userCount - 1].zLen - userSpecs[userCount - 2].zLen);
    var tipSlopeAoa = (userSpecs[userCount - 1].aoa - userSpecs[userCount - 2].aoa) / (userSpecs[userCount - 1].zLen - userSpecs[userCount - 2].zLen);
    var tipSlopeChord = (userSpecs[userCount - 1].chord - userSpecs[userCount - 2].chord) / (userSpecs[userCount - 1].zLen - userSpecs[userCount - 2].zLen);
    if (smooth)
    {
        const curve = smoothSpecs(userSpecs);
        wingSpecs = curve.specs;
        tipSlopeX = curve.slopeX;
        tipSlopeY = curve.slopeY;
        tipSlopeAoa = curve.slopeAoa * degree / millimeter;
        tipSlopeChord = curve.slopeChord;
    }

    var specs = wingSpecs;
    const tipIndex = size(wingSpecs) - 1;
    if (definition.tip != WingtipTreatment.FLAT)
    {
        const tipSpec = wingSpecs[tipIndex];
        const n = definition.tipSections;
        // Rounded and elliptical caps can carry on the chord taper at the tip station, so the planform has no kink
        // at the join; the cap shape then scales that continued chord. (A raked tip is a deliberate break.)
        const continueTaper = definition.tip != WingtipTreatment.RAKED && definition.tipContinueTaper == true;
        const maxAngle = acos(MIN_CAP_SCALE);   // elliptical cap runs from 0 to just short of 90 degrees
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
                cap.chord = baseChord(tipSpec, cap, tipSlopeChord, continueTaper);
            }
            else if (definition.tip == WingtipTreatment.ELLIPTICAL)
            {
                // Whole section (chord and thickness) shrinks about the origin point.
                const theta = maxAngle * u;
                cap.zLen = tipSpec.zLen + definition.tipLength * sin(theta);
                cap.chord = baseChord(tipSpec, cap, tipSlopeChord, continueTaper) * cos(theta);
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
            // Carry the sweep and dihedral at the tip station on through the cap (the slope of the curve there when the
            // taper is smooth, otherwise that of the last segment), plus any extra tip sweep, so the cap blends with the
            // tip station instead of kinking back to a straight-span tip.
            const dz = cap.zLen - tipSpec.zLen;
            var slopeX = tan(definition.tipSweep);
            var slopeY = 0;
            if (definition.tipContinue)
            {
                slopeX += tipSlopeX;
                slopeY += tipSlopeY;
            }
            cap.xOff += slopeX * dz;
            cap.yOff += slopeY * dz;

            // Twist: the rate of change of angle of attack at the tip station, plus extra twist spread over the cap.
            // (The raked cap pivots about its leading edge, the others about their origin point.)
            var twist = definition.tipTwist * u;
            if (definition.tipContinueTwist)
                twist += tipSlopeAoa * dz;
            cap.aoa += twist;
            specs = append(specs, cap);
        }
    }
    return { "specs" : specs, "tipIndex" : tipIndex };
}

/**
 * Straight taper: the stations, with evenly spaced extra sections in any segment that twists more than MAX_TWIST_STEP.
 * Their placement is interpolated linearly and their airfoil is a blend of the two stations, so the segment keeps the
 * same straight taper, sweep and dihedral.
 */
function refineTwist(userSpecs is array) returns array
{
    var specs = [];
    for (var k = 0; k < size(userSpecs) - 1; k += 1)
    {
        const a = userSpecs[k];
        const b = userSpecs[k + 1];
        specs = append(specs, a);
        const steps = ceil(abs((b.aoa - a.aoa) / MAX_TWIST_STEP) - 1e-9);
        for (var j = 1; j < steps; j += 1)
        {
            const t = j / steps;
            var section = a;
            section.station = false;
            section.zLen = a.zLen + t * (b.zLen - a.zLen);
            section.chord = a.chord + t * (b.chord - a.chord);
            section.origin = a.origin + t * (b.origin - a.origin);
            section.aoa = a.aoa + t * (b.aoa - a.aoa);
            section.xOff = a.xOff + t * (b.xOff - a.xOff);
            section.yOff = a.yOff + t * (b.yOff - a.yOff);
            section.blendA = a;
            section.blendB = b;
            section.blendT = t;
            specs = append(specs, section);
        }
    }
    return append(specs, userSpecs[size(userSpecs) - 1]);
}

/**
 * Chord of a cap section before the cap shape is applied: the tip chord, or (when continuing the taper) the tip chord
 * extended at the taper rate of the tip station, kept above a fifth of the tip chord.
 */
function baseChord(tipSpec is map, cap is map, slope is number, continueTaper is boolean) returns ValueWithUnits
{
    if (!continueTaper)
        return tipSpec.chord;
    return max(tipSpec.chord + slope * (cap.zLen - tipSpec.zLen), 0.2 * tipSpec.chord);
}

/**
 * Sections along a smooth curve through the stations. Chord, origin, sweep, dihedral and angle of attack are
 * interpolated with a shape-preserving cubic (no overshoot between stations); the section shape between two stations
 * is a blend of the two airfoils. Returns { specs, slopeX, slopeY, slopeAoa, slopeChord } where the slopes are the
 * derivatives at the tip station (mm per mm, mm per mm, degrees per mm, mm per mm).
 */
function smoothSpecs(userSpecs is array) returns map
{
    const count = size(userSpecs);
    var z = [];
    var chord = [];
    var origin = [];
    var aoa = [];
    var xOff = [];
    var yOff = [];
    for (var spec in userSpecs)
    {
        z = append(z, spec.zLen / millimeter);
        chord = append(chord, spec.chord / millimeter);
        origin = append(origin, spec.origin);
        aoa = append(aoa, spec.aoa / degree);
        xOff = append(xOff, spec.xOff / millimeter);
        yOff = append(yOff, spec.yOff / millimeter);
    }
    const dChord = pchipSlopes(z, chord);
    const dOrigin = pchipSlopes(z, origin);
    const dAoa = pchipSlopes(z, aoa);
    const dXOff = pchipSlopes(z, xOff);
    const dYOff = pchipSlopes(z, yOff);

    const spacing = (z[count - 1] - z[0]) / (SMOOTH_SECTIONS_PER_SEGMENT * (count - 1));
    var specs = [];
    for (var k = 0; k < count - 1; k += 1)
    {
        specs = append(specs, userSpecs[k]);
        const h = z[k + 1] - z[k];
        const intervals = max([2, round(h / spacing), ceil(abs(aoa[k + 1] - aoa[k]) / (MAX_TWIST_STEP / degree) - 1e-9)]);
        for (var j = 1; j < intervals; j += 1)
        {
            const t = j / intervals;
            var section = userSpecs[k];
            section.station = false;
            section.zLen = (z[k] + t * h) * millimeter;
            section.chord = hermite(chord[k], chord[k + 1], dChord[k], dChord[k + 1], h, t) * millimeter;
            section.origin = hermite(origin[k], origin[k + 1], dOrigin[k], dOrigin[k + 1], h, t);
            section.aoa = hermite(aoa[k], aoa[k + 1], dAoa[k], dAoa[k + 1], h, t) * degree;
            section.xOff = hermite(xOff[k], xOff[k + 1], dXOff[k], dXOff[k + 1], h, t) * millimeter;
            section.yOff = hermite(yOff[k], yOff[k + 1], dYOff[k], dYOff[k + 1], h, t) * millimeter;
            section.blendA = userSpecs[k];
            section.blendB = userSpecs[k + 1];
            section.blendT = t;
            specs = append(specs, section);
        }
    }
    specs = append(specs, userSpecs[count - 1]);
    return { "specs" : specs, "slopeX" : dXOff[count - 1], "slopeY" : dYOff[count - 1], "slopeAoa" : dAoa[count - 1],
            "slopeChord" : dChord[count - 1] };
}

/** Cubic Hermite interpolation on a segment of length h, at fraction t, from end values and end slopes. */
function hermite(y0 is number, y1 is number, d0 is number, d1 is number, h is number, t is number) returns number
{
    const t2 = t * t;
    const t3 = t2 * t;
    return (2 * t3 - 3 * t2 + 1) * y0 + (t3 - 2 * t2 + t) * h * d0 + (-2 * t3 + 3 * t2) * y1 + (t3 - t2) * h * d1;
}

/** Slopes for a shape-preserving (PCHIP) cubic through the points (x[k], y[k]); x must be increasing. */
function pchipSlopes(x is array, y is array) returns array
{
    const n = size(x);
    var h = [];
    var delta = [];
    for (var k = 0; k < n - 1; k += 1)
    {
        h = append(h, x[k + 1] - x[k]);
        delta = append(delta, (y[k + 1] - y[k]) / h[k]);
    }
    if (n == 2)
        return [delta[0], delta[0]];

    var d = [pchipEndSlope(h[0], h[1], delta[0], delta[1])];
    for (var k = 1; k < n - 1; k += 1)
    {
        if (delta[k - 1] * delta[k] <= 0)
        {
            d = append(d, 0);
        }
        else
        {
            const w1 = 2 * h[k] + h[k - 1];
            const w2 = h[k] + 2 * h[k - 1];
            d = append(d, (w1 + w2) / (w1 / delta[k - 1] + w2 / delta[k]));
        }
    }
    d = append(d, pchipEndSlope(h[n - 2], h[n - 3], delta[n - 2], delta[n - 3]));
    return d;
}

/** One-sided three-point end slope, limited so it does not overshoot (h0, d0 are the end segment). */
function pchipEndSlope(h0 is number, h1 is number, d0 is number, d1 is number) returns number
{
    var slope = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
    if (slope * d0 <= 0)
        slope = 0;
    else if (d0 * d1 <= 0 && abs(slope) > 3 * abs(d0))
        slope = 3 * d0;
    return slope;
}

/**
 * Unit-chord surface points, leading edge to trailing edge, cosine spaced.
 * Returns { upper : [[x, y]...], lower : [[x, y]...] } with x, y as plain numbers (fraction of chord).
 */
function airfoilSurfaces(spec is map, n is number, closedTE is boolean) returns map
{
    // A section between two stations (smooth taper) is a blend of the two airfoils, point by point.
    if (spec.blendA != undefined)
    {
        const a = airfoilSurfaces(spec.blendA, n, closedTE);
        const b = airfoilSurfaces(spec.blendB, n, closedTE);
        var blendedUpper = [];
        var blendedLower = [];
        for (var i = 0; i < n; i += 1)
        {
            blendedUpper = append(blendedUpper, [(1 - spec.blendT) * a.upper[i][0] + spec.blendT * b.upper[i][0],
                        (1 - spec.blendT) * a.upper[i][1] + spec.blendT * b.upper[i][1]]);
            blendedLower = append(blendedLower, [(1 - spec.blendT) * a.lower[i][0] + spec.blendT * b.lower[i][0],
                        (1 - spec.blendT) * a.lower[i][1] + spec.blendT * b.lower[i][1]]);
        }
        return { "upper" : blendedUpper, "lower" : blendedLower };
    }
    if (spec.coords != undefined)
        return coordinateSurfaces(spec.coords, n, closedTE);

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
        else if (spec.reflex)
        {
            // Reflexed: the camber line turns back up towards the trailing edge (k21 = k2 / k1).
            const r = REFLEX_R[spec.posIndex - 1];
            const k1 = REFLEX_K1[spec.posIndex - 1] * spec.designCl / 0.3;
            const k21 = REFLEX_K21[spec.posIndex - 1];
            const tail = k21 * (1 - r) * (1 - r) * (1 - r) + r * r * r;
            if (x < r)
            {
                yc = k1 / 6 * ((x - r) * (x - r) * (x - r) - tail * x + r * r * r);
                dyc = k1 / 6 * (3 * (x - r) * (x - r) - tail);
            }
            else
            {
                yc = k1 / 6 * (k21 * (x - r) * (x - r) * (x - r) - tail * x + r * r * r);
                dyc = k1 / 6 * (3 * k21 * (x - r) * (x - r) - tail);
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

/**
 * Reads pasted airfoil coordinates. A pasted file may have lost its line breaks, so the name and the numbers run
 * together: the coordinates are the numbers with a decimal point, and leading ones outside +-1.5 (a "12.5%" in the
 * name) are skipped. Two whole-number counts matching the number of values after them start a Lednicer file (upper
 * then lower surface, each leading edge to trailing edge); otherwise it is a Selig file (one loop, trailing edge over
 * the upper surface to the leading edge and back along the lower surface).
 * Returns { ok, error, name, upper, lower, closeRamp }: each surface leading edge to trailing edge as [x, y] with the
 * leading edge (smallest x) at the origin, unit chord, and x strictly increasing; closeRamp as in needsCloseRamp().
 */
function parseCoordinates(text is string) returns map
{
    // Number-like runs that contain a decimal point.
    var values = [];
    var starts = [];
    const n = length(text);
    var i = 0;
    while (i < n)
    {
        if (indexOf("0123456789.-+eE", substring(text, i, i + 1)) < 0)
        {
            i += 1;
            continue;
        }
        var j = i;
        while (j < n && indexOf("0123456789.-+eE", substring(text, j, j + 1)) >= 0)
            j += 1;
        var token = substring(text, i, j);
        if (indexOf(token, ".") >= 0)
        {
            // Some files write ".5" or "-.5".
            if (indexOf(token, ".") == 0)
                token = "0" ~ token;
            else if (indexOf(token, "-.") == 0)
                token = "-0" ~ substring(token, 1, length(token));
            const value = try silent(stringToNumber(token));
            if (value != undefined)
            {
                values = append(values, value);
                starts = append(starts, i);
            }
        }
        i = j;
    }

    var k = 0;
    var lednicer = false;
    while (k < size(values) && abs(values[k]) > 1.5)
    {
        if (k + 1 < size(values) && values[k + 1] > 1.5 && values[k] == round(values[k]) && values[k + 1] == round(values[k + 1])
            && size(values) - k - 2 == 2 * (values[k] + values[k + 1]))
        {
            lednicer = true;
            break;
        }
        k += 1;
    }

    var upper = [];
    var lower = [];
    var nameEnd = 0;
    if (lednicer)
    {
        nameEnd = starts[k];
        const upperCount = values[k];
        for (var m = k + 2; m + 1 < size(values); m += 2)
        {
            if ((m - k - 2) / 2 < upperCount)
                upper = append(upper, [values[m], values[m + 1]]);
            else
                lower = append(lower, [values[m], values[m + 1]]);
        }
    }
    else
    {
        if ((size(values) - k) % 2 == 1)
            k += 1;
        if (size(values) - k < 12)
            return { "ok" : false, "error" : "No airfoil coordinates found (paste the .dat file: x y pairs with decimal points, at least 6 points)." };
        nameEnd = starts[k];
        var points = [];
        for (var m = k; m + 1 < size(values); m += 2)
            points = append(points, [values[m], values[m + 1]]);
        var nose = 0;
        for (var m = 1; m < size(points); m += 1)
        {
            if (points[m][0] < points[nose][0])
                nose = m;
        }
        for (var m = nose; m >= 0; m -= 1)
            upper = append(upper, points[m]);
        for (var m = nose; m < size(points); m += 1)
            lower = append(lower, points[m]);
    }

    // Leading edge (smallest x) to the origin, unit chord.
    var lead = upper[0];
    var xMax = upper[0][0];
    for (var p in concatenateArrays([upper, lower]))
    {
        if (p[0] < lead[0])
            lead = p;
        xMax = max(xMax, p[0]);
    }
    const chord = xMax - lead[0];
    if (chord < 1e-9)
        return { "ok" : false, "error" : "The pasted coordinates have no chord length." };
    upper = normaliseSide(upper, lead, chord);
    lower = normaliseSide(lower, lead, chord);
    if (size(upper) < 3 || size(lower) < 3)
        return { "ok" : false, "error" : "The pasted coordinates need at least 3 points on each surface." };
    if (meanY(upper) < meanY(lower))
    {
        const swap = upper;
        upper = lower;
        lower = swap;
    }

    // Name: the text before the coordinates, without surrounding spaces.
    var a = 0;
    var b = nameEnd;
    while (a < b && indexOf(" \t\r\n", substring(text, a, a + 1)) >= 0)
        a += 1;
    while (b > a && indexOf(" \t\r\n", substring(text, b - 1, b)) >= 0)
        b -= 1;
    return { "ok" : true, "name" : substring(text, a, b), "upper" : upper, "lower" : lower,
            "closeRamp" : needsCloseRamp(upper, lower) };
}

/**
 * True when closing the trailing edge with a linear pinch (gap * x) would make the section cross itself, checked on a
 * fine grid: a blunt edge whose surfaces spread apart towards the end (e.g. sc2110).
 */
function needsCloseRamp(upper is array, lower is array) returns boolean
{
    var xs = [];
    for (var i = 0; i < CLOSE_CHECK_POINTS; i += 1)
        xs = append(xs, 0.5 * (1 - cos(PI * radian * (i / (CLOSE_CHECK_POINTS - 1)))));
    const yUpper = resampleSide(upper, xs);
    const yLower = resampleSide(lower, xs);
    const last = CLOSE_CHECK_POINTS - 1;
    const gap = yUpper[last] - yLower[last];
    for (var i = 1; i < last; i += 1)
    {
        if (yUpper[i] - yLower[i] - gap * xs[i] <= 0)
            return true;
    }
    return false;
}

/** Scales one surface to unit chord with the leading edge at the origin, keeping only points with increasing x. */
function normaliseSide(side is array, lead is array, chord is number) returns array
{
    var out = [];
    for (var p in side)
    {
        const q = [(p[0] - lead[0]) / chord, (p[1] - lead[1]) / chord];
        if (size(out) == 0 || q[0] > out[size(out) - 1][0] + 1e-9)
            out = append(out, q);
    }
    return out;
}

function meanY(side is array) returns number
{
    var total = 0;
    for (var p in side)
        total += p[1];
    return total / size(side);
}

/**
 * Pasted coordinates resampled at the same cosine-spaced x as the NACA sections (so sections still blend point by
 * point), with a shape-preserving cubic through the points of each surface. A closed trailing edge pinches the two
 * surfaces together in proportion to x, or, where that would cross (closeRamp), tapers the thickness to zero over the
 * last part of the chord about the mean line; an open one is widened in proportion to x to at least MIN_OPEN_GAP.
 */
function coordinateSurfaces(coords is map, n is number, closedTE is boolean) returns map
{
    var xs = [];
    for (var i = 0; i < n; i += 1)
        xs = append(xs, 0.5 * (1 - cos(PI * radian * (i / (n - 1)))));
    const yUpper = resampleSide(coords.upper, xs);
    const yLower = resampleSide(coords.lower, xs);
    const gap = yUpper[n - 1] - yLower[n - 1];
    var shift = 0;
    if (closedTE)
        shift = -gap / 2;
    else if (gap < MIN_OPEN_GAP)
        shift = (MIN_OPEN_GAP - gap) / 2;

    var upper = [];
    var lower = [];
    for (var i = 0; i < n; i += 1)
    {
        if (closedTE && coords.closeRamp)
        {
            const s = xs[i] > CLOSE_RAMP_START ? ((xs[i] - CLOSE_RAMP_START) / (1 - CLOSE_RAMP_START)) ^ 2 : 0;
            const mid = (yUpper[i] + yLower[i]) / 2;
            const half = (yUpper[i] - yLower[i]) * (1 - s) / 2;
            upper = append(upper, [xs[i], mid + half]);
            lower = append(lower, [xs[i], mid - half]);
            continue;
        }
        upper = append(upper, [xs[i], yUpper[i] + shift * xs[i]]);
        lower = append(lower, [xs[i], yLower[i] - shift * xs[i]]);
    }
    lower[0] = upper[0];
    if (closedTE)
        lower[n - 1] = upper[n - 1];
    return { "upper" : upper, "lower" : lower };
}

/** y of one surface at each x of xs (increasing), from a shape-preserving cubic through its points. */
function resampleSide(side is array, xs is array) returns array
{
    var x = [];
    var y = [];
    for (var p in side)
    {
        x = append(x, p[0]);
        y = append(y, p[1]);
    }
    const slopes = pchipSlopes(x, y);
    var out = [];
    var k = 0;
    for (var xi in xs)
    {
        while (k < size(x) - 2 && xi > x[k + 1])
            k += 1;
        const h = x[k + 1] - x[k];
        out = append(out, hermite(y[k], y[k + 1], slopes[k], slopes[k + 1], h, (xi - x[k]) / h));
    }
    return out;
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
