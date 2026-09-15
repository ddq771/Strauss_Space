# Raptor 2 sea-level — visual model for Strauss Space

Original procedural geometry, with approximate external fittings, proportions
and pipe routing. This is a visual game asset, not a measured replica.

## Deliverables

- `../../Assets/Models/Raptor2/Raptor2_Engine.fbx`: Unity asset.
- `Raptor2_Engine.blend`: editable Blender source with preview studio.
- `Raptor2_Engine_preview.png`: render of the exported model's source geometry.
- `build_raptor2.py`: model generator, using `../engine_asset.py`.
- `model_stats.json`: polygon counts, material counts and metre-based bounds.
- `validation.json`: FBX reimport verification.

The reference identifies the engine family and context:
https://new.spacex.com/vehicles/starship

The RS-25 model also follows the overall layout in NASA's public engine
illustration. Merlin 1D and Raptor 2 are artistic approximations of the
sea-level variants. No external photographs, textures or manufacturer meshes
are redistributed. Hidden geometry and exact component routing are not verified.

## Unity

Refresh Unity, then drag `Raptor2_Engine.fbx` from its Project folder into a
scene or prefab. These files do not alter existing scenes or other models.

Units are metres. Blender +Z exports as Unity +Y. `MountPoint` is at the upper
attachment plane and `ExhaustPoint` is at the nozzle exit. The latter's axis
points downward. The named material groups use solid colors; metallic and
roughness appearance can require adjustment in Unity's material importer.

Strauss Space uses `PlanetBody.WorldUnitsPerMeter = 0.001` for its planet world.
Apply that factor once on the asset or its parent when placing it in that world.
Do not apply it twice if the parent already supplies the conversion.

No colliders, LODs, animated gimbals, flight behavior or flame effects are attached.
Camera, lights and floor are present only in the Blender preview collection and
are excluded from FBX export. FBX was checked by reimporting into Blender;
Unity Editor import is not tested.

## Regenerate

From the project root, with Blender 3.4 or later:

```sh
blender --background --threads 8 --python ArtSource/Raptor2/build_raptor2.py
```

Keep `ArtSource/engine_asset.py` beside the engine source folders. The command
rebuilds the FBX and Blender file, renders the preview, then reimports the FBX to
verify dimensions, mesh/material presence, inward nozzle normals and markers.
