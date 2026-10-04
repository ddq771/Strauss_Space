using UnityEngine;

/// <summary>
/// Fixed, non-editable real-vehicle configurations, selectable from the
/// assembly screen instead of building from scratch. Engine performance
/// comes from EnginePerformance's own published/estimated data; what lives
/// here is everything else needed to approximate the real vehicle: how many
/// of that engine, the body's size, propellant load, and drag.
///
/// Honesty about what this does and doesn't get right:
/// - Engine count, engine performance, and propellant mass/mixture ratio
///   are real published figures (or the same "estimated preset" data
///   EnginePerformance already used for Merlin 1D/Raptor 2).
/// - Each body is a real-size model of the vehicle (ArtSource/*/build_*.py,
///   built in Blender), split into its stages - see
///   Resources/RocketBodies/{key}_stages.json. Physics still treats the
///   vehicle as one tank-diameter body (Rocket.ConfigureShape sets the
///   collider and drag area from the dimensions below).
/// - The body model carries its own engines, so presets get no separate
///   engine parts: the body itself produces thrust - engineCount of
///   engineId's real performance, applied at the base of the body and
///   steered by gimballing (RocketAssemblyController.Rebuild).
/// - Every preset flies as one non-separating vehicle. The models carry
///   their stages (Stage_* objects in each body prefab) for a future
///   staging system, but nothing detaches yet - the real separate-then-
///   relight sequences are not simulated.
/// </summary>
public static class RocketPresets
{
    public struct Preset
    {
        public string name;
        public string engineId;
        public int engineCount;
        public float bodyDiameter, bodyHeight, noseHeight, engineHeight;
        public Color hullColor;
        public float dragCoefficient;
        public string fuelType;
        public float fuelCapacity, fuelDiameter;
        public float oxidizerCapacity, oxidizerDiameter;
        // Matches Assets/Resources/RocketBodies/{key}.prefab, built from the
        // procedural Blender models under ArtSource/*/build_*.py - see
        // Rocket.SetBodyModel. hullColor above is unused when this is set;
        // the imported model carries its own painted materials.
        public string bodyModelKey;
        // Optional different engine in the centre socket (the R-7 core's
        // RD-108 among RD-107 strap-ons). Performance only - see
        // EnginePerformance.Reference.
        public string coreEngineId;
        // Optional solid boosters lit at liftoff alongside the engines
        // (EnginePerformance.Solid), e.g. the Shuttle's two SRBs.
        public string solidBoosterId;
        public int solidBoosterCount;
        // Real non-propellant mass at liftoff (kg): everything except the
        // first stage's liquid propellant (the tanks below) and the solid
        // boosters (EnginePerformance.Solid carries their mass) - first-stage
        // structure and engines, fully fuelled upper stages, payload.
        public float dryMass;
        // The real first-stage flight program, run from liftoff:
        // mecoSeconds - main engine cutoff (end of the first stage burn); 0 =
        //   burn until the propellant runs out.
        // throttleProgram - (seconds, throttle limit) pairs, interpolated;
        //   caps the pilot's throttle (e.g. the Max Q throttle-down).
        // engineCutoffs - (seconds, socket) pairs: individual engines shut
        //   down early (Saturn V's centre F-1, Vostok's strap-ons).
        // Calibrated so the propellant lasts to the real cutoff time.
        // Aerodynamics (RocketFlightModel.SetAerodynamics): centre of
        // pressure as a fraction of height from the base, and normal-force
        // slope per radian of angle of attack (nose + body lift, plus fins).
        public float centerOfPressure, normalForceSlope;
        public float mecoSeconds;
        public float[] throttleProgram;
        public float[] engineCutoffs;
    }

    public static readonly Preset[] All =
    {
        new()
        {
            // 9 Merlin 1D in an octaweb (1 centre + 8 ring - the real
            // layout, and what RocketAssemblyController's ring packing
            // produces for a count of 9 with no special-casing needed).
            // Aero: no fins (grid fins stowed): centre of pressure well forward - unstable.
            centerOfPressure = .72f, normalForceSlope = 2.2f,
            name = "Falcon 9", bodyModelKey = "Falcon9", engineId = "Merlin1D", engineCount = 9,
            // 70 m overall, the real Block 5 height (was 87 m before the
            // to-scale body model came in).
            bodyDiameter = 3.7f, bodyHeight = 53f, noseHeight = 13f, engineHeight = 4f,
            hullColor = new Color(0.94f, 0.94f, 0.95f), dragCoefficient = 0.3f,
            fuelType = "RP-1", fuelCapacity = 152.5f, fuelDiameter = 3.7f,
            oxidizerCapacity = 251.9f, oxidizerDiameter = 3.7f,
            // 549 t at liftoff - 410.9 t first-stage propellant.
            dryMass = 138100f,
            // MECO at T+2:42, with the throttle down to 70% through Max Q
            // (T+52-85 s); leaves ~6 t residual.
            mecoSeconds = 162f,
            throttleProgram = new[] { 0f, 1f, 45f, 1f, 52f, .7f, 85f, .7f, 92f, 1f },
        },
        new()
        {
            // First stage only: Super Heavy's 33 Raptors (74.4 MN at sea
            // level) lift the whole stack - the Ship's 6 engines don't fire
            // until after separation. Propellant is still the full stack's
            // (no staging yet).
            // Aero: flaps fore and aft on the Ship, finless booster - mildly unstable.
            centerOfPressure = .62f, normalForceSlope = 2.6f,
            name = "Starship", bodyModelKey = "Starship", engineId = "Raptor2", engineCount = 33,
            // 121.3 m overall, the full Super Heavy + Ship stack (Flight 5).
            bodyDiameter = 9f, bodyHeight = 95.3f, noseHeight = 18f, engineHeight = 8f,
            hullColor = new Color(0.74f, 0.75f, 0.77f), dragCoefficient = 0.55f,
            // Super Heavy's 3,400 t of propellant (O/F 3.6).
            fuelType = "Methane", fuelCapacity = 1749f, fuelDiameter = 9f,
            oxidizerCapacity = 2332f, oxidizerDiameter = 9f,
            // ~5,000 t at liftoff - 3,400 t booster propellant (booster dry
            // mass + the fuelled Ship).
            dryMass = 1600000f,
            // Hot staging at T+2:39, throttled to 60% through Max Q; the
            // ~26 t left is the booster's boostback/landing reserve.
            mecoSeconds = 159f,
            throttleProgram = new[] { 0f, 1f, 50f, 1f, 58f, .6f, 85f, .6f, 92f, 1f },
        },
        new()
        {
            // 1 RD-108 core + 4 RD-107 boosters, all represented with the
            // same RD107 catalog entry (see EnginePerformance.Reference) -
            // the two are similar engines and this project only models one
            // of them. The boosters themselves are not modeled as separate
            // bodies; this is the core stage's diameter only.
            // Liftoff: 4 RD-107 strap-ons (821 kN each at sea level) around the
            // RD-108 core (745 kN) - 4.03 MN together.
            // Aero: flared strap-ons with fins: centre of pressure low - stable.
            centerOfPressure = .3f, normalForceSlope = 4f,
            name = "Vostok", bodyModelKey = "Vostok", engineId = "RD107", engineCount = 5, coreEngineId = "RD108",
            // 38.4 m overall, the real Vostok-K height (was 39 m).
            bodyDiameter = 2.99f, bodyHeight = 27.4f, noseHeight = 8f, engineHeight = 3f,
            hullColor = new Color(0.52f, 0.54f, 0.48f), dragCoefficient = 0.45f,
            // 249.9 t of core + strap-on propellant at the engines' O/F ~2.45,
            // so fuel and LOX run out together.
            fuelType = "RP-1", fuelCapacity = 89.4f, fuelDiameter = 2.99f,
            oxidizerCapacity = 155.6f, oxidizerDiameter = 2.99f,
            // 287 t at liftoff - 249.9 t core + strap-on propellant (stage
            // structure, fuelled Block E, Vostok spacecraft, shroud).
            dryMass = 37100f,
            // First stage: the four strap-ons shut down (and on the real
            // vehicle separate) at T+118 s; the core burns on alone to
            // depletion at ~T+5:15.
            engineCutoffs = new[] { 118f, 1f, 118f, 2f, 118f, 3f, 118f, 4f },
        },
        new()
        {
            // S-IC first stage: 5 F-1 engines, real diameter and propellant
            // load (upper stages/Apollo stack not modeled separately).
            // Aero: four large fins: centre of pressure low - stable.
            centerOfPressure = .33f, normalForceSlope = 3.5f,
            name = "Saturn V", bodyModelKey = "SaturnV", engineId = "F1", engineCount = 5,
            // 110.6 m overall, the real Apollo stack height.
            bodyDiameter = 10.1f, bodyHeight = 94.6f, noseHeight = 13f, engineHeight = 3f,
            hullColor = new Color(0.92f, 0.92f, 0.89f), dragCoefficient = 0.4f,
            // Apollo 11 S-IC load: 639 t RP-1 + 1,428 t LOX (O/F 2.24, close
            // to the F-1's 2.27 - an earlier version had the RP-1 volume in
            // litres read as tonnes, leaving 190 t of LOX unburnable).
            fuelType = "RP-1", fuelCapacity = 788.9f, fuelDiameter = 10.1f,
            oxidizerCapacity = 1251.5f, oxidizerDiameter = 10.1f,
            // Apollo 11: 2,938 t at liftoff - 2,067 t S-IC propellant (S-IC
            // structure, fuelled S-II and S-IVB, Apollo spacecraft).
            dryMass = 871000f,
            // Apollo 11 S-IC: centre engine cutoff T+135.2 s, outboard
            // engine cutoff T+161.6 s (~34 t residual).
            mecoSeconds = 161.6f,
            engineCutoffs = new[] { 135.2f, 0f },
        },
        new()
        {
            // 3 RS-25, External Tank diameter/height/color - the real
            // vehicle is a side-mounted orbiter and 2 SRBs around this
            // tank, not a stack with the engines at the bottom of it; see
            // the type doc comment.
            // Liftoff: 3 RS-25 (1.86 MN each at sea level) plus 2 SRBs
            // (12.5 MN each) - ~30.6 MN, about 70% from the boosters, which
            // burn out at ~2 min.
            // Aero: orbiter wings and tail add lift aft of centre - near neutral.
            centerOfPressure = .45f, normalForceSlope = 4f,
            name = "Space Shuttle", bodyModelKey = "SpaceShuttle", engineId = "RS25", engineCount = 3,
            solidBoosterId = "SRB", solidBoosterCount = 2,
            // 56.1 m overall, the real stack height.
            bodyDiameter = 8.4f, bodyHeight = 42.1f, noseHeight = 10f, engineHeight = 4f,
            hullColor = new Color(0.72f, 0.42f, 0.25f), dragCoefficient = 0.5f,
            fuelType = "LH2", fuelCapacity = 1499.9f, fuelDiameter = 8.4f,
            oxidizerCapacity = 551.6f, oxidizerDiameter = 8.4f,
            // 2,030 t at liftoff - 735.6 t External Tank propellant - 2 x 590 t
            // SRBs (orbiter + payload, empty External Tank).
            dryMass = 114400f,
            // First stage: the SRBs, burnout ~T+2:04 (their grain profile).
            // The RS-25s then run on to MECO at T+8:30: 104.5% rated power
            // (0.959 of the 109% this engine model is rated at), throttled
            // to 72% through Max Q (T+26-60 s).
            mecoSeconds = 510f,
            throttleProgram = new[] { 0f, .959f, 26f, .959f, 30f, .66f, 56f, .66f, 60f, .959f },
        },
    };
}
