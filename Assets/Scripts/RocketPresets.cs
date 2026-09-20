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
/// - The body is one procedurally-tapered cylinder (Rocket.ConfigureShape) -
///   it gets the real diameter/height, but not the real silhouette. This
///   is a poor match for Vostok (which has 4 external strap-on boosters,
///   not modeled separately) and Space Shuttle (which is a side-mounted
///   orbiter and boosters, not a stacked tube at all) - both are rendered
///   as a stand-in tank-diameter stack, not their actual shape.
/// - Starship is modeled as one 39-engine, non-separating vehicle. There is
///   no staging/separation system in this project - the real two-stage
///   liftoff-then-separate-then-relight sequence is not simulated.
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
    }

    public static readonly Preset[] All =
    {
        new()
        {
            // 9 Merlin 1D in an octaweb (1 centre + 8 ring - the real
            // layout, and what RocketAssemblyController's ring packing
            // produces for a count of 9 with no special-casing needed).
            name = "Falcon 9", bodyModelKey = "Falcon9", engineId = "Merlin1D", engineCount = 9,
            bodyDiameter = 3.7f, bodyHeight = 70f, noseHeight = 13f, engineHeight = 4f,
            hullColor = new Color(0.94f, 0.94f, 0.95f), dragCoefficient = 0.3f,
            fuelType = "RP-1", fuelCapacity = 152.5f, fuelDiameter = 3.7f,
            oxidizerCapacity = 251.9f, oxidizerDiameter = 3.7f,
        },
        new()
        {
            // Super Heavy (33) + Ship (6) combined into one 39-engine
            // cluster - see the type doc comment on why there is no
            // separation between the two.
            name = "Starship", bodyModelKey = "Starship", engineId = "Raptor2", engineCount = 39,
            bodyDiameter = 9f, bodyHeight = 100f, noseHeight = 18f, engineHeight = 8f,
            hullColor = new Color(0.74f, 0.75f, 0.77f), dragCoefficient = 0.55f,
            fuelType = "Methane", fuelCapacity = 2366f, fuelDiameter = 9f,
            oxidizerCapacity = 3155f, oxidizerDiameter = 9f,
        },
        new()
        {
            // 1 RD-108 core + 4 RD-107 boosters, all represented with the
            // same RD107 catalog entry (see EnginePerformance.Reference) -
            // the two are similar engines and this project only models one
            // of them. The boosters themselves are not modeled as separate
            // bodies; this is the core stage's diameter only.
            name = "Vostok", bodyModelKey = "Vostok", engineId = "RD107", engineCount = 5,
            bodyDiameter = 2.99f, bodyHeight = 28f, noseHeight = 8f, engineHeight = 3f,
            hullColor = new Color(0.52f, 0.54f, 0.48f), dragCoefficient = 0.45f,
            fuelType = "RP-1", fuelCapacity = 85.7f, fuelDiameter = 2.99f,
            oxidizerCapacity = 158.2f, oxidizerDiameter = 2.99f,
        },
        new()
        {
            // S-IC first stage: 5 F-1 engines, real diameter and propellant
            // load (upper stages/Apollo stack not modeled separately).
            name = "Saturn V", bodyModelKey = "SaturnV", engineId = "F1", engineCount = 5,
            bodyDiameter = 10.1f, bodyHeight = 95f, noseHeight = 13f, engineHeight = 3f,
            hullColor = new Color(0.92f, 0.92f, 0.89f), dragCoefficient = 0.4f,
            fuelType = "RP-1", fuelCapacity = 950.6f, fuelDiameter = 10.1f,
            oxidizerCapacity = 1149f, oxidizerDiameter = 10.1f,
        },
        new()
        {
            // 3 RS-25, External Tank diameter/height/color - the real
            // vehicle is a side-mounted orbiter and 2 SRBs around this
            // tank, not a stack with the engines at the bottom of it; see
            // the type doc comment.
            name = "Space Shuttle", bodyModelKey = "SpaceShuttle", engineId = "RS25", engineCount = 3,
            bodyDiameter = 8.4f, bodyHeight = 42f, noseHeight = 10f, engineHeight = 4f,
            hullColor = new Color(0.72f, 0.42f, 0.25f), dragCoefficient = 0.5f,
            fuelType = "LH2", fuelCapacity = 1499.9f, fuelDiameter = 8.4f,
            oxidizerCapacity = 551.6f, oxidizerDiameter = 8.4f,
        },
    };
}
