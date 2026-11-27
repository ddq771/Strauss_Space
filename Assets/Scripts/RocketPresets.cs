using UnityEngine;

// STUDY: ==================== STUDY GUIDE: RocketPresets ====================
// STUDY: WHAT: pure data - the real rockets (Falcon 9, Starship, Vostok, Saturn V,
// STUDY: Shuttle, ...). Each Preset: body size and model, engines, propellant, dry
// STUDY: mass, payload, first-stage flight program (throttle schedule, MECO), strap-on
// STUDY: drops, upper stages (StageSpec), heat protection per stage (HeatProtection
// STUDY: / HeatZone), aerodynamics. No behaviour lives here.
// STUDY: USED BY: LaunchMenu (the line-up), RocketAssemblyController.LoadPreset and
// STUDY: staging, Autopilot (stage masses for delta-v and the ascent simulation),
// STUDY: ReentryHeating (zones), RocketFlightModel.
// STUDY: To add a rocket: add a new Preset to All - it appears in the menu by itself.

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
/// - Staged presets separate for real: each stage's Stage_* parts in the
///   body model detach (RocketAssemblyController.Separate / Drop) and the
///   next stage lights on the real timeline. Strap-ons, fairings, shrouds,
///   interstage rings and escape towers drop off mid-burn as StageDrops.
/// - Simplified: physics treats the vehicle as one cylinder of the body
///   diameter with a fixed drag coefficient, and the Space Shuttle flies as
///   an in-line stack rather than a side-mounted orbiter.
/// </summary>
public static class RocketPresets
{
    /// <summary>
    /// A stage that takes over after the one below it is spent. Times are
    /// seconds after this stage's own ignition.
    /// </summary>
    /// <summary>
    /// One area of a stage's surface and what it's made of, for re-entry
    /// heating (ReentryHeating). Directions are in the vehicle frame: +Y the
    /// nose, +Z the windward side the tiles are on (found from the body
    /// model's tile meshes; the -Z side of an untiled body), +X across.
    /// </summary>
    [System.Serializable]
    public struct HeatZone
    {
        public string name;
        public Vector3 direction;       // the way the area faces (vehicle frame)
        public float halfAngle;         // degrees: surface facing within this of direction
        public float limitC;            // hottest it survives (°C)
        public float emissivity;        // how well it radiates heat away
        public float heatCapacity;      // J/(m²·K) of the hot surface layer: how fast it heats
        // Local radius of curvature (m) for the heating: tight curves (a
        // nose cap, a leading edge) heat far more than broad flat ones (a
        // belly). 0 = the body radius.
        public float localRadius;

        // Bare structure. Aluminium-lithium tank walls lose their strength
        // by ~500 °C (and are painted, radiating well); 300-series
        // stainless holds to ~820-870 °C but radiates poorly when bare.
        public static HeatZone Aluminium => new HeatZone { name = "bare aluminium", limitC = 500f, emissivity = .85f, heatCapacity = 9000f };
        public static HeatZone Steel => new HeatZone { name = "bare stainless steel", limitC = 870f, emissivity = .55f, heatCapacity = 16000f };
        public static Vector3 Nose => Vector3.up;
        public static Vector3 Base => Vector3.down;
        public static Vector3 Windward => Vector3.forward;
        /// <summary>Which way the vehicle meets the air flying nose-forward at this angle of attack, windward side down.</summary>
        public static Vector3 AtAngleOfAttack(float degrees) => new Vector3(0, Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
    }

    /// <summary>
    /// A stage's thermal protection: the protected zones (a surface patch
    /// belongs to the first one it falls in), the bare structure that
    /// covers the rest, and the attitude it was built to enter at. Real
    /// ratings: on its design entry each zone peaks below its limit; turn
    /// a weaker area into the flow and it overheats.
    /// </summary>
    [System.Serializable]
    public struct HeatProtection
    {
        public HeatZone[] zones;
        public HeatZone bare;                    // default: bare aluminium
        // The side to put into the airflow (vehicle frame) - the re-entry
        // attitude. Zero = nose-first (no protection to show the flow).
        public Vector3 entryDirection;
        public float liftToDrag;                 // hypersonic L/D at that attitude (0 = ballistic)
        public float planformArea;               // m² meeting the air side-on (0 = length × diameter)
        // Set at runtime: the windward (+Z) side in the rocket's own axes.
        [System.NonSerialized] public Vector3 windwardLocal;

        public HeatZone Bare => bare.limitC > 0 ? bare : HeatZone.Aluminium;
        public bool HasShield => zones != null && zones.Length > 0;
        public Vector3 EntryDirection => entryDirection.sqrMagnitude > 0 ? entryDirection.normalized : HeatZone.Nose;
        /// <summary>Vehicle frame (+Y nose, +Z windward) to the rocket's own axes.</summary>
        public Vector3 ToLocal(Vector3 v)
        {
            var w = windwardLocal.sqrMagnitude > 0 ? windwardLocal.normalized : Vector3.back;
            return (Vector3.up * v.y + w * v.z + Vector3.Cross(Vector3.up, w) * v.x).normalized;
        }

        /// <summary>A booster that comes back engines-first behind a base heat shield.</summary>
        public static HeatProtection BaseShield(float limitC, HeatZone bare) => new HeatProtection
        {
            zones = new[] { new HeatZone { name = "base heat shield", direction = HeatZone.Base, halfAngle = 50, limitC = limitC, emissivity = .85f, heatCapacity = 12000f } },
            bare = bare,
            entryDirection = HeatZone.Base,
        };

        /// <summary>A payload fairing over the nose with cork ablative insulation, as on
        /// Atlas V: during the climb the nose takes the heating (it faces the air), and
        /// a booster still accelerating hard low in the atmosphere - Atlas V reaches
        /// ~3.3 km/s below 70 km - heats it far past the 500 °C bare aluminium is rated
        /// for (~60 kW/m² there, a radiative balance near 770 °C). Cork chars and
        /// carries the heat away instead; rated here to 1,000 °C. The zone covers
        /// the whole forward-facing half (90°): ReentryHeating models the hull as a
        /// sphere of patches, and climbing nose-first every patch the air meets
        /// is the fairing - the aluminium tank sides behind it meet the air
        /// edge-on. (A 45° nose cap alone left the patches just outside it, still
        /// facing the flow, overheating as bare aluminium with a heavier payload.)</summary>
        public static HeatProtection FairingNose => new HeatProtection
        {
            zones = new[] { new HeatZone { name = "payload fairing (cork insulation)", direction = HeatZone.Nose, halfAngle = 90, limitC = 1000f, emissivity = .85f, heatCapacity = 12000f } },
            bare = HeatZone.Aluminium,
        };
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
        // Optional first-stage engine layout as (count, radius m, phase
        // degrees, centre offset x m) groups; the sockets are numbered group
        // by group in this order, so engineCutoffs can name a whole group
        // (Falcon Heavy's side cores). Null = the automatic centre + rings.
        // RocketAssemblyController.BodyThrustPositions lays them out.
        public float[] engineLayout;
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
        // Payload fairing (or nose shroud) chosen off in the launch menu:
        // its mass comes off whichever stage carries it, it isn't drawn or
        // jettisoned, and the bare payload adds drag.
        public bool fairingOff;
        public static bool IsFairingDrop(StageDrop d) =>
            d.modelStages != null && System.Array.Exists(d.modelStages, n => n.Contains("Fairing") || n.Contains("Shroud"));
        private static float FairingMassOf(StageDrop[] drops)
        {
            float m = 0;
            if (drops != null) foreach (var d in drops) if (IsFairingDrop(d)) m += d.mass;
            return m;
        }
        /// <summary>Fairing mass carried by stage k (0 = first stage).</summary>
        public float FairingMass(int k) =>
            k == 0 ? FairingMassOf(firstStageDrops) : k - 1 < (upperStages?.Length ?? 0) ? FairingMassOf(upperStages[k - 1].drops) : 0;
        public bool HasFairing
        {
            get
            {
                if (!Staged) return false;
                for (var k = 0; k <= upperStages.Length; k++) if (FairingMass(k) > 0) return true;
                return false;
            }
        }
        /// <summary>Dry mass of stage k as flown (without the fairing if it's off).</summary>
        public float StageDry(int k) =>
            (k == 0 ? dryMass : upperStages[k - 1].dryMass) - (fairingOff ? FairingMass(k) : 0);

        public float LiftoffDryMass
        {
            get
            {
                if (!Staged) return dryMass;
                var total = StageDry(0) + payloadMass;
                for (var k = 1; k <= upperStages.Length; k++)
                {
                    var s = upperStages[k - 1];
                    total += StageDry(k) + s.fuelMass + s.oxidizerMass;
                }
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
            firstStageHeat = HeatProtection.BaseShield(1300f, HeatZone.Aluminium), firstStageRcs = 1.5f, firstStageAeroControl = 1.5f,
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
                    // 3.9 t dry + the 1.9 t fairing riding on it until jettison.
                    fuelMass = 27600f, oxidizerMass = 65100f, dryMass = 5800f, diameter = 3.66f,
                    separationDelay = 3f, ignitionDelay = 7f,
                    // Fairing halves jettisoned ~T+3:30, 38 s into the burn.
                    drops = new[] { new StageDrop { time = 38f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 1900f } },
                },
            },
            // A 15 t payload under the fairing.
            payloadMass = 15000f,
            payloadMax = 22800f,      // expendable, LEO
            // MECO at T+2:42, with the throttle down to 70% through Max Q
            // (T+52-85 s); leaves ~6 t residual.
            mecoSeconds = 162f,
            throttleProgram = new[] { 0f, 1f, 45f, 1f, 52f, .7f, 85f, .7f, 92f, 1f },
        },
        new()
        {
            // Falcon Heavy: three Falcon 9 first-stage cores side by side
            // (27 Merlin 1D), the Falcon 9 second stage and fairing on the
            // centre core. 70 m tall, 12.2 m wide, ~1,420 t at liftoff.
            // The cores share one propellant pool here (the game models one
            // first-stage tank): the side cores' 18 engines shut down at
            // T+2:34 and the boosters drop away; the centre core's 9 burn on
            // to MECO. (The real centre core throttles down early to save
            // its own propellant; one pool gives the same total burn.)
            // Aero: three cores, no fins: centre of pressure forward - unstable.
            centerOfPressure = .7f, normalForceSlope = 2.6f,
            firstStageHeat = HeatProtection.BaseShield(1300f, HeatZone.Aluminium), firstStageRcs = 1.5f, firstStageAeroControl = 1.5f,
            description = "SpaceX's heavy lifter since 2018: three Falcon 9 cores, 27 Merlins; the side boosters peel away at T+2:34.",
            name = "Falcon Heavy", bodyModelKey = "FalconHeavy", engineId = "Merlin1D", engineCount = 27,
            // Physics flies one cylinder: 6.34 m has the frontal area of the
            // three 3.66 m cores together (31.6 m²), so drag comes out right.
            bodyDiameter = 6.34f, bodyHeight = 53f, noseHeight = 13f, engineHeight = 4f,
            hullColor = new Color(0.94f, 0.94f, 0.95f), dragCoefficient = 0.35f,
            // Three Falcon 9 loads: 3 × (123.5 t RP-1 + 287.4 t LOX).
            fuelType = "RP-1", fuelCapacity = 457.5f, fuelDiameter = 6.34f,
            oxidizerCapacity = 755.7f, oxidizerDiameter = 6.34f,
            // Three cores, 25.6 t dry each.
            dryMass = 76800f,
            firstStageName = "Centre core + side boosters",
            firstStageModelStages = new[] { "CentreCore" },
            // Side boosters separate at T+2:34 (their 18 engines cut off then,
            // see engineCutoffs).
            firstStageDrops = new[]
            {
                new StageDrop { time = 154f, modelStages = new[] { "Booster_Left", "Booster_Right" }, mass = 51200f },
            },
            upperStages = new[]
            {
                // The Falcon 9 second stage: one Merlin Vacuum, 92.7 t of
                // RP-1/LOX; the fairing goes ~T+3:40, 30 s after it lights.
                new StageSpec
                {
                    name = "Second stage", modelStages = new[] { "SecondStage" },
                    engines = Engines("MerlinVac", 1), fuelType = "RP-1",
                    fuelMass = 27600f, oxidizerMass = 65100f, dryMass = 5800f, diameter = 3.66f,
                    separationDelay = 3f, ignitionDelay = 7f,
                    drops = new[] { new StageDrop { time = 30f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 1900f } },
                },
            },
            payloadMass = 15000f,
            payloadMax = 63800f,      // to LEO, fully expendable
            // Each core as a Falcon 9 octaweb: centre engine + ring of 8
            // (1.3 m radius); centre core first, then the side cores 4.1 m
            // either side - so sockets 9-26 are the side cores.
            engineLayout = new[] { 1f, 0f, 0f, 0f, 8f, 1.3f, 0f, 0f, 1f, 0f, 0f, -4.1f, 8f, 1.3f, 0f, -4.1f, 1f, 0f, 0f, 4.1f, 8f, 1.3f, 0f, 4.1f },
            engineCutoffs = new[]
            {
                154f, 9f, 154f, 10f, 154f, 11f, 154f, 12f, 154f, 13f, 154f, 14f, 154f, 15f, 154f, 16f, 154f, 17f,
                154f, 18f, 154f, 19f, 154f, 20f, 154f, 21f, 154f, 22f, 154f, 23f, 154f, 24f, 154f, 25f, 154f, 26f,
            },
            // Throttled to 70% through Max Q like Falcon 9; the centre core's
            // MECO at T+3:04, where the shared propellant runs out.
            mecoSeconds = 184f,
            throttleProgram = new[] { 0f, 1f, 45f, 1f, 52f, .7f, 85f, .7f, 92f, 1f },
        },
        new()
        {
            // First stage only: Super Heavy's 33 Raptors (74.4 MN at sea
            // level) lift the whole stack - the Ship's 6 engines don't fire
            // until after separation, when the Ship lights on its own
            // propellant (hot staging).
            // Aero: flaps fore and aft on the Ship, finless booster - mildly unstable.
            centerOfPressure = .62f, normalForceSlope = 2.6f,
            firstStageHeat = HeatProtection.BaseShield(1300f, HeatZone.Steel), firstStageRcs = 1f, firstStageAeroControl = 1f,
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
                    // Hexagonal silica tiles (rated ~1,400 °C) on the windward
                    // half and round the nose; the leeward side is bare
                    // stainless. Enters belly-first at ~65° angle of attack.
                    // The belly meets the air as a 9 m cylinder side-on,
                    // which heats ~1/√2 as much as a sphere (9 m radius).
                    heat = new HeatProtection
                    {
                        zones = new[]
                        {
                            new HeatZone { name = "belly tiles", direction = HeatZone.Windward, halfAngle = 80, limitC = 1400f, emissivity = .9f, heatCapacity = 4000f, localRadius = 9f },
                            new HeatZone { name = "nose tiles", direction = HeatZone.Nose, halfAngle = 40, limitC = 1400f, emissivity = .9f, heatCapacity = 4000f },
                        },
                        bare = HeatZone.Steel,
                        entryDirection = HeatZone.AtAngleOfAttack(65),
                    },
                    rcsDegPerSec2 = 2f, aeroControlPerKPa = 2f,   // RCS + four flaps
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
            // Rocket Lab Electron: 18 m, 1.2 m, ~13 t at liftoff - a small
            // launcher (300 kg to LEO) whose Rutherford engines' pumps are
            // driven by electric motors from batteries. Carbon-composite
            // body. Published: liftoff mass, thrust, Isp, ~2 t second-stage
            // propellant. Estimated: the first stage's 9.6 t of RP-1/LOX
            // (O/F 2.4: 2.82 t + 6.78 t) and 900 kg dry, from the liftoff
            // mass, which burn ~145 s (MECO is ~T+2:30 for real).
            // Aero: no fins, but a slim body (15:1) makes little lift - a
            // centre of pressure near the middle, mildly unstable.
            centerOfPressure = .5f, normalForceSlope = 2f,
            description = "Rocket Lab's small launcher since 2017: nine battery-powered Rutherford engines, 300 kg to orbit.",
            name = "Electron", bodyModelKey = "Electron", engineId = "Rutherford", engineCount = 9,
            bodyDiameter = 1.2f, bodyHeight = 14.9f, noseHeight = 2.5f, engineHeight = .6f,
            hullColor = new Color(0.08f, 0.08f, 0.09f), dragCoefficient = 0.35f,
            fuelType = "RP-1", fuelCapacity = 3.486f, fuelDiameter = 1.2f,
            oxidizerCapacity = 5.939f, oxidizerDiameter = 1.2f,
            dryMass = 900f,
            firstStageName = "First stage",
            firstStageModelStages = new[] { "FirstStage" },
            upperStages = new[]
            {
                // Second stage: one Rutherford Vacuum, ~2 t of propellant,
                // ~4.5 min. Dry 300 kg with the Curie kick stage, + the 50 kg
                // fairing until it goes, 25 s after ignition (~T+3:00).
                new StageSpec
                {
                    name = "Second stage", modelStages = new[] { "SecondStage" },
                    engines = Engines("RutherfordVac", 1), fuelType = "RP-1",
                    fuelMass = 588f, oxidizerMass = 1412f, dryMass = 350f, diameter = 1.2f,
                    separationDelay = 2f, ignitionDelay = 3f,
                    drops = new[] { new StageDrop { time = 25f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 50f } },
                },
            },
            payloadMass = 200f,       // to a 500 km sun-synchronous orbit
            payloadMax = 300f,        // to LEO
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
            // (+ the 0.55 t nose shroud until it's jettisoned.)
            dryMass = 22650f,
            firstStageName = "Blocks A + B/V/G/D",
            firstStageModelStages = new[] { "Core" },
            // The strap-ons shut down and fall away at T+118 s; the core keeps
            // burning its own propellant to ~T+5:13.
            firstStageDrops = new[]
            {
                new StageDrop { time = 118.5f, modelStages = new[] { "Booster_B", "Booster_V", "Booster_G", "Booster_D" }, mass = 15200f },
                // Nose shroud over the spacecraft jettisoned at T+2:36.
                new StageDrop { time = 156f, modelStages = new[] { "Shroud" }, mass = 550f },
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
            // Vostok spacecraft (4.73 t) + adapter; the shroud is in dryMass.
            payloadMass = 5000f,
            payloadMax = 4730f + 270f, // Vostok spacecraft (4.73 t) + adapter
            // First stage: the four strap-ons shut down (and on the real
            // vehicle separate) at T+118 s; the core burns on alone to
            // depletion at ~T+5:15.
            engineCutoffs = new[] { 118f, 1f, 118f, 2f, 118f, 3f, 118f, 4f },
        },
        new()
        {
            // Saturn IB (AS-205 / Apollo 7 configuration): 68 m with the
            // Apollo CSM and escape tower, ~590 t at liftoff, 21 t to LEO.
            // S-IB: 6.6 m, eight H-1 engines (7.3 MN), 400 t of RP-1/LOX at
            // O/F 2.23 (124 t + 276 t), 42 t dry; inboard engines off at
            // T+140.6 s, outboard at T+144.3 s. Then the S-IVB-200 - the same
            // stage as the Saturn V's third stage, here burning to orbit.
            // Aero: eight fins - stable.
            centerOfPressure = .3f, normalForceSlope = 4f,
            description = "The smaller Apollo rocket, 1966-75 (Apollo 7, Skylab crews, Apollo-Soyuz): eight H-1s, then an S-IVB.",
            name = "Saturn IB", bodyModelKey = "SaturnIB", engineId = "H1", engineCount = 8,
            bodyDiameter = 6.6f, bodyHeight = 47f, noseHeight = 18.06f, engineHeight = 3f,
            hullColor = new Color(0.92f, 0.92f, 0.89f), dragCoefficient = 0.45f,
            fuelType = "RP-1", fuelCapacity = 153f, fuelDiameter = 6.6f,
            oxidizerCapacity = 241.9f, oxidizerDiameter = 6.6f,
            dryMass = 42000f,
            firstStageName = "S-IB",
            firstStageModelStages = new[] { "SIB" },
            upperStages = new[]
            {
                // S-IVB-200: one J-2, ~105 t of LH2/LOX, burns to orbit (~7.5
                // min). Dry 11.5 t with the Instrument Unit + the 4.2 t escape
                // tower riding above until it's jettisoned 20 s in.
                new StageSpec
                {
                    name = "S-IVB", modelStages = new[] { "SIVB" },
                    engines = Engines("J2", 1), fuelType = "LH2",
                    fuelMass = 19400f, oxidizerMass = 85600f, dryMass = 15670f, diameter = 6.6f,
                    drops = new[] { new StageDrop { time = 20f, modelStages = new[] { "LES" }, mass = 4170f } },
                    separationDelay = 1.3f, ignitionDelay = 1.3f,
                },
            },
            // The Apollo CSM (Apollo 7: ~15.6 t) with its adapter.
            payloadMass = 15600f,
            payloadMax = 21000f,      // to LEO
            mecoSeconds = 144.3f,
            // Four inboard engines on an inner ring (sockets 0-3), four
            // outboard around them; the inboard four shut down first.
            engineLayout = new[] { 4f, .95f, 45f, 0f, 4f, 2.4f, 0f, 0f },
            engineCutoffs = new[] { 140.6f, 0f, 140.6f, 1f, 140.6f, 2f, 140.6f, 3f },
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
                // centre J-2 off T+460.6, cutoff T+548.2. Dry 40.8 t with the
                // 5.2 t S-IC/S-II interstage ring (dropped T+193.5) + the
                // 4.2 t launch escape tower riding above (jettisoned T+197.9).
                new StageSpec
                {
                    name = "S-II", modelStages = new[] { "SII" },
                    engines = Engines("J2", 5), fuelType = "LH2",
                    fuelMass = 72700f, oxidizerMass = 386000f, dryMass = 44970f, diameter = 10.1f,
                    drops = new[]
                    {
                        new StageDrop { time = 29.5f, modelStages = new[] { "Interstage" }, mass = 5200f },
                        new StageDrop { time = 33.9f, modelStages = new[] { "LES" }, mass = 4170f },
                    },
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
            // Apollo spacecraft on top: CSM, LM and adapter (the escape
            // tower is counted with the S-II, which carries it until jettison).
            payloadMass = 45730f,
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
                    // Enters at 40° angle of attack, gliding (hypersonic L/D
                    // ~1, 250 m² wing). Reinforced carbon-carbon on the nose
                    // cap and wing leading edges where the flow stagnates
                    // (1,650 °C); black HRSI tiles on the belly, lower sides
                    // and forward fuselage (1,260 °C); white LRSI tiles and
                    // FRSI blankets on top (650 °C).
                    heat = new HeatProtection
                    {
                        zones = new[]
                        {
                            new HeatZone { name = "nose cap (RCC)", direction = HeatZone.Nose, halfAngle = 30, limitC = 1650f, emissivity = .85f, heatCapacity = 8000f, localRadius = 2f },
                            new HeatZone { name = "wing leading edges (RCC)", direction = HeatZone.AtAngleOfAttack(40), halfAngle = 30, limitC = 1650f, emissivity = .85f, heatCapacity = 8000f, localRadius = 2f },
                            new HeatZone { name = "belly tiles (HRSI)", direction = HeatZone.Windward, halfAngle = 90, limitC = 1260f, emissivity = .85f, heatCapacity = 3500f, localRadius = 10f },
                            new HeatZone { name = "forward fuselage tiles (HRSI)", direction = HeatZone.Nose, halfAngle = 60, limitC = 1260f, emissivity = .85f, heatCapacity = 3500f, localRadius = 10f },
                        },
                        bare = new HeatZone { name = "upper surface (LRSI/FRSI)", limitC = 650f, emissivity = .8f, heatCapacity = 3000f, localRadius = 10f },
                        entryDirection = HeatZone.AtAngleOfAttack(40),
                        liftToDrag = 1f, planformArea = 250f,
                    },
                    rcsDegPerSec2 = 2f, aeroControlPerKPa = 1.5f,   // 44 RCS thrusters + elevons/body flap
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
            // The fairing's cork insulation protects the nose on the way up
            // (without it, the climb burned the nose off at T+3:40).
            firstStageHeat = HeatProtection.FairingNose,
            description = "ULA's workhorse since 2002: a Russian RD-180 on the Common Core Booster, then Centaur with one RL10.",
            name = "Atlas V 401", bodyModelKey = "AtlasV", engineId = "RD180", engineCount = 1,
            bodyDiameter = 3.81f, bodyHeight = 46.3f, noseHeight = 9f, engineHeight = 3f,
            hullColor = new Color(0.72f, 0.43f, 0.22f), dragCoefficient = 0.5f,
            // CCB: 76.4 t RP-1 + 207.7 t LOX.
            fuelType = "RP-1", fuelCapacity = 94.3f, fuelDiameter = 3.81f,
            oxidizerCapacity = 182.1f, oxidizerDiameter = 3.81f,
            // CCB 21.05 t dry + interstage 1 t.
            dryMass = 22053f,
            firstStageName = "Common Core Booster",
            firstStageModelStages = new[] { "Booster" },
            upperStages = new[]
            {
                // Centaur III: one RL10C-1, 20.8 t of propellant, up to ~14 min.
                new StageSpec
                {
                    name = "Centaur", modelStages = new[] { "Centaur" },
                    engines = Engines("RL10C1", 1), fuelType = "LH2",
                    // 2.3 t dry + the 2.1 t fairing riding on it until jettison.
                    fuelMass = 3030f, oxidizerMass = 17800f, dryMass = 4443f, diameter = 3.05f,
                    separationDelay = 6f, ignitionDelay = 10f,
                    // Fairing jettisoned ~8 s after Centaur ignition (~T+4:27),
                    // once the booster is gone - not during the booster burn.
                    drops = new[] { new StageDrop { time = 8f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 2127f } },
                },
            },
            payloadMass = 5000f,
            payloadMax = 9797f,       // to LEO, 28.7°
            // RD-180 at full thrust until late in the burn, then back to 75 %
            // to hold the g-load; booster engine cutoff ~T+4:10 at depletion.
            // No Max Q throttle-down: with no solids the 401 lifts off at
            // only 1.16× its weight, and cutting to 92 % from T+40-75 s, when
            // it's slowest, let gravity bend its path over - the steering
            // can't hold the nose more than a few degrees off the airflow
            // there - until it flew nearly level at 40 km, 3 km/s, and the
            // nose burned off. (The 431's solids give it thrust to spare, so
            // it keeps the throttle-down.)
            throttleProgram = new[] { 0f, 1f, 170f, 1f, 190f, .75f },
        },
        new()
        {
            // Atlas V 431: the 401 (same booster, single-engine Centaur, 4-m
            // fairing) with three AJ-60A solid boosters: ~474 t at liftoff,
            // 15.3 t to LEO.
            // Aero: three solids add area low down - near neutral.
            centerOfPressure = .45f, normalForceSlope = 3f,
            firstStageHeat = HeatProtection.FairingNose,   // as on the 401
            description = "Atlas V with three solid boosters strapped to the RD-180 core: the 400 series' heavier lifter.",
            name = "Atlas V 431", bodyModelKey = "AtlasV431", engineId = "RD180", engineCount = 1,
            solidBoosterId = "AJ60A", solidBoosterCount = 3,
            bodyDiameter = 3.81f, bodyHeight = 46.3f, noseHeight = 9f, engineHeight = 3f,
            hullColor = new Color(0.72f, 0.43f, 0.22f), dragCoefficient = 0.5f,
            fuelType = "RP-1", fuelCapacity = 94.3f, fuelDiameter = 3.81f,
            oxidizerCapacity = 182.1f, oxidizerDiameter = 3.81f,
            dryMass = 22053f,
            firstStageName = "Common Core Booster + solids",
            firstStageModelStages = new[] { "Booster" },
            // The solids burn out ~T+1:34 and are jettisoned at ~T+1:40.
            firstStageDrops = new[]
            {
                new StageDrop { time = 100f, modelStages = new[] { "SRB_1", "SRB_2", "SRB_3" }, solids = true },
            },
            upperStages = new[]
            {
                new StageSpec
                {
                    name = "Centaur", modelStages = new[] { "Centaur" },
                    engines = Engines("RL10C1", 1), fuelType = "LH2",
                    fuelMass = 3030f, oxidizerMass = 17800f, dryMass = 4443f, diameter = 3.05f,
                    separationDelay = 6f, ignitionDelay = 10f,
                    drops = new[] { new StageDrop { time = 8f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 2127f } },
                },
            },
            payloadMass = 10000f,
            payloadMax = 15260f,      // to LEO, 28.5°
            // RD-180 down to 92 % through Max Q (the solids give the 431
            // thrust to spare), and back to 75 % late in the burn to hold
            // the g-load.
            throttleProgram = new[] { 0f, 1f, 30f, 1f, 40f, .92f, 75f, .92f, 85f, 1f, 170f, 1f, 190f, .75f },
        },
        new()
        {
            // ULA Vulcan Centaur VC2S (2 solids, standard fairing), as flown
            // since 2024: 61.6 m, 5.4 m, ~560 t at liftoff, ~19 t to LEO.
            // Booster: 382 t gross, 28.6 t empty -> 353.4 t of methane/LOX at
            // O/F 3.6 (76.8 t + 276.6 t); two BE-4s (4.9 MN), throttled to
            // 90 % through Max Q. (The real ~5 min booster burn implies
            // deeper throttling than this BE-4 model needs; here it burns
            // out at ~T+3:50.)
            // Aero: two solids low down - near neutral.
            centerOfPressure = .45f, normalForceSlope = 3f,
            description = "ULA's Atlas V successor (2024): two methane BE-4s, solid boosters, and the hydrogen Centaur V.",
            name = "Vulcan Centaur", bodyModelKey = "Vulcan", engineId = "BE4", engineCount = 2,
            solidBoosterId = "GEM63XL", solidBoosterCount = 2,
            bodyDiameter = 5.4f, bodyHeight = 45f, noseHeight = 13f, engineHeight = 3.6f,
            hullColor = new Color(0.9f, 0.9f, 0.9f), dragCoefficient = 0.45f,
            fuelType = "Methane", fuelCapacity = 181.8f, fuelDiameter = 5.4f,
            oxidizerCapacity = 242.4f, oxidizerDiameter = 5.4f,
            // Booster 28.6 t + the 3.5 t fairing (jettisoned during the
            // booster burn).
            dryMass = 32100f,
            firstStageName = "Booster + solids",
            firstStageModelStages = new[] { "Booster" },
            firstStageDrops = new[]
            {
                new StageDrop { time = 100f, modelStages = new[] { "SRB_Left", "SRB_Right" }, solids = true },
                new StageDrop { time = 220f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 3500f },
            },
            upperStages = new[]
            {
                // Centaur V: two RL10C-X, 54 t of LH2/LOX, 7.1 t dry.
                new StageSpec
                {
                    name = "Centaur V", modelStages = new[] { "Centaur" },
                    engines = Engines("RL10CX", 2), fuelType = "LH2",
                    fuelMass = 7848f, oxidizerMass = 46152f, dryMass = 7100f, diameter = 5.4f,
                    separationDelay = 6f, ignitionDelay = 10f,
                },
            },
            payloadMass = 10000f,
            payloadMax = 19000f,      // VC2S to LEO
            throttleProgram = new[] { 0f, 1f, 35f, 1f, 45f, .9f, 75f, .9f, 85f, 1f },
        },
        new()
        {
            // Delta IV Heavy (final, RS-68A configuration), 2004-2024: three
            // 5.1 m hydrogen Common Booster Cores, 72 m, ~733 t, 28.8 t to
            // LEO. Each core: one RS-68A, ~202 t of LH2/LOX (O/F 6), 26.8 t
            // dry. One shared pool here (606 t: 86.6 t LH2 + 519.4 t LOX):
            // the side cores' engines shut down at T+3:56 and they drop; the
            // centre core burns on to MECO near T+5:30. (For real the
            // centre core throttles to ~58 % right after liftoff to save
            // its own propellant; the throttle program does that for all
            // three here, the same total.)
            // Aero: three cores, no fins - unstable.
            centerOfPressure = .65f, normalForceSlope = 2.6f,
            description = "Hydrogen heavy lifter, 2004-24: three orange RS-68A cores, the Delta Cryogenic Second Stage above.",
            name = "Delta IV Heavy", bodyModelKey = "DeltaIVHeavy", engineId = "RS68A", engineCount = 3,
            // One cylinder with the three cores' frontal area (61.9 m²).
            bodyDiameter = 8.88f, bodyHeight = 50f, noseHeight = 19f, engineHeight = 3f,
            hullColor = new Color(0.85f, 0.45f, 0.15f), dragCoefficient = 0.45f,
            fuelType = "LH2", fuelCapacity = 1222.3f, fuelDiameter = 8.88f,
            oxidizerCapacity = 455.2f, oxidizerDiameter = 8.88f,
            // Three cores 80.4 t + the 3.5 t fairing (jettisoned during the
            // centre core's burn).
            dryMass = 83900f,
            firstStageName = "Centre core + side boosters",
            firstStageModelStages = new[] { "CentreCore" },
            firstStageDrops = new[]
            {
                new StageDrop { time = 239f, modelStages = new[] { "Booster_Left", "Booster_Right" }, mass = 53600f },
                new StageDrop { time = 250f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 3500f },
            },
            upperStages = new[]
            {
                // DCSS (5 m): one RL10B-2, 27.2 t of LH2/LOX, 3.5 t dry.
                new StageSpec
                {
                    name = "DCSS", modelStages = new[] { "DCSS" },
                    engines = Engines("RL10B2", 1), fuelType = "LH2",
                    fuelMass = 3956f, oxidizerMass = 23264f, dryMass = 3490f, diameter = 5.1f,
                    separationDelay = 6f, ignitionDelay = 13f,
                },
            },
            payloadMass = 10000f,
            payloadMax = 28790f,      // to LEO
            // Centre core's RS-68A (socket 0) on the axis, the side cores'
            // 5.3 m either side (sockets 1-2).
            engineLayout = new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, -5.3f, 1f, 0f, 0f, 5.3f },
            engineCutoffs = new[] { 236f, 1f, 236f, 2f },
            throttleProgram = new[] { 0f, 1f, 30f, 1f, 40f, .78f, 236f, .78f, 240f, 1f },
        },
        new()
        {
            // Long March 8A (CZ-8A), first flown 11 Feb 2025 - the newest
            // Long March in regular service (12A, 12B and 10B are still
            // prototypes). 50.5 m, 371 t at liftoff. Published: engines,
            // diameters, liftoff mass. Estimated: the propellant split below,
            // fitted to the liftoff mass and a ~3.5 min first-stage burn.
            // Core 3.35 m with 2 YF-100 + two 2.25 m boosters with 1 YF-100
            // each, one shared pool of 305 t RP-1/LOX at O/F 2.6 (84.7 t +
            // 220.3 t); the boosters' engines shut down at T+2:47 when their
            // share (~68 t each) is gone and the boosters drop away.
            // Aero: two boosters, small fins - near neutral.
            centerOfPressure = .45f, normalForceSlope = 3f,
            description = "China's newest Long March in service (2025): a YF-100 kerosene core and two boosters, then a hydrogen upper stage.",
            name = "Long March 8A", bodyModelKey = "LongMarch8A", engineId = "YF100", engineCount = 4,
            // Physics flies one cylinder: 4.6 m has the frontal area of the
            // core and both boosters (16.8 m²).
            bodyDiameter = 4.6f, bodyHeight = 36.5f, noseHeight = 11f, engineHeight = 3f,
            hullColor = new Color(0.94f, 0.94f, 0.94f), dragCoefficient = 0.45f,
            fuelType = "RP-1", fuelCapacity = 104.6f, fuelDiameter = 4.6f,
            oxidizerCapacity = 193.1f, oxidizerDiameter = 4.6f,
            // Core 15 t dry + 2 boosters 6 t each.
            dryMass = 27000f,
            firstStageName = "Core + boosters",
            firstStageModelStages = new[] { "Core" },
            firstStageDrops = new[]
            {
                new StageDrop { time = 168f, modelStages = new[] { "Booster_Left", "Booster_Right" }, mass = 12000f },
            },
            upperStages = new[]
            {
                // Second stage: 3.35 m, 2 YF-75H, ~25 t of LH2/LOX (estimated),
                // ~9 min. Dry 4.5 t + the 2 t fairing until it's jettisoned.
                new StageSpec
                {
                    name = "Second stage", modelStages = new[] { "SecondStage" },
                    engines = Engines("YF75H", 2), fuelType = "LH2",
                    fuelMass = 3570f, oxidizerMass = 21430f, dryMass = 6500f, diameter = 3.35f,
                    separationDelay = 2f, ignitionDelay = 3f,
                    drops = new[] { new StageDrop { time = 20f, modelStages = new[] { "Fairing_A", "Fairing_B" }, mass = 2000f } },
                },
            },
            payloadMass = 7000f,      // to a 700 km sun-synchronous orbit
            payloadMax = 9800f,       // to LEO
            // Core's two engines either side of the axis (0.75 m), the
            // boosters' on the other axis (2.9 m out) - so sockets 2 and 3
            // are the boosters.
            engineLayout = new[] { 2f, .75f, 0f, 0f, 2f, 2.9f, 90f, 0f },
            engineCutoffs = new[] { 167f, 2f, 167f, 3f },
        },
    };
}
