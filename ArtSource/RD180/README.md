# RD-180 — first visual model

An original procedural visual approximation of the RD-180 engine for Strauss
Space. It has two hollow nozzle bells, a shared upper pump assembly, pale pipe
jackets, olive equipment housings, service lines and a dark support frame.
Small fittings, pipe routes and dimensions are approximate, not measured CAD.
This asset represents RD-180, not the previously discussed RD-182.

## Files

- `../../Assets/Models/RD180/RD180_Engine.fbx`: Unity model.
- `RD180_Engine.blend`: editable source and preview studio.
- `RD180_Engine_preview.png`: render of the actual mesh.
- `build_rd180.py`: standalone Blender 3.4+ generator.
- `validate_fbx.py`: FBX round-trip validation.
- `model_stats.json`, `validation.json`: generated measurements and checks.

## Unity placement

After Unity refreshes, drag the FBX from Project into a scene or prefab.
The model is approximately 3.59 m high and 3.1 m wide, authored in metres.
Blender +Z is exported as Unity +Y. `MountPoint` is at the upper attachment
plane. `ExhaustPoint_L` and `ExhaustPoint_R` mark the two nozzle exits.

The planet world in Strauss Space uses `PlanetBody.WorldUnitsPerMeter = 0.001`.
Apply that scale once when placing the model directly in that world; account
for any existing scale on the parent before scaling the engine itself.

The FBX contains mesh geometry, named solid-color materials and attachment
markers. No textures, preview lights, floor or camera are exported. Material
metallic/roughness settings may need adjustment after import into Unity.
There are no flight scripts, colliders, animated gimbals or exhaust effects.
The initial mesh has 78,488 triangles and seven material groups.
Existing scenes and the F-1 model are unchanged.

## Rebuild

From the project root:

```sh
blender --background --threads 8 --python ArtSource/RD180/build_rd180.py
blender --background --threads 2 --python ArtSource/RD180/validate_fbx.py
```

Regeneration replaces only this model's generated files. Keep the Blender
source outside Assets to avoid a second automatic Blender import in Unity.

## References

NASA describes RD-180 as one engine with two thrust chambers:
https://science.nasa.gov/blogs/insight/2018/05/05/workhorse-rocket-to-launch-nasas-insight-spacecraft-on-its-mission-to-mars/

Visual reference: the RD-180 photograph on Purdue's propulsion page:
https://engineering.purdue.edu/~propulsi/propulsion/rockets/liquids/rd180.html

The reference photograph shows the engine inverted on a display stand. This
asset is oriented with nozzles downward. The display stand is not included.
The original photograph is not redistributed with this model.
