# ALS-Unity

An **independent Unity prototype built with [ALS-Community](https://github.com/PanicPetal/ALS-Community) as its
reference** (the C++ community version of Advanced Locomotion System V4 for Unreal Engine). The movement,
character rotation, camera, mantle and animation logic were re-written for Unity after studying the
ALS-Community source code; no Unreal content (mannequin, animations, blueprints) is used. It is not an official
port and is not affiliated with ALS or Epic Games.

It contains a single-player, playable third-person character and a small test scene.

## Unity version

- Unity **6000.3.12f1** (Unity 6.3), Universal Render Pipeline
- Input System 1.14 (Active Input Handling: new Input System only)
- No additional packages or Asset Store content are required.

## Setup

Model and texture files are stored with Git LFS.

```bash
git lfs install
```

```bash
git clone https://github.com/schyro/ALS-Unity.git
```

Open the folder with Unity Hub in version 6000.3.12f1. The first import can take a few minutes.

The `com.unity.ai.assistant`, `com.unity.ai.inference` and `com.unity.pipeline` packages in
`Packages/manifest.json` were only used for editor automation during development; the prototype does not depend
on them and they can be removed if they cause trouble.

## Running

1. Open `Assets/ALS/Scenes/ALS_TestScene.unity` (the only scene in the Build Settings).
2. Press Play. The mouse is captured when you click the Game view; `Esc` releases it.

The scene, the prefab and the Animator Controller are generated from code. After changing something, rebuild
all of them from the menu with **ALS > Rebuild Everything** (the individual steps are under `ALS > Build Steps`).

## Controls

The controls are also shown on screen while playing (`F1` hides the panels).

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move (camera relative) | `W A S D` | Left stick |
| Camera | Mouse | Right stick |
| Jump / mantle / get up from ragdoll | `Space` | A (south button) |
| Sprint (hold) | `Left Shift` | Left stick press |
| Toggle walk / run | `Left Ctrl` | D-pad down |
| Crouch (double tap: roll) | `C` or `Left Alt` | B (east button) |
| Roll | `Q` | X (west button) |
| Ragdoll on / off | `X` | Y (north button) |
| Aim (hold) | Right mouse button | Left trigger |
| Rotation mode: velocity direction / looking direction | `1` / `2` | D-pad left / right |
| Switch camera shoulder | `V` | Right stick press |
| Reset to start | `R` | Select |

Mantle: hold a direction toward the obstacle and press `Space`. While in the air, suitable ledges are grabbed
automatically as long as a direction is held.

## Features

- **Gaits:** walk (1.5 m/s), run (4 m/s), sprint (6.5 m/s) and crouch (0.9 / 1.5 m/s). The "desired / allowed /
  actual" gait split works as in ALS; acceleration, braking, friction and rotation rate come from ALS' "Normal"
  movement curves.
- **Rotation modes:**
  - *Looking direction* (default): the character keeps facing where the camera looks. Moving back makes it
    backpedal instead of turning around; moving sideways or diagonally turns the hips into the movement while
    the spine and head stay turned toward the camera. Standing still, it turns in place once the camera has
    moved more than 45°.
  - *Velocity direction*: the character turns smoothly into the direction it moves in.
  - *Aiming*: the whole body faces the camera and the character walks, side-steps and backs up.
- **Speed matched animation:** every locomotion clip is measured for the ground speed it was animated for. At
  run time the playback rate and the stride length (through the foot IK) are adjusted together so that the feet
  cover exactly the distance the character moves, at any speed and while accelerating. The walk / jog / sprint,
  forward / backward and side-step clips live in one blend and are kept in step, so changing speed or direction
  never restarts a clip.
- **Jump, fall, land:** limited air control, hand-over to the fall loop at the apex, landing animation and a
  landing dip. A hard landing (≥ 7 m/s) with a direction held becomes a roll, a very hard one (> 10 m/s) a
  ragdoll.
- **Roll:** `Q` or a double tap on crouch; the capsule shrinks while rolling, the direction can be steered
  slowly, and rolling off a ledge turns into a ragdoll.
- **Mantle:** ALS' trace logic (forward capsule trace, downward sphere trace, room check). Low mantle for
  0.4–1.25 m, high mantle for 1.25–2.5 m (jump + climb), ledge grab in the air (up to about 3.3 m with a jump).
- **Camera:** mouse-driven third-person camera with per-axis pivot lag, shoulder switching, settings that blend
  with the state (run, sprint, crouch, aim, ragdoll) and wall collision.
- **Foot IK:** the feet follow slopes, stairs and uneven ground, and the pelvis drops toward the lower foot.
- **Ragdoll:** built at run time from the Humanoid skeleton; separate get-ups for face up and face down, and a
  smooth blend from the ragdoll pose back into animation.
- **Animation:** lean from the acceleration, upper body and head follow the camera, and all state changes are
  cross-faded from code.
- **Test scene:** flat ground, 10°–50° ramps (50° is not walkable), two staircases, 0.5–4 m climbing blocks,
  uneven ground for the foot IK, a crouch tunnel and drop platforms (3 m and 6.5 m).

## Project layout

```
Assets/ALS/
  Scripts/Runtime   Character, animation driver, mantle, ragdoll, camera, input, HUD
  Scripts/Editor    Import settings and the Animator, prefab and scene builders (ALS menu)
  Art               CC0 mannequin and animations, generated materials, grid texture
  Animation         Generated Animator Controller
  Prefabs           ALSCharacter prefab
  Scenes            ALS_TestScene
Tools/blender       Blender scripts: GLB to FBX conversion, backward locomotion clips
```

## How it was verified

The prototype was run in Play Mode in the Unity editor with scripted input (through the character API and
synthetic keyboard / mouse events), and checked from state logs and screenshots: gaits, direction changes,
jump / land, crouch and the tunnel, roll, mantle at every block height, ramps, stairs, foot IK on uneven
ground, the 3 m and 6.5 m drops, ragdoll and get-up from both sides, the looking direction / aiming modes and
turn in place.

The speed matching was measured rather than judged by eye: a Play Mode probe recorded how fast the ball of the
supporting foot moves over the ground (0 would be a perfectly planted foot) while the character moves at a
steady speed on flat ground. Median slip along the direction of travel:

| Movement | Speed | Playback rate | Stride scale | Foot slip |
|---|---|---|---|---|
| Walk forward / backward | 1.5 / 1.2 m/s | 1.24 / 1.11 | 1.24 / 1.11 | 0.03 m/s (2%) |
| Run forward (also sideways and in velocity mode) | 4.0 m/s | 0.96 | 0.70 | 0.11 m/s (3%) |
| Run backward | 3.2 m/s | 0.94 | 0.57 | 0.20 m/s (6%) |
| Sprint | 6.5 m/s | 0.97 | 0.76 | 0.28 m/s (4%) |
| Crouch forward / backward | 1.5 / 1.2 m/s | 1.47 / 1.29 | 1.40 / 1.29 | 0.25 / 0.17 m/s (17% / 14%) |
| Aim, side step | 1.2 m/s | 1.32 | 1.25 | 0.43 m/s (36%) |

A 25 second stress run with random direction changes, jumps, crouching, sprinting, aiming and a rotating camera
finished without errors or invalid values.

Not verified:

- Movement feel and animation smoothness were not judged by a person playing in real time; the tuning rests on
  the ALS values, the measurements above and frame-by-frame inspection.
- No real gamepad was used (the bindings exist but are untested).
- No standalone build was made or run.

## Known limitations

- No multiplayer, AI, overlay states (weapons, boxes, ...), first-person camera or footstep / effect systems.
- The animations are not the ALS set but come from CC0 libraries. There are no pivot, start / stop transition
  or additive lean clips; lean and the landing dip are procedural.
- The library has no backward or sideways run. The backward walk / jog / crouch loops are the forward loops
  reversed in time with the torso lean taken out (`Tools/blender/make_backward_clips.py`). Running sideways is
  done by turning the hips into the movement, so switching between a forward and a backward diagonal makes the
  character swing around. A real side-step clip only exists at walking pace and is used while aiming, where
  sideways movement is limited to 1.2 m/s.
- The speed matching can only be as even as the clips. The crouch walk and the side step move their planted
  feet at a varying speed, so those feet still slide within each step even though the average is matched (see
  the table above).
- The jog and sprint clips are animated for much faster movement than the gait speeds (about 5.9 and 8.8 m/s),
  so they play with a shortened stride. Their bounce is not scaled down with it.
- There is no dedicated high-mantle clip: it is a jump followed by the 1 m climb clip, and the hands may not
  land exactly on the ledge (no hand IK).
- The face-down get-up is assembled from a push-up clip and standing up from a crouch.
- The foot IK is simple (no foot locking). On stairs the capsule briefly catches on the steps, which costs a
  little speed (about 5%).
- Moving platforms and interaction with physics objects are not handled.

## Sources and license

- Code: MIT ([LICENSE](LICENSE)). The parts adapted from the ALS-Community source code (MIT) and the original
  copyright notice are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- Character and animations: Quaternius *Universal Animation Library 1 / 2* and Mesh2Motion, all **CC0 1.0**.
  Source addresses, versions and the conversions that were applied are listed in the same file.
- The Unreal content in the ALS-Community repository (mannequin, animations, curves) is distributed under the
  Unreal Engine license only and is therefore not part of this project.
