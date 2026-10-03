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
        // Clips of the standing locomotion blend, in the order of ALSCharacterAnimation.MoveClip. The weights are
        // set from code, which also matches the playback rate and the stride length to the character speed.
        // Both side steps use the Mesh2Motion clip "Strafe_left", which steps to the character's right once the
        // rig faces +Z (see the stride velocities logged by ALSCharacterPrefabBuilder). The step to the left is
        // the same clip mirrored; "Strafe_right" is not used because its feet move unevenly.
        public static readonly string[] MoveClips =
        {
            "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop", "Walk_Bwd_Loop", "Jog_Bwd_Loop", "Strafe_left", "Strafe_left"
        };
        public static readonly bool[] MoveClipMirrored = { false, false, false, false, false, true, false };

        // Clips of the crouched locomotion blend: forward, backward.
        public static readonly string[] CrouchClips = { "Crouch_Fwd_Loop", "Crouch_Bwd_Loop" };

        public const string MoveWeightPrefix = "MoveWeight";
        public const string CrouchWeightPrefix = "CrouchWeight";

        [MenuItem("ALS/Build Steps/2. Build Animator Controller")]
        public static AnimatorController Build()
        {
            System.IO.Directory.CreateDirectory(ALSAssetPaths.AnimationFolder);
            AssetDatabase.DeleteAsset(ALSAssetPaths.AnimatorController);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ALSAssetPaths.AnimatorController);

            AddFloat(controller, "MoveRate", 1f);
            AddFloat(controller, "CrouchRate", 1f);
            AddFloat(controller, "ActionRate", 1f);
            for (int i = 0; i < MoveClips.Length; i++)
            {
                AddFloat(controller, MoveWeightPrefix + i, i == 0 ? 1f : 0f);
            }
            for (int i = 0; i < CrouchClips.Length; i++)
            {
                AddFloat(controller, CrouchWeightPrefix + i, i == 0 ? 1f : 0f);
            }

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].name = "Base";
            layers[0].iKPass = true;
            controller.layers = layers;
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState idle = AddState(machine, "Idle", Clip("Idle_Loop"), 0, 0);
            machine.defaultState = idle;

            // Standing locomotion: forward walk / jog / sprint, backward walk / jog and side steps.
            AnimatorState move = AddBlendState(controller, "Move", MoveClips, MoveClipMirrored, MoveWeightPrefix, "MoveRate");
            Place(machine, move, 0, 1);

            AddState(machine, "CrouchIdle", Clip("Crouch_Idle_Loop"), 1, 0);
            AnimatorState crouchMove = AddBlendState(controller, "CrouchMove", CrouchClips, null, CrouchWeightPrefix, "CrouchRate");
            Place(machine, crouchMove, 1, 1);

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

        /// <summary>
        /// A state with a direct blend tree: one weight parameter per clip. Unity keeps the clips of a blend tree
        /// in step (same normalized time), so clips with the same foot phase blend without the feet crossing.
        /// Every locomotion loop plants the left foot at the start of its cycle; a mirrored clip starts on the
        /// other foot and is therefore offset by half a cycle.
        /// </summary>
        private static AnimatorState AddBlendState(AnimatorController controller, string name, string[] clips,
            bool[] mirrored, string weightPrefix, string rateParameter)
        {
            AnimatorState state = controller.CreateBlendTreeInController(name, out BlendTree tree, 0);
            tree.name = name;
            tree.blendType = BlendTreeType.Direct;
            foreach (string clip in clips)
            {
                tree.AddChild(Clip(clip));
            }
            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                children[i].directBlendParameter = weightPrefix + i;
                children[i].timeScale = 1f;
                if (mirrored != null && mirrored[i])
                {
                    children[i].mirror = true;
                    children[i].cycleOffset = 0.5f;
                }
            }
            tree.children = children;

            var serializedTree = new SerializedObject(tree);
            SerializedProperty normalized = serializedTree.FindProperty("m_NormalizedBlendValues");
            if (normalized != null)
            {
                normalized.boolValue = true;
                serializedTree.ApplyModifiedPropertiesWithoutUndo();
            }

            state.speedParameter = rateParameter;
            state.speedParameterActive = true;
            state.iKOnFeet = true;
            state.writeDefaultValues = true;
            return state;
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
