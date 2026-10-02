# Builds backward locomotion loops from the forward ones in an animation FBX and exports them as a new FBX.
# usage: blender -b --python make_backward_clips.py -- input.fbx output.fbx "Source>New:lean|Source>New:lean"
#
# Backward walking / running is close to forward locomotion played in reverse, so every new clip is the
# source clip reversed in time. Two things are changed on top of that:
#   * the clip is shifted by half a cycle, so that the same foot is planted at the same normalized time as in
#     the forward clip (the two can then be blended while the character changes direction);
#   * the forward lean of the torso is taken out ("lean" is the correction in degrees, positive leans back)
#     and the neck / head are rotated the other way to keep the head level.
import bpy, sys, math
from mathutils import Matrix, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:]
inp, out = argv[0], argv[1]
jobs = []
for item in argv[2].split("|"):
    names, lean = item.split(":")
    source, new = names.split(">")
    jobs.append((source, new, float(lean)))

SPINE = ["spine_01", "spine_02", "spine_03"]
HEAD = ["neck_01", "Head"]
# Share of the torso correction that the neck and head give back.
HEAD_COUNTER = 0.9

if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
else:
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
scene = bpy.context.scene
scene.render.fps = 30
bpy.ops.import_scene.fbx(filepath=inp)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for o in list(bpy.data.objects):
    if o.type == 'MESH':
        bpy.data.objects.remove(o)


def find_action(name):
    # The FBX importer prefixes the take name with the object name(s).
    return next(a for a in bpy.data.actions if a.name.split("|")[-1] == name)


def fcurves_of(action):
    return action.layers[0].strips[0].channelbag(action.slots[0]).fcurves


def use_action(action):
    arm.animation_data.action = action
    arm.animation_data.action_slot = action.slots[0]


def rotation_curves(action, bone):
    path = 'pose.bones["%s"].rotation_quaternion' % bone
    curves = sorted((fc for fc in fcurves_of(action) if fc.data_path == path), key=lambda fc: fc.array_index)
    assert len(curves) == 4, bone
    return curves


for source, new_name, lean in jobs:
    src = find_action(source)
    start, end = int(src.frame_range[0]), int(src.frame_range[1])
    count = end - start
    assert count % 2 == 0, "%s needs an even number of frames for the half cycle shift" % source
    new = src.copy()
    new.name = new_name
    new.use_fake_user = True

    # Reverse in time and shift by half a cycle. Every frame of the library clips is keyed.
    for fc in fcurves_of(new):
        keys = fc.keyframe_points
        assert len(keys) == count + 1, (source, fc.data_path, len(keys))
        values = [k.co[1] for k in keys]
        for i, k in enumerate(keys):
            value = values[(count // 2 - i) % count]
            k.co[1] = value
            k.handle_left[1] = value
            k.handle_right[1] = value
        fc.update()

    if abs(lean) > 1e-3:
        # Rotate the spine about the side axis of the armature. The rotations of the whole chain share that
        # axis, so each bone's correction can be computed from the uncorrected pose.
        use_action(new)
        chain = [(b, math.radians(lean) / len(SPINE)) for b in SPINE]
        chain += [(b, -math.radians(lean) * HEAD_COUNTER / len(HEAD)) for b in HEAD]
        corrected = {b: [] for b, _ in chain}
        for i in range(count + 1):
            scene.frame_set(start + i)
            bpy.context.view_layer.update()
            for bone, angle in chain:
                pb = arm.pose.bones[bone]
                basis = pb.matrix_basis.to_3x3()
                parent_rest = pb.matrix.to_3x3() @ basis.inverted()
                local = parent_rest.inverted() @ Matrix.Rotation(angle, 3, 'X') @ parent_rest
                q = (local @ basis).to_quaternion()
                previous = corrected[bone][-1] if corrected[bone] else pb.rotation_quaternion
                if q.dot(previous) < 0.0:
                    q.negate()
                corrected[bone].append(q.copy())
        for bone, _ in chain:
            for fc in rotation_curves(new, bone):
                for i, k in enumerate(fc.keyframe_points):
                    value = corrected[bone][i][fc.array_index]
                    k.co[1] = value
                    k.handle_left[1] = value
                    k.handle_right[1] = value
                fc.update()
    print("BUILT", new_name, "from", source, "frames", count, "lean", lean)

# Keep the new clips and the keyed rest pose, which has to stay the first take (see convert_glb_to_fbx.py).
rest = find_action("A_TPose")
keep = {new_name for _, new_name, _ in jobs}
for a in list(bpy.data.actions):
    if a != rest and a.name not in keep:
        bpy.data.actions.remove(a)
rest.name = "A_TPose"
rest.use_fake_user = True
use_action(rest)
scene.frame_set(int(rest.frame_range[0]))
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.context.view_layer.update()
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, object_types={'ARMATURE'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    apply_scale_options='FBX_SCALE_UNITS', axis_forward='Z', axis_up='Y')
print("EXPORTED", out, sorted(a.name for a in bpy.data.actions))
