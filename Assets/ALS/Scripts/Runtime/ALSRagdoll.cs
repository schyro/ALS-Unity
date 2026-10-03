using System.Collections.Generic;
using UnityEngine;

namespace ALSUnity
{
    /// <summary>
    /// Builds a ragdoll on the Humanoid skeleton at startup and switches between animation and physics.
    /// On exit, the ragdoll pose is captured and blended into the get-up animation (the equivalent of the
    /// pose snapshot ALS takes in RagdollEnd).
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class ALSRagdoll : MonoBehaviour
    {
        public float totalMass = 75f;
        [Tooltip("Seconds used to blend from the ragdoll pose into the get-up animation.")]
        public float blendOutDuration = 0.45f;
        [Header("Get-up alignment (set by the builder from the first frame of each get-up clip)")]
        [Tooltip("Yaw (degrees) of the hips-to-head direction, in character space, of the face-up get-up clip.")]
        public float faceUpHeadYaw = 180f;
        [Tooltip("Hips position, in character space, of the face-up get-up clip.")]
        public Vector3 faceUpHipsOffset;
        [Tooltip("Yaw (degrees) of the hips-to-head direction, in character space, of the face-down get-up clip.")]
        public float faceDownHeadYaw;
        public Vector3 faceDownHipsOffset;

        public bool IsActive { get; private set; }
        public bool IsBlending => blendTimer > 0f;
        public Vector3 Velocity => hipsBody != null ? hipsBody.linearVelocity : Vector3.zero;
        public Vector3 HipsPosition => hips != null ? hips.position : transform.position;

        private ALSCharacter character;
        private Animator animator;
        private Transform hips;
        private Transform head;
        private Transform leftUpperLeg;
        private Transform rightUpperLeg;
        private Rigidbody hipsBody;

        private readonly List<Rigidbody> bodies = new List<Rigidbody>();
        private readonly List<Collider> colliders = new List<Collider>();

        private Transform[] blendBones;
        private Quaternion[] snapshotRotations;
        private Vector3 snapshotHipsPosition;
        private float blendTimer;

        private void Awake()
        {
            character = GetComponent<ALSCharacter>();
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning("ALSRagdoll needs a Humanoid Animator in its children.", this);
                enabled = false;
                return;
            }
            Build();
            SetSimulated(false);
        }

        // ------------------------------------------------------------------------------------------
        // Runtime
        // ------------------------------------------------------------------------------------------

        public void Begin(Vector3 initialVelocity)
        {
            if (hipsBody == null)
            {
                return;
            }
            blendTimer = 0f;
            IsActive = true;
            Physics.SyncTransforms();
            SetSimulated(true);
            foreach (Rigidbody body in bodies)
            {
                body.linearVelocity = initialVelocity;
                body.angularVelocity = Vector3.zero;
            }
        }

        public void End(out bool onGround, out bool faceUp, out Vector3 velocity)
        {
            onGround = false;
            faceUp = false;
            velocity = Vector3.zero;
            if (!IsActive)
            {
                return;
            }

            velocity = hipsBody.linearVelocity;
            IsActive = false;

            // Determine whether the ragdoll is facing up or down.
            Vector3 hipsPosition = hips.position;
            Vector3 spineUp = head.position - hipsPosition;
            Vector3 right = rightUpperLeg.position - leftUpperLeg.position;
            Vector3 bodyForward = Vector3.Cross(right, spineUp);
            faceUp = bodyForward.y > 0f;

            // Orient and place the root so the first frame of the get-up clip lines up with the body on the floor.
            Vector3 headDirection = ALSMath.Flatten(spineUp);
            float rootYaw = transform.eulerAngles.y;
            if (headDirection.sqrMagnitude > 1e-4f)
            {
                rootYaw = ALSMath.YawOf(headDirection) - (faceUp ? faceUpHeadYaw : faceDownHeadYaw);
            }
            Vector3 hipsOffset = Quaternion.Euler(0f, rootYaw, 0f) * (faceUp ? faceUpHipsOffset : faceDownHipsOffset);
            Vector3 rootPosition = hipsPosition - ALSMath.Flatten(hipsOffset);

            // Trace downward from the pelvis to find the floor.
            float halfHeight = character.standingHeight * 0.5f;
            if (Physics.Raycast(rootPosition + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, halfHeight + 0.1f,
                    character.worldMask, QueryTriggerInteraction.Ignore))
            {
                onGround = true;
                rootPosition = hit.point + Vector3.up * character.Controller.skinWidth;
            }
            else
            {
                rootPosition -= Vector3.up * halfHeight;
            }

            // Move the root under the ragdoll without moving the ragdoll itself.
            Quaternion hipsRotation = hips.rotation;
            transform.SetPositionAndRotation(rootPosition, Quaternion.Euler(0f, rootYaw, 0f));
            hips.SetPositionAndRotation(hipsPosition, hipsRotation);

            // Save a snapshot of the current Ragdoll Pose to blend out of the ragdoll.
            for (int i = 0; i < blendBones.Length; i++)
            {
                snapshotRotations[i] = blendBones[i].localRotation;
            }
            snapshotHipsPosition = hips.localPosition;
            blendTimer = blendOutDuration;

            SetSimulated(false);
        }

        private void LateUpdate()
        {
            if (blendTimer <= 0f)
            {
                return;
            }

            blendTimer -= Time.deltaTime;
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blendTimer / blendOutDuration));
            for (int i = 0; i < blendBones.Length; i++)
            {
                blendBones[i].localRotation = Quaternion.Slerp(blendBones[i].localRotation, snapshotRotations[i], weight);
            }
            hips.localPosition = Vector3.Lerp(hips.localPosition, snapshotHipsPosition, weight);
        }

        private void SetSimulated(bool simulated)
        {
            foreach (Rigidbody body in bodies)
            {
                body.isKinematic = !simulated;
                body.detectCollisions = simulated;
            }
            foreach (Collider col in colliders)
            {
                col.enabled = simulated;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------------------------------

        private void Build()
        {
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            Transform upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            Transform torso = chest != null ? chest : spine;
            leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            // The skeleton is still in its reference pose here, so the character axes describe the body axes.
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            Vector3 forward = transform.forward;

            float hipWidth = Vector3.Distance(leftUpperLeg.position, rightUpperLeg.position);
            float shoulderWidth = Vector3.Distance(leftUpperArm.position, rightUpperArm.position);
            float torsoHeight = Vector3.Distance(torso.position, head.position);
            float pelvisHeight = Mathf.Max(0.12f, Vector3.Distance(hips.position, torso.position) * 1.2f);

            // Bodies
            hipsBody = AddBody(hips, 0.2f);
            AddBox(hips, hips.position + up * (pelvisHeight * 0.15f),
                new Vector3(hipWidth * 1.7f, pelvisHeight, hipWidth * 1.1f));

            Rigidbody torsoBody = AddBody(torso, 0.25f);
            Transform torsoTop = upperChest != null ? upperChest : torso;
            AddBox(torso, Vector3.Lerp(torso.position, head.position, 0.45f),
                new Vector3(shoulderWidth * 0.95f, torsoHeight * 0.95f, shoulderWidth * 0.6f));

            Rigidbody headBody = AddBody(head, 0.07f);
            float headRadius = Mathf.Max(0.09f, shoulderWidth * 0.3f);
            AddSphere(head, head.position + up * headRadius * 0.8f, headRadius);

            Rigidbody leftThigh = AddBody(leftUpperLeg, 0.1f);
            AddCapsule(leftUpperLeg, leftUpperLeg.position, leftLowerLeg.position, 0.2f);
            Rigidbody rightThigh = AddBody(rightUpperLeg, 0.1f);
            AddCapsule(rightUpperLeg, rightUpperLeg.position, rightLowerLeg.position, 0.2f);

            Rigidbody leftShin = AddBody(leftLowerLeg, 0.075f);
            AddCapsule(leftLowerLeg, leftLowerLeg.position, leftFoot.position, 0.16f);
            Rigidbody rightShin = AddBody(rightLowerLeg, 0.075f);
            AddCapsule(rightLowerLeg, rightLowerLeg.position, rightFoot.position, 0.16f);

            Rigidbody leftArm = AddBody(leftUpperArm, 0.035f);
            AddCapsule(leftUpperArm, leftUpperArm.position, leftLowerArm.position, 0.2f);
            Rigidbody rightArm = AddBody(rightUpperArm, 0.035f);
            AddCapsule(rightUpperArm, rightUpperArm.position, rightLowerArm.position, 0.2f);

            Rigidbody leftForearm = AddBody(leftLowerArm, 0.03f);
            AddCapsule(leftLowerArm, leftLowerArm.position, leftHand.position, 0.18f, 1.25f);
            Rigidbody rightForearm = AddBody(rightLowerArm, 0.03f);
            AddCapsule(rightLowerArm, rightLowerArm.position, rightHand.position, 0.18f, 1.25f);

            // Joints. The twist axis is the main hinge of each joint; limits are (low twist, high twist, swing 1, swing 2).
            AddJoint(torsoBody, hipsBody, right, forward, -25f, 25f, 15f, 15f);
            AddJoint(headBody, torsoBody, right, forward, -40f, 25f, 25f, 20f);
            AddJoint(leftThigh, hipsBody, right, forward, -20f, 70f, 30f, 10f);
            AddJoint(rightThigh, hipsBody, right, forward, -20f, 70f, 30f, 10f);
            AddJoint(leftShin, leftThigh, right, forward, -120f, 0f, 0f, 0f);
            AddJoint(rightShin, rightThigh, right, forward, -120f, 0f, 0f, 0f);
            AddJoint(leftArm, torsoBody, up, forward, -70f, 30f, 60f, 30f);
            AddJoint(rightArm, torsoBody, -up, forward, -70f, 30f, 60f, 30f);
            AddJoint(leftForearm, leftArm, up, forward, -120f, 0f, 0f, 0f);
            AddJoint(rightForearm, rightArm, -up, forward, -120f, 0f, 0f, 0f);

            // Bones blended back to animation after the ragdoll ends.
            blendBones = hips.GetComponentsInChildren<Transform>(true);
            var boneList = new List<Transform>();
            foreach (Transform t in blendBones)
            {
                if (!t.name.StartsWith(ColliderName))
                {
                    boneList.Add(t);
                }
            }
            blendBones = boneList.ToArray();
            snapshotRotations = new Quaternion[blendBones.Length];
        }

        private const string ColliderName = "RagdollCollider";

        private Rigidbody AddBody(Transform bone, float massFraction)
        {
            Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = totalMass * massFraction;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.maxDepenetrationVelocity = 3f;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;
            body.isKinematic = true;
            bodies.Add(body);
            return body;
        }

        private Transform AddColliderObject(Transform bone, Vector3 worldCenter, Quaternion worldRotation)
        {
            var go = new GameObject(ColliderName);
            go.layer = bone.gameObject.layer;
            go.transform.SetParent(bone, false);
            go.transform.SetPositionAndRotation(worldCenter, worldRotation);
            // Cancel any bone scale so collider sizes are in metres.
            Vector3 s = bone.lossyScale;
            go.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            return go.transform;
        }

        private void AddBox(Transform bone, Vector3 worldCenter, Vector3 size)
        {
            Transform t = AddColliderObject(bone, worldCenter, transform.rotation);
            BoxCollider box = t.gameObject.AddComponent<BoxCollider>();
            box.size = size;
            colliders.Add(box);
        }

        private void AddSphere(Transform bone, Vector3 worldCenter, float radius)
        {
            Transform t = AddColliderObject(bone, worldCenter, transform.rotation);
            SphereCollider sphere = t.gameObject.AddComponent<SphereCollider>();
            sphere.radius = radius;
            colliders.Add(sphere);
        }

        private void AddCapsule(Transform bone, Vector3 start, Vector3 end, float radiusPerLength, float lengthScale = 1f)
        {
            Vector3 axis = end - start;
            float length = axis.magnitude * lengthScale;
            if (length < 1e-4f)
            {
                return;
            }
            Transform t = AddColliderObject(bone, start + axis.normalized * (length * 0.5f), Quaternion.LookRotation(axis));
            CapsuleCollider capsule = t.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = axis.magnitude * radiusPerLength;
            capsule.height = Mathf.Max(length, capsule.radius * 2f);
            colliders.Add(capsule);
        }

        private void AddJoint(Rigidbody body, Rigidbody parent, Vector3 worldAxis, Vector3 worldSwingAxis,
            float lowTwist, float highTwist, float swing1, float swing2)
        {
            CharacterJoint joint = body.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.anchor = Vector3.zero;
            joint.axis = body.transform.InverseTransformDirection(worldAxis);
            joint.swingAxis = body.transform.InverseTransformDirection(worldSwingAxis);
            joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
            joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
            joint.swing1Limit = new SoftJointLimit { limit = swing1 };
            joint.swing2Limit = new SoftJointLimit { limit = swing2 };
            joint.enableProjection = true;
            joint.enablePreprocessing = false;
            joint.enableCollision = false;
        }
    }
}
