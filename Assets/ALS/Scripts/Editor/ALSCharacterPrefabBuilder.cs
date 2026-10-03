using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ALSUnity.EditorTools
{
    /// <summary>Assembles the playable character prefab from the mannequin model and the ALS components.</summary>
    public static class ALSCharacterPrefabBuilder
    {
        [MenuItem("ALS/Build Steps/3. Build Character Prefab")]
        public static GameObject Build()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ALSAssetPaths.AnimatorController);
            if (controller == null)
            {
                controller = ALSAnimatorBuilder.Build();
            }

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ALSAssetPaths.Ual1Model);
            if (modelAsset == null)
            {
                Debug.LogError("ALS: character model not found at " + ALSAssetPaths.Ual1Model);
                return null;
            }

            int layer = LayerMask.NameToLayer("Ignore Raycast");
            var root = new GameObject("ALSCharacter") { layer = layer };

            CharacterController capsule = root.AddComponent<CharacterController>();
            capsule.radius = 0.3f;
            capsule.height = 1.8f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.stepOffset = 0.4f;
            capsule.slopeLimit = 46f;
            capsule.skinWidth = 0.03f;
            capsule.minMoveDistance = 0f;

            root.AddComponent<ALSCharacter>();
            root.AddComponent<ALSMantle>();
            ALSRagdoll ragdoll = root.AddComponent<ALSRagdoll>();
            root.AddComponent<ALSPlayerInput>();

            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            model.name = "Model";
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }

            Animator animator = model.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;

            ALSCharacterAnimation anim = model.AddComponent<ALSCharacterAnimation>();
            anim.jumpStartLength = ALSAnimatorBuilder.Clip("Jump_Start").length;
            anim.landLength = ALSAnimatorBuilder.Clip("Jump_Land").length;
            anim.rollLength = ALSAnimatorBuilder.Clip("Roll").length;
            anim.mantleLength = ALSAnimatorBuilder.Clip("ClimbUp_1m").length;
            anim.getUpBackLength = ALSAnimatorBuilder.Clip("LayToIdle").length;
            anim.getUpFrontLength = ALSAnimatorBuilder.Clip("Pushup").length;
            anim.turn90Length = ALSAnimatorBuilder.Clip("Turn_Left_90").length;
            anim.turn180Length = ALSAnimatorBuilder.Clip("Turn_Left_180").length;

            // Measure clip dependent values on a scratch copy of the model.
            GameObject probe = Object.Instantiate(modelAsset);
            probe.hideFlags = HideFlags.HideAndDontSave;
            anim.turnRotationCurves = new[]
            {
                MeasureTurnCurve(probe, "Turn_Left_90"), MeasureTurnCurve(probe, "Turn_Right_90"),
                MeasureTurnCurve(probe, "Turn_Left_180"), MeasureTurnCurve(probe, "Turn_Right_180")
            };
            MeasureLocomotion(probe, ALSAnimatorBuilder.MoveClips, ALSAnimatorBuilder.MoveClipMirrored,
                out anim.moveClipVelocities, out anim.moveClipLengths);
            MeasureLocomotion(probe, ALSAnimatorBuilder.CrouchClips, null, out anim.crouchClipVelocities,
                out anim.crouchClipLengths);
            MeasureLyingPose(probe, "LayToIdle", 0f, out ragdoll.faceUpHeadYaw, out ragdoll.faceUpHipsOffset);
            MeasureLyingPose(probe, "Pushup", anim.getUpFrontStart, out ragdoll.faceDownHeadYaw, out ragdoll.faceDownHipsOffset);
            Object.DestroyImmediate(probe);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "ALS: get-up back yaw={0:0} hips={1}; front yaw={2:0} hips={3}",
                ragdoll.faceUpHeadYaw, ragdoll.faceUpHipsOffset.ToString("F2"), ragdoll.faceDownHeadYaw,
                ragdoll.faceDownHipsOffset.ToString("F2")));

            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // The ragdoll moves the skeleton away from the root, keep the bounds following it.
                skin.updateWhenOffscreen = true;
            }

            System.IO.Directory.CreateDirectory(ALSAssetPaths.PrefabsFolder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ALSAssetPaths.CharacterPrefab);
            Object.DestroyImmediate(root);
            Debug.Log("ALS: built " + ALSAssetPaths.CharacterPrefab);
            return prefab;
        }

        /// <summary>
        /// Samples how far (0-1) a turn in place clip has rotated the root over its normalized time. The
        /// character replays this curve to rotate in step with the animation.
        /// </summary>
        private static AnimationCurve MeasureTurnCurve(GameObject probe, string clipName)
        {
            AnimationClip clip = ALSAnimatorBuilder.Clip(clipName);
            const int steps = 20;
            var accumulated = new float[steps + 1];
            clip.SampleAnimation(probe, 0f);
            float previous = probe.transform.eulerAngles.y;
            for (int i = 1; i <= steps; i++)
            {
                clip.SampleAnimation(probe, clip.length * i / steps);
                float yaw = probe.transform.eulerAngles.y;
                accumulated[i] = accumulated[i - 1] + Mathf.DeltaAngle(previous, yaw);
                previous = yaw;
            }
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            float total = accumulated[steps];
            var curve = new AnimationCurve();
            for (int i = 0; i <= steps; i++)
            {
                float value = Mathf.Abs(total) > 1f ? Mathf.Clamp01(accumulated[i] / total) : i / (float)steps;
                curve.AddKey(new Keyframe(i / (float)steps, value));
            }
            for (int i = 0; i <= steps; i++)
            {
                curve.SmoothTangents(i, 0f);
            }
            return curve;
        }

        private static void MeasureLocomotion(GameObject probe, string[] clipNames, bool[] mirrored,
            out Vector2[] velocities, out float[] lengths)
        {
            velocities = new Vector2[clipNames.Length];
            lengths = new float[clipNames.Length];
            var log = new System.Text.StringBuilder("ALS: stride velocities (m/s)");
            for (int i = 0; i < clipNames.Length; i++)
            {
                AnimationClip clip = ALSAnimatorBuilder.Clip(clipNames[i]);
                lengths[i] = clip.length;
                velocities[i] = MeasureStrideVelocity(probe, clip);
                if (mirrored != null && mirrored[i])
                {
                    velocities[i].x = -velocities[i].x;
                }
                log.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, " {0}{1}=({2:0.00}, {3:0.00})",
                    clipNames[i], mirrored != null && mirrored[i] ? " (mirrored)" : "", velocities[i].x, velocities[i].y);
            }
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// The ground velocity (x = right, y = forward, m/s) an in-place locomotion clip is animated for: the
        /// opposite of the velocity of the ball of the supporting foot. The animation driver uses it to match
        /// the playback rate and the stride length to the speed of the character.
        /// </summary>
        private static Vector2 MeasureStrideVelocity(GameObject probe, AnimationClip clip)
        {
            Animator animator = probe.GetComponent<Animator>();
            Transform[] toes =
            {
                animator.GetBoneTransform(HumanBodyBones.LeftToes) ?? animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                animator.GetBoneTransform(HumanBodyBones.RightToes) ?? animator.GetBoneTransform(HumanBodyBones.RightFoot)
            };

            const int steps = 120;
            const float contactHeight = 0.025f;
            var positions = new Vector3[toes.Length, steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                clip.SampleAnimation(probe, clip.length * i / steps);
                for (int foot = 0; foot < toes.Length; foot++)
                {
                    positions[foot, i] = probe.transform.InverseTransformPoint(toes[foot].position);
                }
            }
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Floor height per foot: a low percentile, so single frames that dip below the ground do not count.
            var floors = new float[toes.Length];
            var heights = new float[steps + 1];
            for (int foot = 0; foot < toes.Length; foot++)
            {
                for (int i = 0; i <= steps; i++)
                {
                    heights[i] = positions[foot, i].y;
                }
                System.Array.Sort(heights);
                floors[foot] = heights[steps / 10];
            }

            // The body moves over the foot that carries it: the lower one, while it is on the ground (a run has
            // flight phases in between). The median leaves out the frames in which that foot rolls off its toes.
            float dt = clip.length / steps;
            var sideways = new System.Collections.Generic.List<float>();
            var forward = new System.Collections.Generic.List<float>();
            for (int i = 0; i < steps; i++)
            {
                int support = -1;
                float lowest = contactHeight;
                for (int foot = 0; foot < toes.Length; foot++)
                {
                    float height = Mathf.Max(positions[foot, i].y, positions[foot, i + 1].y) - floors[foot];
                    if (height < lowest)
                    {
                        lowest = height;
                        support = foot;
                    }
                }
                if (support >= 0)
                {
                    Vector3 velocity = (positions[support, i] - positions[support, i + 1]) / dt;
                    sideways.Add(velocity.x);
                    forward.Add(velocity.z);
                }
            }
            if (forward.Count == 0)
            {
                return Vector2.zero;
            }
            sideways.Sort();
            forward.Sort();
            return new Vector2(sideways[sideways.Count / 2], forward[forward.Count / 2]);
        }

        /// <summary>
        /// Reads where the body lies relative to the character root in a get-up clip, so the ragdoll can line
        /// the root up with the body before the clip starts.
        /// </summary>
        private static void MeasureLyingPose(GameObject probe, string clipName, float normalizedTime, out float headYaw,
            out Vector3 hipsOffset)
        {
            AnimationClip clip = ALSAnimatorBuilder.Clip(clipName);
            clip.SampleAnimation(probe, clip.length * normalizedTime);
            Animator animator = probe.GetComponent<Animator>();
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Quaternion toLocal = Quaternion.Inverse(probe.transform.rotation);
            Vector3 localHips = toLocal * (hips.position - probe.transform.position);
            Vector3 localHeadDirection = toLocal * (head.position - hips.position);
            headYaw = Mathf.Atan2(localHeadDirection.x, localHeadDirection.z) * Mathf.Rad2Deg;
            hipsOffset = new Vector3(localHips.x, 0f, localHips.z);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
    }
}
