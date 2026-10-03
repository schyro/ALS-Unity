# Converts a glTF animation library to FBX for Unity, keeping only the listed actions.
# usage: blender -b --python convert.py -- input.glb output.fbx "ActionA|ActionB" [flags: mesh,flip,fps30]
import bpy, sys
argv = sys.argv[sys.argv.index("--") + 1:]
inp, out = argv[0], argv[1]
keep = set(argv[2].split("|")) if len(argv) > 2 and argv[2] else None
with_mesh = len(argv) > 3 and "mesh" in argv[3]
turn_around = len(argv) > 3 and "flip" in argv[3]
bpy.ops.wm.read_factory_settings(use_empty=True)
# Import at the frame rate the clips were authored at so the bake below does not resample them.
bpy.context.scene.render.fps = 30 if (len(argv) > 3 and "fps30" in argv[3]) else 24
bpy.ops.import_scene.gltf(filepath=inp)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
names = sorted(a.name for a in bpy.data.actions)
print("ACTIONS", len(names), "FPS", bpy.context.scene.render.fps)
if keep is not None:
    print("MISSING", keep - set(names))
    for a in list(bpy.data.actions):
        if a.name not in keep:
            bpy.data.actions.remove(a)
# Only export plain actions, and store the rest pose (not a frame of some clip) as the default pose.
if arm.animation_data:
    for tr in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(tr)
    arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.location = (0.0, 0.0, 0.0)
    pb.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
    pb.rotation_euler = (0.0, 0.0, 0.0)
    pb.scale = (1.0, 1.0, 1.0)
for a in bpy.data.actions:
    a.use_fake_user = True
# Unity reads the default pose of an animated FBX from its first take, and the exporter leaves the armature
# in the last baked pose unless an active action restores it. A keyed rest pose named to sort first covers both.
for a in list(bpy.data.actions):
    if a.name == "A_TPose":
        bpy.data.actions.remove(a)
rest = bpy.data.actions.new("A_TPose")
arm.animation_data_create()
arm.animation_data.action = rest
for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'
    pb.keyframe_insert("location", frame=0)
    pb.keyframe_insert("rotation_quaternion", frame=0)
    pb.keyframe_insert("scale", frame=0)
    pb.keyframe_insert("location", frame=10)
    pb.keyframe_insert("rotation_quaternion", frame=10)
    pb.keyframe_insert("scale", frame=10)
bpy.context.scene.frame_set(0)
if not with_mesh:
    for o in list(bpy.data.objects):
        if o.type == 'MESH':
            bpy.data.objects.remove(o)
else:
    # drop helper meshes that are not skinned to the armature
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and not any(m.type == 'ARMATURE' for m in o.modifiers):
            print("REMOVE MESH", o.name)
            bpy.data.objects.remove(o)
print("OBJECTS", [(o.name, o.type) for o in bpy.data.objects], "MATERIALS", [m.name for m in bpy.data.materials])
# The "*_leaf" end bones carry no skin weights and only confuse the Humanoid auto-mapping (toes, head).
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
leaf = [b.name for b in arm.data.edit_bones if "_leaf" in b.name]
for name in leaf:
    arm.data.edit_bones.remove(arm.data.edit_bones[name])
bpy.ops.object.mode_set(mode='OBJECT')
print("REMOVED LEAF BONES", len(leaf), "REMAINING", len(arm.data.bones))
# The exporter skips actions with curves it cannot resolve, so drop the curves of the removed bones too.
removed_curves = 0
for act in bpy.data.actions:
    bags = []
    if hasattr(act, "layers"):
        for layer in act.layers:
            for strip in layer.strips:
                for slot in act.slots:
                    bag = strip.channelbag(slot)
                    if bag is not None:
                        bags.append(bag.fcurves)
    elif hasattr(act, "fcurves"):
        bags.append(act.fcurves)
    for fcurves in bags:
        for fc in [fc for fc in fcurves if "_leaf" in fc.data_path]:
            fcurves.remove(fc)
            removed_curves += 1
print("REMOVED LEAF CURVES", removed_curves)

if turn_around:
    # The source faces the other way: turn the whole rig around and bake that into the rest pose.
    import math
    bpy.ops.object.select_all(action='DESELECT')
    for o in bpy.data.objects:
        if o.parent is None:
            o.rotation_mode = 'XYZ'
            o.rotation_euler[2] += math.pi
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
bpy.context.view_layer.update()
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    apply_scale_options='FBX_SCALE_UNITS', axis_forward='Z', axis_up='Y', mesh_smooth_type='OFF',
    path_mode='COPY', embed_textures=False)
print("EXPORTED", out)
