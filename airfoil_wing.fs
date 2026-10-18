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
 * Math: NACA 4-digit and 4-digit modified (Abbott & von Doenhoff), NACA 5-digit standard (2x0) and reflexed (2x1)
 * camber lines, NACA 16-series, and NACA 6- and 6A-series (thickness by conformal mapping from the epsilon / psi
 * functions, as in Carmichael's public domain naca456 program; a= and modified a=0.8 mean lines).
 * A station can instead use pasted coordinates (Selig or Lednicer .dat, e.g. from the UIUC database).
 * Reference implementations / UIUC comparison: tools/naca_reference.py, tools/dat_reference.py
 */

export enum AirfoilFamily
{
    annotation { "Name" : "NACA 4-digit" }
    NACA4,
    annotation { "Name" : "NACA 5-digit" }
    NACA5,
    annotation { "Name" : "NACA 6, 6A or 16-series" }
    NACA6
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
// Pasted coordinates: below this max camber (fraction of chord) the airfoil counts as symmetric and cannot be scaled.
const MIN_SCALABLE_CAMBER = 1e-4;

// Smallest scale a cap section is allowed to reach (a loft cannot end on a zero-size section).
const MIN_CAP_SCALE = 0.03;

// NACA 5-digit standard camber-line constants for 210, 220, 230, 240, 250 (design CL = 0.3).
const FIVE_DIGIT_R = [0.0580, 0.1260, 0.2025, 0.2900, 0.3910];
const FIVE_DIGIT_K1 = [361.4, 51.64, 15.957, 6.643, 3.230];
// NACA 6- and 6A-series thickness families, in the order of the tables below.
const SIX_FAMILIES = ["63", "64", "65", "66", "67", "63A", "64A", "65A"];
// The epsilon and psi functions of the conformal mapping that defines each family, and their slopes, at 21 knots
// (from naca456's epspsi.f90, public domain; cubic Hermite between the knots reproduces naca456's 201-point tables to
// within 0.0001 chord). Multiplied by a polynomial in t/c (SIX_SCALE, naca456's ScaleFactor) they give the
// thickness distribution for that t/c.
// epsilon(phi) at phi = k * pi / 20, k = 0..20
const SIX_EPS = [
        [0.00000, 0.01367, 0.01661, 0.01884, 0.02268, 0.02801, 0.03465, 0.04237, 0.05098, 0.06011, 0.06832, 0.07464, 0.07848, 0.07938, 0.07685, 0.07070, 0.06103, 0.04834, 0.03333, 0.01698, 0.00000],   // 63
        [0.00000, 0.02035, 0.02812, 0.03090, 0.03538, 0.04250, 0.05208, 0.06395, 0.07808, 0.09462, 0.11125, 0.12386, 0.13182, 0.13459, 0.13166, 0.12261, 0.10735, 0.08645, 0.06052, 0.03113, 0.00000],   // 64
        [0.00000, 0.01515, 0.01943, 0.01715, 0.01821, 0.02211, 0.02772, 0.03510, 0.04404, 0.05467, 0.06653, 0.07771, 0.08614, 0.09017, 0.08982, 0.08427, 0.07368, 0.05228, 0.02939, 0.01302, 0.00000],   // 65
        [0.00000, 0.01340, 0.01940, 0.01450, 0.01350, 0.01530, 0.01890, 0.02390, 0.03020, 0.03770, 0.04670, 0.05730, 0.07020, 0.08180, 0.08850, 0.08940, 0.08390, 0.07190, 0.05290, 0.02810, 0.00000],   // 66
        [0.00000, 0.01591, 0.02259, 0.02126, 0.01979, 0.01997, 0.02135, 0.02386, 0.02752, 0.03232, 0.03849, 0.04622, 0.05610, 0.06814, 0.08009, 0.08659, 0.08602, 0.06908, 0.04301, 0.02049, 0.00000],   // 67
        [0.00000, 0.01405, 0.01751, 0.01813, 0.02134, 0.02569, 0.03125, 0.03759, 0.04496, 0.05243, 0.05921, 0.06422, 0.06714, 0.06720, 0.06441, 0.05896, 0.05152, 0.03959, 0.02589, 0.01279, 0.00000],   // 63A
        [0.00000, 0.01507, 0.01899, 0.01846, 0.02010, 0.02306, 0.02749, 0.03295, 0.03984, 0.04772, 0.05554, 0.06147, 0.06508, 0.06586, 0.06376, 0.05881, 0.05168, 0.03997, 0.02630, 0.01304, 0.00000],   // 64A
        [0.00000, 0.01413, 0.01804, 0.01642, 0.01545, 0.01708, 0.02075, 0.02522, 0.03122, 0.03806, 0.04610, 0.05356, 0.05931, 0.06227, 0.06254, 0.05948, 0.05343, 0.04234, 0.02847, 0.01430, 0.00000]   // 65A
    ];
// psi(phi)
const SIX_PSI = [
        [0.15066, 0.14497, 0.13753, 0.13576, 0.13547, 0.13511, 0.13414, 0.13194, 0.12808, 0.12160, 0.11218, 0.10029, 0.08657, 0.07153, 0.05591, 0.04065, 0.02682, 0.01527, 0.00680, 0.00169, 0.00000],   // 63
        [0.25269, 0.24730, 0.23325, 0.22818, 0.22730, 0.22751, 0.22736, 0.22584, 0.22203, 0.21449, 0.19966, 0.17950, 0.15596, 0.13024, 0.10338, 0.07671, 0.05193, 0.03051, 0.01385, 0.00353, 0.00000],   // 64
        [0.17464, 0.16808, 0.15523, 0.15235, 0.15350, 0.15536, 0.15678, 0.15731, 0.15653, 0.15393, 0.14779, 0.13680, 0.12154, 0.10353, 0.08401, 0.06385, 0.04422, 0.02590, 0.01154, 0.00289, 0.00000],   // 65
        [0.16457, 0.16037, 0.14604, 0.14236, 0.14366, 0.14601, 0.14790, 0.14917, 0.14961, 0.14908, 0.14725, 0.14360, 0.13615, 0.12108, 0.10180, 0.07989, 0.05733, 0.03566, 0.01705, 0.00439, 0.00000],   // 66
        [0.18028, 0.17403, 0.16085, 0.15278, 0.15048, 0.14982, 0.15002, 0.15046, 0.15095, 0.15110, 0.15076, 0.14945, 0.14628, 0.13974, 0.12560, 0.10386, 0.07840, 0.04935, 0.02327, 0.00615, 0.00000],   // 67
        [0.15517, 0.14634, 0.13753, 0.13521, 0.13468, 0.13420, 0.13322, 0.13131, 0.12792, 0.12228, 0.11416, 0.10395, 0.09221, 0.07941, 0.06650, 0.05441, 0.04315, 0.03186, 0.02464, 0.02248, 0.02243],   // 63A
        [0.15365, 0.14473, 0.13407, 0.13038, 0.12949, 0.12942, 0.12925, 0.12841, 0.12652, 0.12281, 0.11554, 0.10568, 0.09416, 0.08147, 0.06848, 0.05604, 0.04450, 0.03288, 0.02524, 0.02266, 0.02243],   // 64A
        [0.14761, 0.13785, 0.12892, 0.12219, 0.12279, 0.12387, 0.12472, 0.12502, 0.12448, 0.12266, 0.11859, 0.11122, 0.10107, 0.08921, 0.07622, 0.06275, 0.04985, 0.03695, 0.02763, 0.02341, 0.02242]   // 65A
    ];
// d epsilon / d phi
const SIX_DEPS = [
        [0.10430, 0.05153, 0.00681, 0.01998, 0.02920, 0.03837, 0.04595, 0.05211, 0.05751, 0.05667, 0.04699, 0.03286, 0.01562, -0.00479, -0.02757, -0.05069, -0.07181, -0.08912, -0.10075, -0.10680, -0.10860],   // 63
        [0.14800, 0.09163, 0.02253, 0.01975, 0.03711, 0.05334, 0.06847, 0.08243, 0.09837, 0.10984, 0.09576, 0.06556, 0.03485, -0.00004, -0.03776, -0.07771, -0.11568, -0.15018, -0.17797, -0.19447, -0.20000],   // 64
        [0.10460, 0.06791, -0.00515, -0.00912, 0.01835, 0.03047, 0.04140, 0.05202, 0.06220, 0.07293, 0.07561, 0.06468, 0.04022, 0.01243, -0.01965, -0.04651, -0.10257, -0.15418, -0.12661, -0.08921, -0.07785],   // 65
        [0.09184, 0.07101, -0.00538, -0.02849, 0.00665, 0.01719, 0.02775, 0.03608, 0.04375, 0.05249, 0.06143, 0.07614, 0.08282, 0.06048, 0.02475, -0.01433, -0.05527, -0.09880, -0.14157, -0.17144, -0.18300],   // 66
        [0.10740, 0.07861, 0.00960, -0.01482, -0.00378, 0.00531, 0.01233, 0.01968, 0.02679, 0.03473, 0.04380, 0.05554, 0.07036, 0.08166, 0.06117, 0.02604, -0.05207, -0.15219, -0.16061, -0.13335, -0.12740],   // 67
        [0.09522, 0.05936, 0.00176, 0.01151, 0.02535, 0.03149, 0.03797, 0.04390, 0.04828, 0.04641, 0.03824, 0.02582, 0.00995, -0.00869, -0.02732, -0.03940, -0.06125, -0.08553, -0.08613, -0.08180, -0.08114],   // 63A
        [0.10100, 0.06557, -0.00058, 0.00152, 0.01572, 0.02346, 0.03158, 0.03912, 0.04780, 0.05175, 0.04503, 0.03072, 0.01429, -0.00402, -0.02342, -0.03693, -0.05956, -0.08464, -0.08661, -0.08324, -0.08273],   // 64A
        [0.09403, 0.06306, -0.00175, -0.01234, 0.00163, 0.01841, 0.02595, 0.03326, 0.04099, 0.04800, 0.05120, 0.04325, 0.02811, 0.01066, -0.00906, -0.02769, -0.05416, -0.08301, -0.09051, -0.09048, -0.09132]   // 65A
    ];
// d psi / d phi
const SIX_DPSI = [
        [0.00000, -0.05528, -0.02966, -0.00199, -0.00173, -0.00351, -0.00963, -0.01852, -0.03205, -0.05077, -0.06853, -0.08210, -0.09217, -0.09850, -0.09941, -0.09362, -0.08169, -0.06433, -0.04335, -0.02163, 0.00000],   // 63
        [0.00000, -0.07538, -0.06976, -0.01074, -0.00091, 0.00159, -0.00432, -0.01621, -0.03263, -0.07005, -0.11441, -0.14057, -0.15792, -0.16855, -0.17207, -0.16552, -0.14847, -0.12295, -0.08699, -0.04438, 0.00000],   // 64
        [0.00000, -0.07862, -0.05623, 0.00311, 0.01077, 0.01132, 0.00659, -0.00045, -0.00957, -0.02581, -0.05410, -0.08493, -0.10751, -0.12044, -0.12748, -0.12746, -0.12261, -0.10688, -0.07399, -0.03660, 0.00000],   // 65
        [0.00000, -0.07127, -0.06881, 0.00254, 0.01318, 0.01443, 0.01006, 0.00567, -0.00008, -0.00709, -0.01666, -0.03095, -0.07154, -0.11299, -0.13253, -0.14356, -0.14253, -0.13104, -0.10260, -0.05576, 0.00000],   // 66
        [0.00000, -0.07383, -0.07578, -0.02892, -0.00661, -0.00118, 0.00255, 0.00322, 0.00235, -0.00039, -0.00442, -0.01344, -0.02739, -0.06246, -0.11774, -0.15184, -0.17636, -0.18379, -0.14138, -0.07576, 0.00000],   // 67
        [0.00000, -0.07598, -0.03297, -0.00471, -0.00261, -0.00413, -0.00874, -0.01609, -0.02811, -0.04394, -0.05895, -0.07036, -0.07884, -0.08295, -0.08039, -0.07297, -0.07370, -0.06291, -0.02818, -0.00351, 0.00000],   // 63A
        [0.00000, -0.08211, -0.04550, -0.00997, -0.00211, 0.00007, -0.00276, -0.00833, -0.01605, -0.03440, -0.05603, -0.06863, -0.07777, -0.08268, -0.08196, -0.07514, -0.07546, -0.06533, -0.03105, -0.00565, 0.00000],   // 64A
        [0.00000, -0.07655, -0.05077, -0.01947, 0.01158, 0.00524, 0.00431, -0.00053, -0.00678, -0.01742, -0.03604, -0.05689, -0.07099, -0.07951, -0.08556, -0.08361, -0.08364, -0.07457, -0.04244, -0.01427, 0.00000]   // 65A
    ];
const SIX_SCALE = [
        [0.0, 8.1827699, 1.3776209, -0.092851684, 7.5942563],
        [0.0, 4.6535511, 1.038063, -1.5041794, 4.7882784],
        [0.0, 6.5718716, 0.49376292, 0.7319794, 1.9491474],
        [0.0, 6.7581414, 0.19253769, 0.81282621, 0.85202897],
        [0.0, 6.627289, 0.098965859, 0.96759774, 0.90537584],
        [0.0, 8.1845925, 1.0492569, 1.31150930, 4.4515579],
        [0.0, 8.2125018, 0.76855961, 1.4922345, 3.6130133],
        [0.0, 8.2514822, 0.46569361, 1.50113018, 2.0908904]
    ];
// Points along the conformal mapping (phi = 0..pi) for a 6-series thickness distribution.
const SIX_POINTS = 201;
// Modified 4-digit: trailing-edge half angle d1 as a polynomial in the max-thickness position (naca456 CalculateD1).
const FOUR_MOD_D1 = [3.48e-5, 2.3076628, -10.127712, 19.961478, -10.420597];

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

                annotation { "Name" : "Scale thickness (start from the file's thickness)" }
                station.scaleThickness is boolean;

                if (station.scaleThickness)
                {
                    annotation { "Name" : "Thickness (% chord, camber line kept)" }
                    isReal(station.coordThickness, THICKNESS_BOUNDS);
                }

                annotation { "Name" : "Scale camber (start from the file's camber)" }
                station.scaleCamber is boolean;

                if (station.scaleCamber)
                {
                    annotation { "Name" : "Max camber (% chord, position and thickness kept)" }
                    isReal(station.coordCamber, CAMBER_BOUNDS);
                }
            }
            else
            {
                annotation { "Name" : "NACA designation (e.g. 2412, 23012, 0010-64, 16-212, 63-412, 64A010, 65-410 a=0.5)", "Default" : "2412" }
                station.designation is string;

                // Derived from the designation by the editing logic; only used to pick which fields to show.
                annotation { "Name" : "Series", "UIHint" : UIHint.ALWAYS_HIDDEN }
                station.family is AirfoilFamily;

                annotation { "Name" : "Customize shape (start from the designation's values)" }
                station.custom is boolean;

                if (station.custom)
                {
                    // Each parameter may be declared only once in the precondition, so Design CL (5-digit, 6, 6A and
                    // 16-series) sits in one branch with the 5-digit extras nested inside it.
                    if (station.family == AirfoilFamily.NACA4)
                    {
                        annotation { "Name" : "Max camber (% chord)" }
                        isReal(station.camber, CAMBER_BOUNDS);

                        annotation { "Name" : "Camber position (% chord)" }
                        isReal(station.camberPos, CAMBER_POS_BOUNDS);
                    }
                    else
                    {
                        annotation { "Name" : "Design CL" }
                        isReal(station.designCl, DESIGN_CL_BOUNDS);

                        if (station.family == AirfoilFamily.NACA5)
                        {
                            annotation { "Name" : "Camber position index (1-5 = 5-25% chord)" }
                            isInteger(station.posIndex, CAMBER_INDEX_BOUNDS);

                            annotation { "Name" : "Reflexed camber line (no pitching moment, index 2-5)" }
                            station.reflex is boolean;
                        }
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
            "scaleThickness" : false, "coordThickness" : 12, "scaleCamber" : false, "coordCamber" : 2, "designation" : designation, "family" : AirfoilFamily.NACA4, "custom" : false, "chord" : chord, "origin" : 25, "aoa" : 0 * degree, "sweep" : 0 * millimeter, "vertical" : 0 * millimeter };
}

/**
 * Decodes a NACA designation into shape values (percentages for camber, camberPos, thickness).
 *   4-digit MPTT          : max camber M %, at P * 10 % chord, thickness TT %
 *   4-digit modified MPTT-IX: as 4-digit, leading-edge radius index I, max thickness at X * 10 % chord
 *   5-digit LPSTT         : design CL = 0.15 * L, camber position index P (P * 5 % chord), S = 0 standard or
 *                           1 reflexed, thickness TT %
 *   16-series 16-CTT      : 4-digit modified thickness with I = 4, X = 5; a=1.0 mean line, design CL C / 10
 *   6-series 6F-CTT       : family 63..67 (a low-drag range subscript such as 63(2)-, 63_2- or 632- is ignored),
 *                           design CL C / 10, thickness TT %; a=1.0 mean line unless "a=..." is given
 *   6A-series 6FACTT      : family 63A..65A, modified a=0.8 mean line unless "a=..." is given
 * Spaces, a leading "NACA" and brackets are ignored. Returns { ok, error, name, kind, family, camber, camberPos,
 * designCl, posIndex, reflex, thickness, sixFamily, meanA, leIndex, xMaxThickness } where kind is "4", "4M", "5",
 * "16", "6" or "6A", sixFamily indexes SIX_FAMILIES and meanA is the a= value (-1 for the modified a=0.8 line).
 */
function parseNaca(designation is string) returns map
{
    // Keep digits, dashes, and an "A" that follows a digit; read "a=0.5"; drop everything else.
    var core = "";
    var meanA = undefined;
    const n = length(designation);
    var i = 0;
    while (i < n)
    {
        const c = substring(designation, i, i + 1);
        if ((c == "a" || c == "A") && i + 1 < n && substring(designation, i + 1, i + 2) == "=")
        {
            var j = i + 2;
            while (j < n && indexOf("0123456789.", substring(designation, j, j + 1)) >= 0)
                j += 1;
            meanA = try silent(stringToNumber(substring(designation, i + 2, j)));
            if (meanA == undefined || meanA < 0 || meanA > 1)
                return { "ok" : false, "error" : "The mean line \"a=\" value must be between 0 and 1 (in \"" ~ designation ~ "\")." };
            i = j;
            continue;
        }
        if (indexOf("0123456789-", c) >= 0)
            core = core ~ c;
        else if ((c == "a" || c == "A") && length(core) > 0 && indexOf("0123456789", substring(core, length(core) - 1, length(core))) >= 0)
            core = core ~ "A";
        i += 1;
    }
    const bad = { "ok" : false, "error" : "Unknown NACA designation \"" ~ designation ~ "\" (use e.g. 2412, 23012, 0010-64, 16-212, 63-412, 64A010)." };
    const name = core ~ (meanA == undefined ? "" : " a=" ~ meanA);

    const dash = indexOf(core, "-");
    if (dash >= 0)
    {
        const left = substring(core, 0, dash);
        const right = substring(core, dash + 1, length(core));
        if (!allDigits(right) || length(left) < 2)
            return bad;
        if (length(left) == 4 && allDigits(left) && length(right) == 2)
        {
            // 4-digit modified: the 4-digit camber and thickness with a leading-edge index and max-thickness position.
            const four = parseFourFive(left, designation);
            if (!four.ok || meanA != undefined)
                return four.ok ? bad : four;
            const leIndex = digitAt(right, 0);
            const xMax = digitAt(right, 1);
            if (xMax < 2 || xMax > 6)
                return { "ok" : false, "error" : "4-digit modified: the max thickness position digit (after the dash) must be 2 to 6." };
            return mergeMaps(four, { "kind" : "4M", "name" : name, "leIndex" : leIndex, "xMaxThickness" : xMax / 10 });
        }
        if (length(right) != 3)
            return bad;
        const cl = digitAt(right, 0) / 10;
        const thickness = stringToNumber(substring(right, 1, 3));
        if (thickness < 1 || thickness > 40)
            return { "ok" : false, "error" : "Thickness in the designation must be 01 to 40 (got " ~ thickness ~ ")." };
        if (left == "16")
            return sixResult(name, "16", -1, cl, thickness, meanA == undefined ? 1 : meanA, 4, 0.5);
        const six = sixFamilyIndex(left);
        if (six < 0)
            return bad;
        const isA = six >= 5;
        return sixResult(name, isA ? "6A" : "6", six, cl, thickness, meanA != undefined ? meanA : (isA ? -1 : 1), 0, 0);
    }

    // 6A-series written without a dash: 64A010, 63A210.
    if (length(core) == 6 && indexOf(core, "A") == 2)
    {
        const six = sixFamilyIndex(substring(core, 0, 3));
        const right = substring(core, 3, 6);
        if (six < 0 || !allDigits(right))
            return bad;
        const thickness = stringToNumber(substring(right, 1, 3));
        if (thickness < 1 || thickness > 40)
            return { "ok" : false, "error" : "Thickness in the designation must be 01 to 40 (got " ~ thickness ~ ")." };
        return sixResult(name, "6A", six, digitAt(right, 0) / 10, thickness, meanA != undefined ? meanA : -1, 0, 0);
    }
    if (!allDigits(core))
        return bad;
    // 6-series written as six digits with the subscript: 632415 = 63(2)-415.
    if (length(core) == 6 && substring(core, 0, 1) == "6")
    {
        const six = sixFamilyIndex(substring(core, 0, 2));
        const thickness = stringToNumber(substring(core, 4, 6));
        if (six < 0 || thickness < 1 || thickness > 40)
            return bad;
        return sixResult(name, "6", six, digitAt(core, 3) / 10, thickness, meanA != undefined ? meanA : 1, 0, 0);
    }
    if (meanA != undefined)
        return { "ok" : false, "error" : "\"a=\" applies to 6-, 6A- and 16-series designations only." };
    const four = parseFourFive(core, designation);
    return four.ok ? mergeMaps(four, { "name" : name }) : four;
}

/** The 4- and 5-digit part of parseNaca (digits only). */
function parseFourFive(d is string, designation is string) returns map
{
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
                "ok" : true, "kind" : "4", "family" : AirfoilFamily.NACA4,
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
            "ok" : true, "kind" : "5", "family" : AirfoilFamily.NACA5,
            "camber" : 2, "camberPos" : 40,
            "designCl" : 0.15 * liftDigit, "posIndex" : posIndex, "reflex" : reflex == 1, "thickness" : thickness
        };
}

/** parseNaca result for the 6-, 6A- and 16-series. */
function sixResult(name is string, kind is string, sixFamily is number, designCl is number, thickness is number,
    meanA is number, leIndex is number, xMaxThickness is number) returns map
{
    return { "ok" : true, "name" : name, "kind" : kind, "family" : AirfoilFamily.NACA6, "camber" : 0, "camberPos" : 40,
            "designCl" : designCl, "posIndex" : 3, "reflex" : false, "thickness" : thickness, "sixFamily" : sixFamily,
            "meanA" : meanA, "leIndex" : leIndex, "xMaxThickness" : xMaxThickness };
}

/** Index into SIX_FAMILIES of "63".."67" or "63A".."65A" at the start of text (a subscript after it is ignored), or -1. */
function sixFamilyIndex(text is string) returns number
{
    if (length(text) < 2)
        return -1;
    const isA = length(text) >= 3 && substring(text, 2, 3) == "A";
    const key = substring(text, 0, 2) ~ (isA ? "A" : "");
    for (var k = 0; k < size(SIX_FAMILIES); k += 1)
    {
        if (SIX_FAMILIES[k] == key)
            return k;
    }
    return -1;
}

function allDigits(text is string) returns boolean
{
    if (length(text) == 0)
        return false;
    for (var i = 0; i < length(text); i += 1)
    {
        if (indexOf("0123456789", substring(text, i, i + 1)) < 0)
            return false;
    }
    return true;
}

function digitAt(text is string, index is number) returns number
{
    return stringToNumber(substring(text, index, index + 1));
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
            if (!coords.ok)
                continue;
            // Fill in the file's own thickness / camber when scaling is switched on or different coordinates are pasted.
            var oldSt = undefined;
            if (oldDefinition.stations != undefined && i < size(oldDefinition.stations))
                oldSt = oldDefinition.stations[i];
            const changed = oldSt == undefined || oldSt.coordinates != st.coordinates;
            var notes = "";
            if (st.scaleThickness == true)
            {
                if (changed || oldSt.scaleThickness != true)
                    definition.stations[i].coordThickness = roundToPrecision(coords.thickness * 100, 2);
                notes = "" ~ roundToPrecision(definition.stations[i].coordThickness, 2) ~ " % thick";
            }
            if (st.scaleCamber == true)
            {
                if (changed || oldSt.scaleCamber != true)
                    definition.stations[i].coordCamber = roundToPrecision(abs(coords.camber) * 100, 2);
                notes = notes ~ (notes == "" ? "" : ", ") ~ roundToPrecision(definition.stations[i].coordCamber, 2) ~ " % camber";
            }
            if (notes != "")
                definition.stations[i].label = definition.stations[i].label ~ " (" ~ notes ~ ")";
            continue;
        }
        const parsed = parseNaca(st.designation);
        definition.stations[i].label = parsed.ok ? "NACA " ~ parsed.name : "NACA ?";
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
        else if (naca.family == AirfoilFamily.NACA6)
        {
            definition.stations[i].designCl = naca.designCl;
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
            var thicknessScale = 1;
            if (st.scaleThickness == true)
                thicknessScale = st.coordThickness / 100 / coords.thickness;
            var camberScale = 1;
            if (st.scaleCamber == true)
            {
                if (abs(coords.camber) < MIN_SCALABLE_CAMBER && st.coordCamber > 0)
                    throw regenError("Station at " ~ st.position ~ " %: the pasted airfoil has no camber to scale (it is symmetric).", ["stations"]);
                camberScale = abs(coords.camber) < MIN_SCALABLE_CAMBER ? 0 : st.coordCamber / 100 / abs(coords.camber);
            }
            userSpecs = append(userSpecs, mergeMaps(placement, { "family" : AirfoilFamily.NACA4, "coords" : coords,
                            "thicknessScale" : thicknessScale, "camberScale" : camberScale }));
            continue;
        }

        const naca = parseNaca(st.designation);
        if (!naca.ok)
            throw regenError("Station at " ~ st.position ~ " %: " ~ naca.error, ["stations"]);
        // The designation supplies the defaults; "Customize shape" replaces them with the edited values.
        const five = naca.family == AirfoilFamily.NACA5;
        const six = naca.family == AirfoilFamily.NACA6;

        if (five && st.custom && st.reflex == true && st.posIndex == 1)
            throw regenError("Station at " ~ st.position ~ " %: a reflexed camber line needs a camber position index of 2 to 5.", ["stations"]);

        const thickness = (st.custom ? st.thickness : naca.thickness) / 100;
        var spec = mergeMaps(placement, {
                    "family" : naca.family,
                    "camber" : (five || six) ? 0 : (st.custom ? st.camber : naca.camber) / 100,
                    "camberPos" : (five || six) ? 0 : (st.custom ? st.camberPos : naca.camberPos) / 100,
                    "designCl" : (five || six) ? (st.custom ? st.designCl : naca.designCl) : 0,
                    "posIndex" : five ? (st.custom ? st.posIndex : naca.posIndex) : 1,
                    "reflex" : five && (st.custom ? st.reflex == true : naca.reflex),
                    "thickness" : thickness,
                    "meanA" : six ? naca.meanA : 1
                });
        // Thickness forms other than the 4-digit one, worked out once per station.
        if (naca.kind == "4M" || naca.kind == "16")
            spec.fourModified = fourModifiedCoefficients(thickness, naca.leIndex, naca.xMaxThickness);
        if (naca.kind == "6" || naca.kind == "6A")
            spec.sixThickness = sixThickness(naca.sixFamily, thickness);
        userSpecs = append(userSpecs, spec);
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
        return coordinateSurfaces(spec.coords, n, closedTE, spec.thicknessScale == undefined ? 1 : spec.thicknessScale,
                spec.camberScale == undefined ? 1 : spec.camberScale);

    var xs = [];
    for (var i = 0; i < n; i += 1)
        xs = append(xs, 0.5 * (1 - cos(PI * radian * (i / (n - 1)))));

    // Half thickness. The 4-digit formula has its own closed and open forms; the 6-series and 4-digit modified
    // forms are closed (pinched in proportion to x) or opened to at least MIN_OPEN_GAP like pasted coordinates.
    var yt = [];
    if (spec.sixThickness != undefined)
        yt = resampleSide(spec.sixThickness, xs);
    else
    {
        for (var x in xs)
            yt = append(yt, spec.fourModified != undefined ? fourModifiedThickness(x, spec.fourModified) : thicknessDistribution(x, spec.thickness, closedTE));
    }
    if (spec.sixThickness != undefined || spec.fourModified != undefined)
    {
        const edge = yt[n - 1];
        var shift = 0;
        if (closedTE)
            shift = -edge;
        else if (2 * edge < MIN_OPEN_GAP)
            shift = MIN_OPEN_GAP / 2 - edge;
        for (var i = 0; i < n; i += 1)
            yt[i] = yt[i] + shift * xs[i];
    }

    var upper = [];
    var lower = [];
    for (var i = 0; i < n; i += 1)
    {
        const x = xs[i];
        const camber = meanLine(spec, x);
        const theta = atan(camber[1]);
        upper = append(upper, [x - yt[i] * sin(theta), camber[0] + yt[i] * cos(theta)]);
        lower = append(lower, [x + yt[i] * sin(theta), camber[0] - yt[i] * cos(theta)]);
    }

    // The leading edge is shared and, for a closed trailing edge, so is the trailing edge.
    lower[0] = upper[0];
    if (closedTE)
        lower[n - 1] = upper[n - 1];
    return { "upper" : upper, "lower" : lower };
}

/** Mean line height and slope [yc, dyc/dx] at x (fraction of chord) for a NACA section spec. */
function meanLine(spec is map, x is number) returns array
{
    if (spec.family == AirfoilFamily.NACA6)
        return spec.meanA < 0 ? modifiedSixMeanLine(spec.designCl, x) : sixMeanLine(spec.meanA, spec.designCl, x);
    if (spec.family == AirfoilFamily.NACA4)
    {
        const m = spec.camber;
        const p = spec.camberPos;
        if (m < 1e-9 || p < 1e-9)
            return [0, 0];
        if (x < p)
            return [m / (p * p) * (2 * p * x - x * x), 2 * m / (p * p) * (p - x)];
        return [m / ((1 - p) * (1 - p)) * ((1 - 2 * p) + 2 * p * x - x * x), 2 * m / ((1 - p) * (1 - p)) * (p - x)];
    }
    if (spec.reflex)
    {
        // Reflexed: the camber line turns back up towards the trailing edge (k21 = k2 / k1).
        const r = REFLEX_R[spec.posIndex - 1];
        const k1 = REFLEX_K1[spec.posIndex - 1] * spec.designCl / 0.3;
        const k21 = REFLEX_K21[spec.posIndex - 1];
        const tail = k21 * (1 - r) * (1 - r) * (1 - r) + r * r * r;
        if (x < r)
            return [k1 / 6 * ((x - r) * (x - r) * (x - r) - tail * x + r * r * r), k1 / 6 * (3 * (x - r) * (x - r) - tail)];
        return [k1 / 6 * (k21 * (x - r) * (x - r) * (x - r) - tail * x + r * r * r), k1 / 6 * (3 * k21 * (x - r) * (x - r) - tail)];
    }
    const r = FIVE_DIGIT_R[spec.posIndex - 1];
    const k1 = FIVE_DIGIT_K1[spec.posIndex - 1] * spec.designCl / 0.3;
    if (x < r)
        return [k1 / 6 * (x * x * x - 3 * r * x * x + r * r * (3 - r) * x), k1 / 6 * (3 * x * x - 6 * r * x + r * r * (3 - r))];
    return [k1 * r * r * r / 6 * (1 - x), -k1 * r * r * r / 6];
}

/**
 * NACA 6-series mean line with uniform loading back to x = a (Abbott & von Doenhoff eq. 4-26, 4-27, as in naca456
 * MeanLine6), scaled to the design lift coefficient. Height and slope are taken as 0 at the ends (log singularities).
 */
function sixMeanLine(a is number, cl is number, x is number) returns array
{
    const omx = 1 - x;
    if (x < 1e-7 || omx < 1e-7)
        return [0, 0];
    if (1 - a < 1e-7)
        return [-(omx * log(omx) + x * log(x)) * cl / (4 * PI), (log(omx) - log(x)) * cl / (4 * PI)];
    const oma = 1 - a;
    var g = -0.25;
    var h = -0.5;
    if (a > 1e-7)
    {
        g = -(a * a * (0.5 * log(a) - 0.25) + 0.25) / oma;
        h = g + (0.5 * oma * oma * log(oma) - 0.25 * oma * oma) / oma;
    }
    const amx = a - x;
    var term1 = 0;
    var term1p = 0;
    if (abs(amx) > 1e-7)
    {
        term1 = amx * amx * (2 * log(abs(amx)) - 1);
        term1p = -amx * log(abs(amx));
    }
    const term2 = omx * omx * (1 - 2 * log(omx));
    const term2p = omx * log(omx);
    const scale = cl / (2 * PI * (a + 1));
    return [scale * (0.25 * (term1 + term2) / oma - x * log(x) + g - h * x), scale * ((term1p + term2p) / oma - 1 - log(x) - h)];
}

/**
 * The modified a=0.8 mean line of the 6A-series (NACA Report 903, as in naca456 MeanLine6M): the a=0.8 line scaled by
 * 0.97948, replaced aft of x = 0.86 by a straight line to the trailing edge.
 */
function modifiedSixMeanLine(cl is number, x is number) returns array
{
    const teSlope = -0.24521 * cl;
    if (x > 0.86)
        return [teSlope * (x - 1), teSlope];
    const base = sixMeanLine(0.8, cl, x);
    return [0.97948 * base[0], 0.97948 * base[1]];
}

/**
 * Half-thickness distribution of a NACA 6- or 6A-series family at thickness ratio tc, as [[x, y]...] from the leading
 * edge (x = 0) to the trailing edge (x = 1): the scaled epsilon and psi functions map a circle onto the profile
 * (z' = exp(psi + i (phi - eps)), Joukowski zeta = z' + 1 / z'), then the profile is normalised to unit chord.
 */
function sixThickness(family is number, tc is number) returns array
{
    const scale = polynomial(SIX_SCALE[family], tc);
    const knot = PI / 20;
    var re = [];
    var im = [];
    for (var i = 0; i < SIX_POINTS; i += 1)
    {
        const phi = PI * i / (SIX_POINTS - 1);
        const k = min(floor(phi / knot), 19);
        const t = phi / knot - k;
        const eps = scale * hermite(SIX_EPS[family][k], SIX_EPS[family][k + 1], SIX_DEPS[family][k], SIX_DEPS[family][k + 1], knot, t);
        const psi = scale * hermite(SIX_PSI[family][k], SIX_PSI[family][k + 1], SIX_DPSI[family][k], SIX_DPSI[family][k + 1], knot, t);
        const r = exp(psi);
        const angle = (phi - eps) * radian;
        re = append(re, (r + 1 / r) * cos(angle));
        im = append(im, (r - 1 / r) * sin(angle));
    }
    const span = re[0] - re[SIX_POINTS - 1];
    var out = [];
    for (var i = 0; i < SIX_POINTS; i += 1)
    {
        const q = [(re[0] - re[i]) / span, im[i] / span];
        if (size(out) == 0 || q[0] > out[size(out) - 1][0] + 1e-12)
            out = append(out, q);
    }
    return out;
}

/**
 * Coefficients of the NACA 4-digit modified thickness distribution (Abbott & von Doenhoff 6.4, as in naca456
 * Thickness4M) for thickness ratio tc, leading-edge radius index leIndex and max thickness at xMax.
 */
function fourModifiedCoefficients(tc is number, leIndex is number, xMax is number) returns map
{
    const m = xMax;
    const d1 = polynomial(FOUR_MOD_D1, m);
    const rle = 1.1019 / 36 * (tc * leIndex) * (tc * leIndex);
    const a0 = (0.2 / tc) * sqrt(2 * rle);
    const a3 = 0.1 / (m * m * m) + (d1 * (1 - m) - 0.294) / (m * (1 - m) * (1 - m)) - 0.375 * a0 / (m * m * sqrt(m));
    const a2 = -0.1 / (m * m) + 0.5 * a0 / (m * sqrt(m)) - 2 * a3 * m;
    const a1 = -0.5 * a0 / sqrt(m) - 2 * a2 * m - 3 * a3 * m * m;
    const d3 = (3 * d1 - 0.588 / (1 - m)) / (3 * (1 - m) * (1 - m));
    const d2 = -1.5 * (1 - m) * d3 - 0.5 * d1 / (1 - m);
    return { "tc" : tc, "xMax" : m, "a" : [a0, a1, a2, a3], "d" : [0.002, d1, d2, d3] };
}

/** NACA 4-digit modified half thickness at x for coefficients from fourModifiedCoefficients. */
function fourModifiedThickness(x is number, c is map) returns number
{
    if (x <= 0)
        return 0;
    if (x < c.xMax)
        return 5 * c.tc * (c.a[0] * sqrt(x) + x * (c.a[1] + x * (c.a[2] + x * c.a[3])));
    const u = 1 - x;
    return 5 * c.tc * (c.d[0] + u * (c.d[1] + u * (c.d[2] + u * c.d[3])));
}

/** c[0] + c[1] x + c[2] x^2 + ... */
function polynomial(c is array, x is number) returns number
{
    var f = 0;
    for (var k = size(c) - 1; k >= 0; k -= 1)
        f = f * x + c[k];
    return f;
}

/**
 * Reads pasted airfoil coordinates. A pasted file may have lost its line breaks, so the name and the numbers run
 * together: the coordinates are the numbers with a decimal point, and leading ones outside +-1.5 (a "12.5%" in the
 * name) are skipped. Two whole-number counts matching the number of values after them start a Lednicer file (upper
 * then lower surface, each leading edge to trailing edge); otherwise it is a Selig file (one loop, trailing edge over
 * the upper surface to the leading edge and back along the lower surface).
 * Returns { ok, error, name, upper, lower, closeRamp, thickness, camber }: each surface leading edge to trailing edge as
 * [x, y] with the leading edge (smallest x) at the origin, unit chord, and x strictly increasing; closeRamp as in
 * needsCloseRamp(); thickness the largest distance between the surfaces at the same x; camber the height of the camber
 * line (mid-point of the surfaces at each x) above the chord line (leading edge to trailing-edge mid-point) where it
 * is largest in size, with its sign (all fractions of chord).
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
    var xs = [];
    for (var m = 0; m < CLOSE_CHECK_POINTS; m += 1)
        xs = append(xs, 0.5 * (1 - cos(PI * radian * (m / (CLOSE_CHECK_POINTS - 1)))));
    const yUpper = resampleSide(upper, xs);
    const yLower = resampleSide(lower, xs);
    const last = CLOSE_CHECK_POINTS - 1;
    const teMid = (yUpper[last] + yLower[last]) / 2;
    var thickness = 0;
    var camber = 0;
    for (var m = 0; m < CLOSE_CHECK_POINTS; m += 1)
    {
        thickness = max(thickness, yUpper[m] - yLower[m]);
        const height = (yUpper[m] + yLower[m]) / 2 - teMid * xs[m];
        if (abs(height) > abs(camber))
            camber = height;
    }
    if (thickness < 1e-6)
        return { "ok" : false, "error" : "The pasted coordinates have no thickness." };
    return { "ok" : true, "name" : substring(text, a, b), "upper" : upper, "lower" : lower,
            "closeRamp" : needsCloseRamp(xs, yUpper, yLower), "thickness" : thickness, "camber" : camber };
}

/**
 * True when closing the trailing edge with a linear pinch (gap * x) would make the section cross itself, checked on the
 * fine grid xs (surfaces resampled there): a blunt edge whose surfaces spread apart towards the end (e.g. sc2110).
 * Scaling the thickness does not change the answer (both sides of the test scale alike).
 */
function needsCloseRamp(xs is array, yUpper is array, yLower is array) returns boolean
{
    const last = size(xs) - 1;
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
 * thicknessScale multiplies the distance between the surfaces about their mid-point at each x (the camber line), and
 * camberScale the height of that mid-point above the chord line (leading edge to trailing-edge mid-point), before the
 * trailing edge is closed or opened.
 */
function coordinateSurfaces(coords is map, n is number, closedTE is boolean, thicknessScale is number, camberScale is number) returns map
{
    var xs = [];
    for (var i = 0; i < n; i += 1)
        xs = append(xs, 0.5 * (1 - cos(PI * radian * (i / (n - 1)))));
    var yUpper = resampleSide(coords.upper, xs);
    var yLower = resampleSide(coords.lower, xs);
    const teMid = (yUpper[n - 1] + yLower[n - 1]) / 2;
    for (var i = 0; i < n; i += 1)
    {
        const chordLine = teMid * xs[i];
        const mid = chordLine + ((yUpper[i] + yLower[i]) / 2 - chordLine) * camberScale;
        const half = (yUpper[i] - yLower[i]) * thicknessScale / 2;
        yUpper[i] = mid + half;
        yLower[i] = mid - half;
    }
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
