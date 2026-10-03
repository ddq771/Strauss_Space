# Third-party rocket models

Body models under `Assets/Models/<Key>/<Key>_Body.fbx` that were not built by
the `build_*.py` scripts. Each was converted with `import_body_model.py`
(upright, scaled to the preset's total height, centred).

| Key | Source | Author | License |
|---|---|---|---|
| SaturnV | [NASA 3D Resources - Saturn V](https://github.com/nasa/NASA-3D-Resources/tree/master/3D%20Models/Saturn%20V) | NASA | Public domain (US Government work) |
| SpaceShuttle | [NASA 3D Resources - Space Shuttle (A)](https://github.com/nasa/NASA-3D-Resources/tree/master/3D%20Models/Space%20Shuttle%20(A)) | NASA | Public domain (US Government work) |

NASA's `.glb` files are Draco-compressed; Ubuntu's Blender package can't
decode Draco, so they were decompressed (DracoPy + pygltflib) before import.
