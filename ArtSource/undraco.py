"""Decode KHR_draco_mesh_compression primitives in a .glb so Blender builds
without the Draco library can import it. Usage: undraco.py in.glb out.glb"""
import sys
import numpy as np
import DracoPy
import pygltflib as g

src, dst = sys.argv[1], sys.argv[2]
gl = g.GLTF2().load(src)
blob = bytearray(gl.binary_blob())
EXT = 'KHR_draco_mesh_compression'


def add(arr, target, ctype, typ, minmax=False):
    global blob
    while len(blob) % 4: blob.append(0)
    data = np.ascontiguousarray(arr).tobytes()
    gl.bufferViews.append(g.BufferView(buffer=0, byteOffset=len(blob), byteLength=len(data), target=target))
    blob.extend(data)
    acc = g.Accessor(bufferView=len(gl.bufferViews) - 1, componentType=ctype,
                     count=len(arr), type=typ)
    if minmax:
        acc.min = arr.min(axis=0).tolist(); acc.max = arr.max(axis=0).tolist()
    gl.accessors.append(acc)
    return len(gl.accessors) - 1


for mesh in gl.meshes:
    for p in mesh.primitives:
        ext = (p.extensions or {}).get(EXT)
        if not ext: continue
        bv = gl.bufferViews[ext['bufferView']]
        raw = bytes(blob[(bv.byteOffset or 0):(bv.byteOffset or 0) + bv.byteLength])
        m = DracoPy.decode(raw)
        pts = np.asarray(m.points, dtype=np.float32).reshape(-1, 3)
        faces = np.asarray(m.faces, dtype=np.uint32).reshape(-1)
        p.attributes.POSITION = add(pts, g.ARRAY_BUFFER, g.FLOAT, g.VEC3, True)
        if 'NORMAL' in ext['attributes'] and m.normals is not None and len(m.normals):
            p.attributes.NORMAL = add(np.asarray(m.normals, np.float32).reshape(-1, 3), g.ARRAY_BUFFER, g.FLOAT, g.VEC3)
        else:
            p.attributes.NORMAL = None
        if 'TEXCOORD_0' in ext['attributes'] and m.tex_coord is not None and len(m.tex_coord):
            p.attributes.TEXCOORD_0 = add(np.asarray(m.tex_coord, np.float32).reshape(-1, 2), g.ARRAY_BUFFER, g.FLOAT, g.VEC2)
        else:
            p.attributes.TEXCOORD_0 = None
        for a in ('TANGENT', 'TEXCOORD_1', 'COLOR_0'):
            if getattr(p.attributes, a, None) is not None: setattr(p.attributes, a, None)
        p.indices = add(faces, g.ELEMENT_ARRAY_BUFFER, g.UNSIGNED_INT, g.SCALAR)
        del p.extensions[EXT]

gl.extensionsUsed = [e for e in (gl.extensionsUsed or []) if e != EXT]
gl.extensionsRequired = [e for e in (gl.extensionsRequired or []) if e != EXT]
gl.buffers[0].byteLength = len(blob)
gl.set_binary_blob(bytes(blob))
gl.save_binary(dst)
print('UNDRACO_OK', dst)
