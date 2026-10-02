using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ALSUnity.EditorTools
{
    /// <summary>
    /// Generates the character's Animator Controller. The controller only holds states (no transitions):
    /// ALSCharacterAnimation picks the state and cross-fades from code.
    /// </summary>
    public static class ALSAnimatorBuilder
    {
        // Blend tree thresholds (m/s) for the standing locomotion clips and the ground speed each clip covers at
        // playback rate 1. The time scale of every clip is threshold / natural speed, so feet do not slide at
        // the threshold speeds while each gait still shows mostly "its" clip.
        public const float WalkThreshold = 1.2f;
        public const float WalkNaturalSpeed = 0.975f;
        public const float JogThreshold = 4.4f;
        public const float JogNaturalSpeed = 5.36f;
        public const float SprintThreshold = 6.8f;
        public const float SprintNaturalSpeed = 8.25f;

        // Camera relative strafe blend: every clip is time scaled to cover StrafeTreeSpeed m/s at rate 1.
        public const float StrafeTreeSpeed = 1.2f;
        public const float BackwardNaturalSpeed = 1f;
        public const float StrafeNaturalSpeed = 0.73f;

        [MenuItem("ALS/Build Steps/2. Build Animator Controller")]
        public static AnimatorController Build()
        {
            System.IO.Directory.CreateDirectory(ALSAssetPaths.AnimationFolder);
            AssetDatabase.DeleteAsset(ALSAssetPaths.AnimatorController);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ALSAssetPaths.AnimatorController);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AddFloat(controller, "MoveRate", 1f);
            AddFloat(controller, "CrouchRate", 1f);
            AddFloat(controller, "StrafeRate", 1f);
            AddFloat(controller, "ActionRate", 1f);
            AddFloat(controller, "DirectionX", 0f);
            AddFloat(controller, "DirectionY", 1f);

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].name = "Base";
            layers[0].iKPass = true;
            controller.layers = layers;
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState idle = AddState(machine, "Idle", Clip("Idle_Loop"), 0, 0);
            machine.defaultState = idle;

            // Standing locomotion: walk -> jog -> sprint over the character speed.
            AnimatorState move = controller.CreateBlendTreeInController("Move", out BlendTree tree, 0);
            tree.name = "Move";
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Walk_Loop"), WalkThreshold);
            tree.AddChild(Clip("Jog_Fwd_Loop"), JogThreshold);
            tree.AddChild(Clip("Sprint_Loop"), SprintThreshold);
            ChildMotion[] children = tree.children;
            children[0].timeScale = WalkThreshold / WalkNaturalSpeed;
            children[1].timeScale = JogThreshold / JogNaturalSpeed;
            children[2].timeScale = SprintThreshold / SprintNaturalSpeed;
            tree.children = children;
            move.speedParameter = "MoveRate";
            move.speedParameterActive = true;
            move.iKOnFeet = true;
            Place(machine, move, 0, 1);

            // Camera relative walking (Looking Direction / Aiming): forward, backward and side steps blended by
            // the velocity direction relative to the character.
            AnimatorState strafe = controller.CreateBlendTreeInController("Strafe", out BlendTree strafeTree, 0);
            strafeTree.name = "Strafe";
            strafeTree.blendType = BlendTreeType.SimpleDirectional2D;
            strafeTree.blendParameter = "DirectionX";
            strafeTree.blendParameterY = "DirectionY";
            strafeTree.AddChild(Clip("Walk_Loop"), new Vector2(0f, 1f));
            strafeTree.AddChild(Clip("Walk_Backwards"), new Vector2(0f, -1f));
            strafeTree.AddChild(Clip("Strafe_left"), new Vector2(-1f, 0f));
            strafeTree.AddChild(Clip("Strafe_right"), new Vector2(1f, 0f));
            ChildMotion[] strafeChildren = strafeTree.children;
            strafeChildren[0].timeScale = StrafeTreeSpeed / WalkNaturalSpeed;
            strafeChildren[1].timeScale = StrafeTreeSpeed / BackwardNaturalSpeed;
            strafeChildren[2].timeScale = StrafeTreeSpeed / StrafeNaturalSpeed;
            strafeChildren[3].timeScale = StrafeTreeSpeed / StrafeNaturalSpeed;
            strafeTree.children = strafeChildren;
            strafe.speedParameter = "StrafeRate";
            strafe.speedParameterActive = true;
            strafe.iKOnFeet = true;
            Place(machine, strafe, 0, 2);

            AddState(machine, "CrouchIdle", Clip("Crouch_Idle_Loop"), 1, 0);
            AnimatorState crouchMove = AddState(machine, "CrouchMove", Clip("Crouch_Fwd_Loop"), 1, 1);
            crouchMove.speedParameter = "CrouchRate";
            crouchMove.speedParameterActive = true;

            AddState(machine, "JumpStart", Clip("Jump_Start"), 2, 0);
            AddState(machine, "Fall", Clip("Jump_Loop"), 2, 1);
            AddState(machine, "Land", Clip("Jump_Land"), 2, 2);

            AddActionState(machine, "Roll", Clip("Roll"), 3, 0);
            AddActionState(machine, "Mantle", Clip("ClimbUp_1m"), 3, 1);
            AddActionState(machine, "GetUpBack", Clip("LayToIdle"), 3, 2);
            AddActionState(machine, "GetUpFront", Clip("Pushup"), 3, 3);

            AddActionState(machine, "TurnLeft90", Clip("Turn_Left_90"), 4, 0);
            AddActionState(machine, "TurnRight90", Clip("Turn_Right_90"), 4, 1);
            AddActionState(machine, "TurnLeft180", Clip("Turn_Left_180"), 4, 2);
            AddActionState(machine, "TurnRight180", Clip("Turn_Right_180"), 4, 3);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("ALS: built " + ALSAssetPaths.AnimatorController);
            return controller;
        }

        public static AnimationClip Clip(string name)
        {
            foreach (string path in ALSModelImportSetup.AllModels)
            {
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == name);
                if (clip != null)
                {
                    return clip;
                }
            }
            Debug.LogError("ALS: animation clip '" + name + "' not found. Run 'Configure Model Imports' first.");
            return null;
        }

        private static void AddFloat(AnimatorController controller, string name, float defaultValue)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = defaultValue
            });
        }

        private static AnimatorState AddState(AnimatorStateMachine machine, string name, Motion motion, int column, int row)
        {
            AnimatorState state = machine.AddState(name, new Vector3(300f + column * 240f, 60f + row * 70f, 0f));
            state.motion = motion;
            state.iKOnFeet = true;
            state.writeDefaultValues = true;
            return state;
        }

        private static AnimatorState AddActionState(AnimatorStateMachine machine, string name, Motion motion, int column, int row)
        {
            AnimatorState state = AddState(machine, name, motion, column, row);
            state.speedParameter = "ActionRate";
            state.speedParameterActive = true;
            return state;
        }

        private static void Place(AnimatorStateMachine machine, AnimatorState state, int column, int row)
        {
            ChildAnimatorState[] states = machine.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state == state)
                {
                    states[i].position = new Vector3(300f + column * 240f, 60f + row * 70f, 0f);
                }
            }
            machine.states = states;
        }
    }
}
