# Art conversion

The animation FBX files under `Assets/ALS/Art` were converted from the original GLB downloads with Blender
(5.2 was used). You only need this if you want to add clips or rebuild the files.

`convert_glb_to_fbx.py` imports a GLB animation library, keeps the listed actions, removes the unused `*_leaf`
end bones, adds a keyed rest pose (`A_TPose`) as the first take (Unity reads the default pose of an animated
FBX from its first take), optionally turns the rig around to face +Z, and exports an FBX.

```
blender -b --python convert_glb_to_fbx.py -- <input.glb> <output.fbx> "<Action A|Action B|...>" <flags>
```

Flags (comma separated): `mesh` keeps the skinned mesh, `flip` turns the rig 180 degrees, `fps30` imports at
30 fps instead of 24 (use the frame rate the clips were authored at).

Commands used for the files in this repository:

```
# Quaternius Universal Animation Library [Standard] -> mannequin + locomotion clips
blender -b --python convert_glb_to_fbx.py -- UAL1_Standard.glb Mannequin_UAL1.fbx "Idle_Loop|Walk_Loop|Jog_Fwd_Loop|Sprint_Loop|Crouch_Idle_Loop|Crouch_Fwd_Loop|Jump_Start|Jump_Loop|Jump_Land|Roll" mesh,fps30,flip

# Quaternius Universal Animation Library 2 [Standard] -> climb and get-up
blender -b --python convert_glb_to_fbx.py -- UAL2_Standard.glb Actions_UAL2.fbx "ClimbUp_1m|LayToIdle" fps30,flip

# Mesh2Motion -> strafes, walk backwards, push-up
blender -b --python convert_glb_to_fbx.py -- human-addon-animations.glb M2M_Addon.fbx "Strafe_left|Strafe_right|Walk_Backwards|Pushup" flip

# Mesh2Motion -> turn in place
blender -b --python convert_glb_to_fbx.py -- human-mocap-animations.glb M2M_Mocap.fbx "Turn_Left_90|Turn_Right_90|Turn_Left_180|Turn_Right_180" flip
```

After replacing an FBX, run **ALS > Rebuild Everything** in Unity so the import settings, Animator Controller,
character prefab and test scene are regenerated. Sources and licenses are listed in
`THIRD_PARTY_NOTICES.md`.
