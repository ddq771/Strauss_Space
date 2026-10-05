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
    /// <summary>
    /// A stage that takes over after the one below it is spent. Times are
    /// seconds after this stage's own ignition.
    /// </summary>
    /// <summary>
    /// Thermal protection of a stage for re-entry heating (ReentryHeating).
    /// Bare structure fails above bareLimitC; if the stage has a heat
    /// shield (shieldLimitC > 0), the part of the surface within
    /// shieldHalfAngle of shieldAxis (rocket-local) survives up to
    /// shieldLimitC. shieldAxis zero = find it from the body model's tile
    /// meshes (the Ship's tiles, the orbiter's black belly).
    /// </summary>
    [System.Serializable]
    public struct HeatProtection
    {
        public Vector3 shieldAxis;
        public float shieldHalfAngle, shieldLimitC, bareLimitC;
        public const float DefaultBareLimitC = 1100f;   // stage structure breaks up / melts
        public float BareLimit => bareLimitC > 0 ? bareLimitC : DefaultBareLimitC;
        public bool HasShield => shieldLimitC > 0;
        public static HeatProtection Tiles(float limitC, float halfAngle) => new HeatProtection { shieldLimitC = limitC, shieldHalfAngle = halfAngle };
        public static HeatProtection Base(float limitC) => new HeatProtection { shieldAxis = Vector3.down, shieldLimitC = limitC, shieldHalfAngle = 50 };
    }

    public struct StageSpec
    {
        public HeatProtection heat;
        // Attitude control with the engines off (RCS thrusters, the Ship's
        // flaps): the most angular acceleration it can give, deg/s².
        // 0 = the default for an upper stage (small RCS).
        public float rcsDegPerSec2;
        // Aerodynamic control surfaces (flaps, elevons, grid fins): their
        // authority grows with dynamic pressure - extra deg/s² per kPa.
        public float aeroControlPerKPa;
        public string name;
        // Stage_* groups of the body model (RocketBodies/{key}_stages.json)
        // that belong to this stage - they fall away when it separates.
        public string[] modelStages;
        public string[] engines;               // engine id per mount (centre first)
        public string fuelType;
        public float fuelMass, oxidizerMass;   // kg loaded
        public float dryMass;                  // kg, the stage's own structure + engines
        public float diameter;                 // m
        public float burnSeconds;              // cutoff; 0 = to depletion
        public float[] engineCutoffs;          // (seconds, socket) pairs
        public float separationDelay;          // previous stage cutoff -> separation
        public float ignitionDelay;            // separation -> this stage lights
        public StageDrop[] drops;              // parts shed during this stage's burn
    }

    /// <summary>Something shed partway through a stage's burn while it keeps
    /// going: strap-on boosters (Vostok), SRBs (Shuttle).</summary>
    public struct StageDrop
    {
        public float time;                     // s after the stage's ignition
        public string[] modelStages;
        public float mass;                     // kg dropped (not counting solid boosters)
        public bool solids;                    // jettison the vehicle's solid boosters
    }

    private static string[] Engines(string id, int count)
    {
        var ids = new string[count];
        for (var i = 0; i < count; i++) ids[i] = id;
        return ids;
    }


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
        // Staging. When set, the first stage (the fields above) separates at
        // its cutoff and each upper stage lights in turn; dryMass is then
        // only the first stage's own, and payloadMass is what rides on top
        // of the last stage. firstStageModelStages: the body model's groups
        // that drop with the first stage.
        public string firstStageName;
        public string[] firstStageModelStages;
        public StageDrop[] firstStageDrops;
        public StageSpec[] upperStages;
        public float payloadMass;
        // The most the real vehicle could carry to low Earth orbit (kg); the
        // payload slider stops here. Mutable at runtime: the pilot's choice.
        public float payloadMax;
        public string description;      // one or two lines for the launch menu
        public HeatProtection firstStageHeat;
        public float firstStageRcs;      // deg/s² with the engines off; 0 = none (most boosters)
        public float firstStageAeroControl;   // deg/s² per kPa of dynamic pressure (grid fins)
        public bool Staged => upperStages != null && upperStages.Length > 0;
        /// <summary>Everything above the first stage's propellant - the mass the
        /// flight model treats as dry at liftoff.</summary>
        public float LiftoffDryMass
        {
            get
            {
                if (!Staged) return dryMass;
                var total = dryMass + payloadMass;
                foreach (var s in upperStages) total += s.dryMass + s.fuelMass + s.oxidizerMass;
                return total;
            }
        }
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
            // Booster comes back engines-first behind its base heat shield,
            // turned by its cold-gas thrusters and grid fins.
            firstStageHeat = HeatProtection.Base(1300f), firstStageRcs = 1.5f, firstStageAeroControl = 1.5f,
            description = "SpaceX's workhorse since 2010: nine Merlins on a reusable booster, one Merlin Vacuum above.",
            name = "Falcon 9", bodyModelKey = "Falcon9", engineId = "Merlin1D", engineCount = 9,
            // 70 m overall, the real Block 5 height (was 87 m before the
            // to-scale body model came in).
            bodyDiameter = 3.7f, bodyHeight = 53f, noseHeight = 13f, engineHeight = 4f,
            hullColor = new Color(0.94f, 0.94f, 0.95f), dragCoefficient = 0.3f,
            fuelType = "RP-1", fuelCapacity = 152.5f, fuelDiameter = 3.7f,
            oxidizerCapacity = 251.9f, oxidizerDiameter = 3.7f,
            // Staged: booster (25.6 t dry) + second stage + fairing/payload =
            // ~550 t at liftoff.
            dryMass = 25600f,
            firstStageName = "Booster",
            firstStageModelStages = new[] { "FirstStage" },
            upperStages = new[]
            {
                // Second stage: separation ~3 s after MECO, Merlin Vacuum
                // lights ~7 s later; 92.7 t RP-1/LOX, burns to depletion
                // (~5.4 min).
                new StageSpec
                {
                    name = "Second stage", modelStages = new[] { "SecondStage" },
                    engines = Engines("MerlinVac", 1), fuelType = "RP-1",
                    fuelMass = 27600f, oxidizerMass = 65100f, dryMass = 3900f, diameter = 3.66f,
                    separationDelay = 3f, ignitionDelay = 7f,
                },
            },
            // Fairing (1.9 t, stays on for now) + a 15 t payload.
            payloadMass = 16900f,
            payloadMax = 22800f,      // expendable, LEO
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
            firstStageHeat = HeatProtection.Base(1300f), firstStageRcs = 1f, firstStageAeroControl = 1f,
            description = "The largest rocket ever flown: 33 Raptors on Super Heavy, hot-staged under the Ship.",
            name = "Starship", bodyModelKey = "Starship", engineId = "Raptor2", engineCount = 33,
            // 121.3 m overall, the full Super Heavy + Ship stack (Flight 5).
            bodyDiameter = 9f, bodyHeight = 95.3f, noseHeight = 18f, engineHeight = 8f,
            hullColor = new Color(0.74f, 0.75f, 0.77f), dragCoefficient = 0.55f,
            // Super Heavy's 3,400 t of propellant (O/F 3.6).
            fuelType = "Methane", fuelCapacity = 1749f, fuelDiameter = 9f,
            oxidizerCapacity = 2332f, oxidizerDiameter = 9f,
            // Staged: Super Heavy (~200 t dry) + Ship (100 t dry, 1,200 t
            // propellant) = ~4,900 t at liftoff.
            dryMass = 200000f,
            firstStageName = "Super Heavy",
            firstStageModelStages = new[] { "SuperHeavy" },
            upperStages = new[]
            {
                // Ship: hot staging - lights as the booster drops away; 3
                // sea-level Raptors (steering) + 3 fixed Raptor Vacuums; burns
                // its 1,200 t to depletion (~4.8 min at full thrust).
                new StageSpec
                {
                    name = "Ship", modelStages = new[] { "Ship" },
                    // Hexagonal tiles on the windward half; enters belly-first.
                    heat = HeatProtection.Tiles(1600f, 80f), rcsDegPerSec2 = 2f, aeroControlPerKPa = 2f,   // RCS + four flaps
                    engines = new[] { "Raptor2", "Raptor2", "Raptor2", "RaptorVac", "RaptorVac", "RaptorVac" },
                    fuelType = "Methane", fuelMass = 260900f, oxidizerMass = 939100f, dryMass = 100000f, diameter = 9f,
                    separationDelay = .5f, ignitionDelay = .5f,
                },
            },
            // Hot staging at T+2:39, throttled to 60% through Max Q; the
            // ~26 t left is the booster's boostback/landing reserve.
            payloadMax = 150000f,     // to LEO, as SpaceX quotes for Starship
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
            description = "Gagarin's 1961 launcher: the R-7 core and four strap-ons, then the Block E upper stage.",
            name = "Vostok", bodyModelKey = "Vostok", engineId = "RD107", engineCount = 5, coreEngineId = "RD108",
            // 38.4 m overall, the real Vostok-K height (was 39 m).
            bodyDiameter = 2.99f, bodyHeight = 27.4f, noseHeight = 8f, engineHeight = 3f,
            hullColor = new Color(0.52f, 0.54f, 0.48f), dragCoefficient = 0.45f,
            // 249.9 t of core + strap-on propellant at the engines' O/F ~2.45,
            // so fuel and LOX run out together.
            fuelType = "RP-1", fuelCapacity = 89.4f, fuelDiameter = 2.99f,
            oxidizerCapacity = 155.6f, oxidizerDiameter = 2.99f,
            // Staged: core Block A (6.9 t dry) + 4 strap-ons (3.8 t dry each)
            // + Block E + Vostok spacecraft = ~285 t at liftoff.
            dryMass = 22100f,
            firstStageName = "Blocks A + B/V/G/D",
            firstStageModelStages = new[] { "Core" },
            // The strap-ons shut down and fall away at T+118 s; the core keeps
            // burning its own propellant to ~T+5:13.
            firstStageDrops = new[]
            {
                new StageDrop { time = 118.5f, modelStages = new[] { "Booster_B", "Booster_V", "Booster_G", "Booster_D" }, mass = 15200f },
            },
            upperStages = new[]
            {
                // Block E: lights as the core cuts off (fire-in-the-hole
                // through the truss), 6.3 t RP-1/LOX to depletion (~6 min).
                new StageSpec
                {
                    name = "Block E", modelStages = new[] { "BlockE" },
                    engines = Engines("RD0109", 1), fuelType = "RP-1",
                    fuelMass = 1800f, oxidizerMass = 4500f, dryMass = 1440f, diameter = 2.66f,
                },
            },
            // Vostok spacecraft (4.7 t) + shroud.
            payloadMass = 5000f,
            payloadMax = 4730f + 270f, // Vostok spacecraft (4.73 t) + adapter
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
            description = "Apollo's Moon rocket, 1967-73: five F-1s, then the S-II and S-IVB on hydrogen.",
            name = "Saturn V", bodyModelKey = "SaturnV", engineId = "F1", engineCount = 5,
            // 110.6 m overall, the real Apollo stack height.
            bodyDiameter = 10.1f, bodyHeight = 94.6f, noseHeight = 13f, engineHeight = 3f,
            hullColor = new Color(0.92f, 0.92f, 0.89f), dragCoefficient = 0.4f,
            // Apollo 11 S-IC load: 639 t RP-1 + 1,428 t LOX (O/F 2.24, close
            // to the F-1's 2.27 - an earlier version had the RP-1 volume in
            // litres read as tonnes, leaving 190 t of LOX unburnable).
            fuelType = "RP-1", fuelCapacity = 788.9f, fuelDiameter = 10.1f,
            oxidizerCapacity = 1251.5f, oxidizerDiameter = 10.1f,
            // Staged (Apollo 11 timeline). S-IC dry 130.9 t; liftoff total
            // ~2,868 t (the real 2,938 t carried more S-IC propellant than
            // this engine model can burn by the real cutoff).
            dryMass = 130900f,
            firstStageName = "S-IC",
            firstStageModelStages = new[] { "SIC" },
            upperStages = new[]
            {
                // S-II: lights T+164.0 (0.7 s separation + 1.7 s ullage),
                // centre J-2 off T+460.6, cutoff T+548.2. 40.8 t dry with the
                // S-IC/S-II interstage ring (which really drops ~30 s after
                // ignition - left attached for now).
                new StageSpec
                {
                    name = "S-II", modelStages = new[] { "Interstage", "SII" },
                    engines = Engines("J2", 5), fuelType = "LH2",
                    fuelMass = 72700f, oxidizerMass = 386000f, dryMass = 40800f, diameter = 10.1f,
                    burnSeconds = 384.2f, engineCutoffs = new[] { 296.6f, 0f },
                    separationDelay = .7f, ignitionDelay = 1.7f,
                },
                // S-IVB + Instrument Unit: lights T+552.2, first burn to orbit
                // ends T+699.3 (147.1 s); ~70 t is left for the translunar
                // injection relight (Space).
                new StageSpec
                {
                    name = "S-IVB", modelStages = new[] { "SIVB" },
                    engines = Engines("J2", 1), fuelType = "LH2",
                    fuelMass = 19800f, oxidizerMass = 87200f, dryMass = 13300f, diameter = 6.6f,
                    burnSeconds = 147.1f,
                    separationDelay = .8f, ignitionDelay = 3.2f,
                },
            },
            // Apollo spacecraft on top: CSM, LM, adapter, escape tower.
            payloadMass = 49900f,
            payloadMax = 140000f,     // LEO (Skylab-class launch: ~140 t)
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
            description = "1981-2011: two solid boosters and three RS-25s fed by the External Tank, then the orbiter's OMS.",
            name = "Space Shuttle", bodyModelKey = "SpaceShuttle", engineId = "RS25", engineCount = 3,
            solidBoosterId = "SRB", solidBoosterCount = 2,
            // 56.1 m overall, the real stack height.
            bodyDiameter = 8.4f, bodyHeight = 42.1f, noseHeight = 10f, engineHeight = 4f,
            hullColor = new Color(0.72f, 0.42f, 0.25f), dragCoefficient = 0.5f,
            fuelType = "LH2", fuelCapacity = 1499.9f, fuelDiameter = 8.4f,
            oxidizerCapacity = 551.6f, oxidizerDiameter = 8.4f,
            // Staged: the first stage is the External Tank (26.5 t dry) and
            // the SRBs, with the orbiter's RS-25s; ~2,030 t at liftoff.
            dryMass = 26500f,
            firstStageName = "SRBs + External Tank",
            firstStageModelStages = new[] { "ExternalTank" },
            // SRB burnout and separation ~T+2:04; the RS-25s burn on from the
            // External Tank.
            firstStageDrops = new[]
            {
                new StageDrop { time = 124.5f, modelStages = new[] { "SRB_Left", "SRB_Right" }, solids = true },
            },
            upperStages = new[]
            {
                // Orbiter after MECO and External Tank separation: the two OMS
                // engines (OMS-2 burn ~2.3 min; restartable).
                new StageSpec
                {
                    name = "Orbiter (OMS)", modelStages = new[] { "Orbiter" },
                    // Black HRSI tiles and RCC on the belly / nose / wing edges.
                    heat = HeatProtection.Tiles(1650f, 75f), rcsDegPerSec2 = 2f, aeroControlPerKPa = 1.5f,   // 44 RCS thrusters + elevons/body flap
                    engines = Engines("OMS", 2), fuelType = "MMH",
                    fuelMass = 3100f, oxidizerMass = 5100f, dryMass = 79900f, diameter = 5.2f,
                    burnSeconds = 140f,
                    separationDelay = 18f, ignitionDelay = 100f,
                },
            },
            // First stage: the SRBs, burnout ~T+2:04 (their grain profile).
            // The RS-25s then run on to MECO at T+8:30: 104.5% rated power
            // (0.959 of the 109% this engine model is rated at), throttled
            // to 72% through Max Q (T+26-60 s).
            payloadMax = 24400f,      // cargo bay, LEO at 28.5°
            mecoSeconds = 510f,
            throttleProgram = new[] { 0f, .959f, 26f, .959f, 30f, .66f, 56f, .66f, 60f, .959f },
        },
        new()
        {
            // SLS Block 1, as flown on Artemis I (16 Nov 2022): 98.1 m,
            // ~2,610 t at liftoff, 39.1 MN.
            description = "NASA's Artemis Moon rocket: two five-segment boosters, four RS-25s on the core, ICPS and Orion on top.",
            name = "SLS Block 1", bodyModelKey = "SLS", engineId = "RS25", engineCount = 4,
            solidBoosterId = "SLS_SRB", solidBoosterCount = 2,
            bodyDiameter = 8.4f, bodyHeight = 80.1f, noseHeight = 12f, engineHeight = 6f,
            hullColor = new Color(0.80f, 0.42f, 0.16f), dragCoefficient = 0.5f,
            // Core stage: 144 t LH2 + 843 t LOX.
            fuelType = "LH2", fuelCapacity = 2032.5f, fuelDiameter = 8.4f,
            oxidizerCapacity = 738.8f, oxidizerDiameter = 8.4f,
            // Core stage 85.3 t dry + LVSA 4.2 t + the 7.4 t Launch Abort
            // System (jettisoned at T+3:15).
            dryMass = 96900f,
            firstStageName = "Core stage + boosters",
            firstStageModelStages = new[] { "Core" },
            firstStageDrops = new[]
            {
                new StageDrop { time = 132f, modelStages = new[] { "SRB_Left", "SRB_Right" }, solids = true },
                new StageDrop { time = 195f, modelStages = new[] { "LAS" }, mass = 7400f },
            },
            upperStages = new[]
            {
                // ICPS: one RL10B-2, 27.2 t of hydrogen/oxygen, burns ~18 min.
                new StageSpec
                {
                    name = "ICPS", modelStages = new[] { "ICPS" },
                    engines = Engines("RL10B2", 1), fuelType = "LH2",
                    fuelMass = 3956f, oxidizerMass = 23264f, dryMass = 3490f, diameter = 5.0f,
                    separationDelay = 8f, ignitionDelay = 12f,
                },
            },
            payloadMass = 26520f,     // Orion at translunar injection
            payloadMax = 95000f,      // to LEO
            // Core MECO at T+8:04; RS-25s at 109%, throttled to 85% through Max Q.
            mecoSeconds = 484f,
            throttleProgram = new[] { 0f, 1f, 30f, 1f, 35f, .78f, 62f, .78f, 67f, 1f },
        },
        new()
        {
            // Ariane 5 ECA (long fairing): 53 m, ~780 t at liftoff.
            description = "Europe's heavy lifter, 1996-2023 (and the James Webb launcher): two P241 solids, Vulcain 2 core, ESC-A upper stage.",
            name = "Ariane 5 ECA", bodyModelKey = "Ariane5", engineId = "Vulcain2", engineCount = 1,
            solidBoosterId = "P241", solidBoosterCount = 2,
            bodyDiameter = 5.4f, bodyHeight = 41f, noseHeight = 8f, engineHeight = 4f,
            hullColor = new Color(0.93f, 0.93f, 0.93f), dragCoefficient = 0.5f,
            // EPC: 24.7 t LH2 + 150.4 t LOX.
            fuelType = "LH2", fuelCapacity = 347.9f, fuelDiameter = 5.4f,
            oxidizerCapacity = 131.8f, oxidizerDiameter = 5.4f,
            // EPC 14.7 t dry + the 2.7 t long fairing (jettisoned T+3:11).
            dryMass = 17375f,
            firstStageName = "EPC + boosters",
            firstStageModelStages = new[] { "EPC" },
            firstStageDrops = new[]
            {
                new StageDrop { time = 140f, modelStages = new[] { "EAP_Left", "EAP_Right" }, solids = true },
                new StageDrop { time = 191f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 2675f },
            },
            upperStages = new[]
            {
                // ESC-A: one HM7B, 14.9 t of propellant, burns ~16 min; dry
                // mass includes the vehicle equipment bay and SYLDA.
                new StageSpec
                {
                    name = "ESC-A", modelStages = new[] { "ESC" },
                    engines = Engines("HM7B", 1), fuelType = "LH2",
                    fuelMass = 2480f, oxidizerMass = 12420f, dryMass = 6200f, diameter = 5.4f,
                    separationDelay = 6f, ignitionDelay = 4f,
                },
            },
            payloadMass = 10000f,     // a GTO pair
            payloadMax = 21000f,      // to LEO
            // The EPC burns to depletion (~T+8:50); Vulcain 2 isn't throttled.
        },
        new()
        {
            // Atlas V 401 (no solids, 4-m fairing, single-engine Centaur): 58.3 m, ~335 t.
            description = "ULA's workhorse since 2002: a Russian RD-180 on the Common Core Booster, then Centaur with one RL10.",
            name = "Atlas V 401", bodyModelKey = "AtlasV", engineId = "RD180", engineCount = 1,
            bodyDiameter = 3.81f, bodyHeight = 46.3f, noseHeight = 9f, engineHeight = 3f,
            hullColor = new Color(0.72f, 0.43f, 0.22f), dragCoefficient = 0.5f,
            // CCB: 76.4 t RP-1 + 207.7 t LOX.
            fuelType = "RP-1", fuelCapacity = 94.3f, fuelDiameter = 3.81f,
            oxidizerCapacity = 182.1f, oxidizerDiameter = 3.81f,
            // CCB 21.05 t dry + interstage 1 t + the 2.1 t fairing (T+3:30).
            dryMass = 24180f,
            firstStageName = "Common Core Booster",
            firstStageModelStages = new[] { "Booster" },
            firstStageDrops = new[]
            {
                new StageDrop { time = 210f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 2127f },
            },
            upperStages = new[]
            {
                // Centaur III: one RL10C-1, 20.8 t of propellant, up to ~14 min.
                new StageSpec
                {
                    name = "Centaur", modelStages = new[] { "Centaur" },
                    engines = Engines("RL10C1", 1), fuelType = "LH2",
                    fuelMass = 3030f, oxidizerMass = 17800f, dryMass = 2316f, diameter = 3.05f,
                    separationDelay = 6f, ignitionDelay = 10f,
                },
            },
            payloadMass = 5000f,
            payloadMax = 9797f,       // to LEO, 28.7°
            // RD-180 throttled through Max Q, and back late in the burn to
            // hold the g-load; booster engine cutoff ~T+4:10 at depletion.
            throttleProgram = new[] { 0f, 1f, 30f, 1f, 40f, .92f, 75f, .92f, 85f, 1f, 170f, 1f, 190f, .75f },
        },
    };
}
