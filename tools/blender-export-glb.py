"""Exports each top-level object in a .blend file, with its children, as <Name>.glb.

A top-level object is one with no parent, other than cameras and lights: usually an
empty with meshes under it, or a lone mesh. Each is exported moved to the origin, with
modifiers applied.

The output folder comes from an `export_dir` custom property, on the object's collection
if set there, otherwise on the scene. Paths starting with // are relative to the .blend
file. Set it once per file in Blender's Python console, then save:

    bpy.context.scene["export_dir"] = "//../ShapeLand.Unity/Assets/Models/Shapes"

Run it from Blender's Text Editor (open this file, then Run Script), or headless:

    blender -b shapeland/art/shapes.blend --python tools/blender-export-glb.py
"""
import os

import bpy

SKIPPED_TYPES = {'CAMERA', 'LIGHT'}


def export_dir(obj, scene):
    for coll in obj.users_collection:
        if "export_dir" in coll:
            return coll["export_dir"]
    return scene.get("export_dir")


def main():
    scene = bpy.context.scene
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    selected = list(bpy.context.selected_objects)
    active = bpy.context.view_layer.objects.active

    roots = [o for o in scene.objects if o.parent is None and o.type not in SKIPPED_TYPES]
    if not roots:
        raise RuntimeError("Nothing to export: no top-level objects other than cameras and lights")
    missing = [o.name for o in roots if not export_dir(o, scene)]
    if missing:
        raise RuntimeError("No export_dir on the scene or collection for: " + ", ".join(missing))

    try:
        for root in roots:
            out = bpy.path.abspath(export_dir(root, scene))
            os.makedirs(out, exist_ok=True)
            bpy.ops.object.select_all(action='DESELECT')
            root.select_set(True)
            for child in root.children_recursive:
                child.select_set(True)
            loc = root.location.copy()
            root.location = (0, 0, 0)
            bpy.context.view_layer.update()
            try:
                path = os.path.join(out, root.name + ".glb")
                bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_apply=True)
                print("Exported", path)
            finally:
                root.location = loc
    finally:
        bpy.ops.object.select_all(action='DESELECT')
        for o in selected:
            o.select_set(True)
        bpy.context.view_layer.objects.active = active


main()
