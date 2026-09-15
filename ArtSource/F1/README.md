# F-1 engine — first visual model

An original procedural model inspired by Rocketdyne F-1, made for Strauss Space.
This is an approximate game/visualization asset, not a dimensionally verified
replica. The pipe routes, fittings, cooling ribs and pump details are stylized.
There are no simulated engine internals or operating specifications.

## Files

- `../../Assets/Models/F1/F1_Engine.fbx`: mesh asset for Unity.
- `F1_Engine.blend`: editable Blender source with preview camera and lights.
- `F1_Engine_preview.png`: rendered preview of the actual mesh.
- `build_f1.py`: reproducible geometry and FBX generator for Blender 3.4+.
- `validate_fbx.py`: reimports the FBX and checks size, materials and markers.
- `model_stats.json`, `validation.json`: generated reports.

## Unity

The FBX is already inside the project's Assets folder. After Unity refreshes,
drag `Assets/Models/F1/F1_Engine.fbx` from Project into a scene or prefab.
The existing planet scene is not changed by this asset.

The asset uses metres, approximately 5.63 m high and 3.80 m across the exit lip.
Export conversion is Blender +Z up to Unity +Y up. The origin and `MountPoint`
are at the upper mounting plate. `ExhaustPoint` is at the nozzle exit.
The five mesh groups use named solid-color materials; there are no external
textures. Metallic/roughness appearance can need adjustment in Unity's material
importer because Blender's preview lighting is not part of the FBX.

Strauss Space's `PlanetBody.WorldUnitsPerMeter` is currently 0.001. When placing
this metre-based model directly in that scaled planet world, apply that factor
once to the model or its parent; do not apply it twice under a scaled parent.

This first version has about 106,000 triangles and five material groups.
It is intended for close inspection; a separate LOD can be built for distant
views or large engine clusters. There are no colliders, flame effects or flight
scripts attached yet.

## Regenerate

From the Strauss Space project folder:

```sh
blender --background --threads 6 --python ArtSource/F1/build_f1.py
blender --background --threads 6 --python ArtSource/F1/validate_fbx.py
```

The preview studio is excluded from FBX export. Rebuilding replaces only this
asset's generated files. The .blend source stays outside Assets so Unity does
not run a second Blender import.

## Reference context

NASA describes the F-1 as the engine used on Saturn V:
https://www.nasa.gov/technology/space-travel-tech/worlds-most-powerful-engine-blazes-path-for-space-launch-system-advanced-propulsion/

The model was procedurally authored, not extracted from NASA CAD or a scan.
Its dimensions and routing should not be treated as sourced engineering data.
