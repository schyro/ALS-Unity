using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ALSUnity.EditorTools
{
    /// <summary>
    /// Configures the animation FBX files (see Tools/blender for how they were produced): Humanoid rig, only the
    /// clips the prototype uses, loop flags and root transform baking.
    /// </summary>
    public static class ALSModelImportSetup
    {
        // Clips used from "Universal Animation Library" (UAL1).
        public static readonly string[] Ual1Clips =
        {
            "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop",
            "Crouch_Idle_Loop", "Crouch_Fwd_Loop",
            "Jump_Start", "Jump_Loop", "Jump_Land",
            "Roll"
        };

        // Clips used from "Universal Animation Library 2" (UAL2).
        public static readonly string[] Ual2Clips =
        {
            "ClimbUp_1m", "LayToIdle"
        };

        // Clips used from the Mesh2Motion animation set.
        public static readonly string[] M2MAddonClips =
        {
            "Strafe_left", "Strafe_right", "Walk_Backwards", "Pushup"
        };

        public static readonly string[] M2MMocapClips =
        {
            "Turn_Left_90", "Turn_Right_90", "Turn_Left_180", "Turn_Right_180"
        };

        public static readonly string[] AllModels =
        {
            ALSAssetPaths.Ual1Model, ALSAssetPaths.Ual2Model, ALSAssetPaths.M2MAddonModel, ALSAssetPaths.M2MMocapModel
        };

        [MenuItem("ALS/Build Steps/1. Configure Model Imports")]
        public static void ConfigureImports()
        {
            // Every file builds its own Humanoid avatar from its "A_TPose" take; the clips are retargeted onto
            // the mannequin in the first file.
            ConfigureModel(ALSAssetPaths.Ual1Model, Ual1Clips);
            ConfigureModel(ALSAssetPaths.Ual2Model, Ual2Clips);
            ConfigureModel(ALSAssetPaths.M2MAddonModel, M2MAddonClips);
            ConfigureModel(ALSAssetPaths.M2MMocapModel, M2MMocapClips);
        }

        private static readonly string[] LoopingClips = { "Strafe_left", "Strafe_right", "Walk_Backwards" };

        public static void ConfigureModel(string path, string[] wantedClips)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("ALS: model not found at " + path);
                return;
            }

            importer.bakeAxisConversion = true;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            // No root motion node: the "root" bone of the converted files carries the axis conversion tilt, and
            // the turn clips rotate the pelvis instead of it. Unity derives the root transform from the body.
            importer.motionNodeName = string.Empty;

            var wanted = new HashSet<string>(wantedClips);
            var clips = new List<ModelImporterClipAnimation>();
            foreach (ModelImporterClipAnimation take in importer.defaultClipAnimations)
            {
                string cleanName = CleanTakeName(take.takeName);
                if (!wanted.Contains(cleanName))
                {
                    continue;
                }

                take.name = cleanName;
                bool loop = cleanName.EndsWith("_Loop") || System.Array.IndexOf(LoopingClips, cleanName) >= 0;
                take.loopTime = loop;
                take.loopPose = false;
                // The library clips are authored in place: keep every root component in the pose.
                take.lockRootRotation = true;
                take.keepOriginalOrientation = true;
                take.lockRootHeightY = true;
                take.keepOriginalPositionY = true;
                take.lockRootPositionXZ = true;
                take.keepOriginalPositionXZ = true;
                if (cleanName.StartsWith("Turn_"))
                {
                    // Turn in place: take the body rotation out of the pose. The character replays it as yaw from
                    // a curve sampled by ALSCharacterPrefabBuilder.
                    take.lockRootRotation = false;
                }
                clips.Add(take);
            }

            foreach (string name in wantedClips)
            {
                if (clips.All(c => c.name != name))
                {
                    Debug.LogWarning("ALS: clip '" + name + "' not found in " + path);
                }
            }

            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();
            Debug.Log("ALS: configured " + path + " with " + clips.Count + " clips.");
        }

        private static string CleanTakeName(string takeName)
        {
            int separator = takeName.LastIndexOf('|');
            return separator >= 0 ? takeName.Substring(separator + 1) : takeName;
        }
    }
}
