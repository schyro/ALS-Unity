# Advanced Locomotion System - Unity

A third-person character locomotion prototype for Unity, inspired by
[ALS-Community](https://github.com/PanicPetal/ALS-Community) (Advanced Locomotion System V4 for Unreal Engine).
The movement, rotation, camera, mantle and animation logic were re-written for Unity after studying the
ALS-Community source. No Unreal content is used, and the project is not affiliated with ALS or Epic Games.

## Features

- Walk, run, sprint and crouch with ALS-style movement curves
- Looking-direction, velocity-direction and aiming rotation modes, with turn in place
- Speed-matched locomotion animation with foot IK (slopes, stairs, uneven ground)
- Jump, fall and landing, with roll or ragdoll on hard landings
- Mantle: low, high and in-air ledge grab
- Ragdoll with get-up from both sides
- Third-person camera with shoulder switching and wall collision
- Test scene with ramps, stairs, climbing blocks, a crouch tunnel and drop platforms

## Requirements

- Unity **6000.3.12f1** (Unity 6.3) with the Universal Render Pipeline
- Input System 1.14
- [Git LFS](https://git-lfs.com) (model files are stored with LFS)

## Getting started

```bash
git lfs install
git clone https://github.com/schyro/ALS-Unity.git
```

1. Open the folder with Unity Hub (6000.3.12f1).
2. Open `Assets/ALS/Scenes/ALS_TestScene.unity` and press Play.

The scene, prefab and Animator Controller are generated from code. After changing something, rebuild them with
**ALS > Rebuild Everything**.

## Controls

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move | `W A S D` | Left stick |
| Camera | Mouse | Right stick |
| Jump / mantle / get up | `Space` | A |
| Sprint (hold) | `Left Shift` | Left stick press |
| Toggle walk / run | `Left Ctrl` | D-pad down |
| Crouch (double tap: roll) | `C` or `Left Alt` | B |
| Roll | `Q` | X |
| Ragdoll on / off | `X` | Y |
| Aim (hold) | Right mouse button | Left trigger |
| Rotation mode | `1` / `2` | D-pad left / right |
| Switch shoulder | `V` | Right stick press |
| Reset | `R` | Select |

`F1` hides the on-screen panels, `Esc` releases the mouse.

## Project layout

```
Assets/ALS/
  Scripts/Runtime   Character, animation, mantle, ragdoll, camera, input, HUD
  Scripts/Editor    Animator, prefab and scene builders (ALS menu)
  Art               CC0 character and animations, materials
  Animation         Generated Animator Controller
  Prefabs           ALSCharacter prefab
  Scenes            ALS_TestScene
Tools/blender       Blender scripts for converting and preparing clips
```

## Known limitations

- Single-player only; no AI, weapons / overlay states, first-person camera or footstep effects.
- Animations come from CC0 libraries, not the original ALS set. Backward clips are the forward clips reversed,
  and lean and the landing dip are procedural.
- Foot IK is simple (no foot locking), and some crouch and side-step clips still slide slightly.
- The high mantle is a jump plus a climb clip, without hand IK.
- Gamepad bindings and standalone builds are untested.

## License and credits

- Code: MIT ([LICENSE](LICENSE)). Parts adapted from ALS-Community are listed in
  [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- Character and animations: Quaternius *Universal Animation Library 1 / 2* and Mesh2Motion, all CC0 1.0.
  Sources are listed in the same notices file.
- The Unreal content of ALS-Community is under the Unreal Engine license and is not part of this project.
