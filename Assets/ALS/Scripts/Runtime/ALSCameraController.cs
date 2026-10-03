// Third person camera. The pivot lag / offset / collision steps follow ALS-Community's
// ALSPlayerCameraManager::CustomCameraBehavior (MIT, (C) 2020 Doğa Can Yanıkoğlu & LongmireLocomotion, see
// THIRD_PARTY_NOTICES.md). The per-state values that ALS reads from its camera AnimBP curves are plain
// settings blocks here.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ALSUnity
{
    [Serializable]
    public class ALSCameraSettings
    {
        [Tooltip("Offset from the smoothed pivot in character space (x = right, y = up, z = forward).")]
        public Vector3 pivotOffset = new Vector3(0f, 0.2f, 0f);
        [Tooltip("Offset from the pivot in camera space (x = right shoulder, y = up, z = forward; negative z is behind).")]
        public Vector3 cameraOffset = new Vector3(0.45f, 0.2f, -3f);
        [Tooltip("Pivot lag speed per camera axis (x = right, y = up, z = forward). Higher is tighter.")]
        public Vector3 pivotLagSpeed = new Vector3(8f, 12f, 8f);
        public float rotationLagSpeed = 20f;

        public ALSCameraSettings()
        {
        }

        public ALSCameraSettings(Vector3 pivotOffset, Vector3 cameraOffset, Vector3 pivotLagSpeed, float rotationLagSpeed)
        {
            this.pivotOffset = pivotOffset;
            this.cameraOffset = cameraOffset;
            this.pivotLagSpeed = pivotLagSpeed;
            this.rotationLagSpeed = rotationLagSpeed;
        }
    }

    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]
    public class ALSCameraController : MonoBehaviour
    {
        public ALSCharacter target;

        [Header("Per-state settings")]
        public ALSCameraSettings standing = new ALSCameraSettings(
            new Vector3(0f, 0.2f, 0f), new Vector3(0.45f, 0.2f, -3f), new Vector3(8f, 12f, 8f), 20f);
        public ALSCameraSettings sprinting = new ALSCameraSettings(
            new Vector3(0f, 0.2f, 0f), new Vector3(0.45f, 0.15f, -3.4f), new Vector3(8f, 12f, 5f), 20f);
        public ALSCameraSettings crouching = new ALSCameraSettings(
            new Vector3(0f, 0.15f, 0f), new Vector3(0.45f, 0.25f, -2.7f), new Vector3(8f, 10f, 8f), 20f);
        public ALSCameraSettings aiming = new ALSCameraSettings(
            new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.15f, -1.7f), new Vector3(16f, 16f, 16f), 30f);
        public ALSCameraSettings ragdoll = new ALSCameraSettings(
            new Vector3(0f, 0.2f, 0f), new Vector3(0f, 0.5f, -3.6f), new Vector3(6f, 6f, 6f), 20f);

        [Tooltip("How quickly the camera blends between the settings blocks.")]
        public float settingsBlendSpeed = 4f;
        public float fieldOfView = 60f;

        [Header("Collision")]
        public float traceRadius = 0.15f;
        [Tooltip("Sideways offset of the collision trace origin towards the active shoulder.")]
        public float traceShoulderOffset = 0.15f;
        [Tooltip("When a wall pushes the camera closer than this to the character, the character is drawn as a shadow only.")]
        public float hideCharacterDistance = 0.5f;

        private Camera cam;
        private Vector3 smoothedPivot;
        private float cameraYaw;
        private float cameraPitch;
        private bool initialized;
        private Renderer[] targetRenderers;
        private bool targetHidden;

        // Blended settings
        private Vector3 pivotOffset;
        private Vector3 cameraOffset;
        private Vector3 pivotLagSpeed;
        private float rotationLagSpeed;

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (!initialized)
            {
                Initialize();
            }

            BlendSettings(dt);

            // Step 2: Calculate Target Camera Rotation. Use the Control Rotation and interpolate for smooth
            // camera rotation.
            cameraYaw = ALSMath.AngleInterpTo(cameraYaw, target.ControlYaw, dt, rotationLagSpeed);
            cameraPitch = ALSMath.AngleInterpTo(cameraPitch, target.ControlPitch, dt, rotationLagSpeed);
            Quaternion cameraRotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);

            // Step 3: Calculate the Smoothed Pivot Target. Get the 3P Pivot Target and interpolate using axis
            // independent lag for maximum control.
            Vector3 pivotTarget = GetPivotTarget();
            smoothedPivot = CalculateAxisIndependentLag(smoothedPivot, pivotTarget, cameraYaw, pivotLagSpeed, dt);

            // Step 4: Calculate Pivot Location. Get the Smoothed Pivot Target and apply local offsets for
            // further camera control.
            Vector3 pivotLocation = smoothedPivot + target.transform.rotation * pivotOffset;

            // Step 5: Calculate Target Camera Location. Get the Pivot location and apply camera relative offsets.
            Vector3 offset = cameraOffset;
            if (!target.IsRightShoulder)
            {
                offset.x = -offset.x;
            }
            Vector3 cameraLocation = pivotLocation + cameraRotation * offset;

            // Step 6: Trace for an object between the camera and character to apply a corrective offset.
            Vector3 traceOrigin = GetTraceOrigin(cameraRotation);
            Vector3 toCamera = cameraLocation - traceOrigin;
            float distance = toCamera.magnitude;
            if (distance > 0.001f && Physics.SphereCast(traceOrigin, traceRadius, toCamera / distance,
                    out RaycastHit hit, distance, target.worldMask, QueryTriggerInteraction.Ignore))
            {
                cameraLocation = traceOrigin + toCamera / distance * hit.distance;
            }

            // Don't look at the inside of the character when a wall squeezes the camera against it.
            float closeDistance = Vector3.Distance(cameraLocation, traceOrigin);
            SetTargetHidden(closeDistance < (targetHidden ? hideCharacterDistance + 0.1f : hideCharacterDistance));

            transform.SetPositionAndRotation(cameraLocation, cameraRotation);
            cam.fieldOfView = fieldOfView;
        }

        private void SetTargetHidden(bool hidden)
        {
            if (hidden == targetHidden)
            {
                return;
            }
            targetHidden = hidden;
            if (targetRenderers == null)
            {
                targetRenderers = target.GetComponentsInChildren<Renderer>();
            }
            foreach (Renderer targetRenderer in targetRenderers)
            {
                targetRenderer.shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }
        }

        private void Initialize()
        {
            initialized = true;
            cameraYaw = target.ControlYaw;
            cameraPitch = target.ControlPitch;
            smoothedPivot = GetPivotTarget();
            ALSCameraSettings s = GetTargetSettings();
            pivotOffset = s.pivotOffset;
            cameraOffset = s.cameraOffset;
            pivotLagSpeed = s.pivotLagSpeed;
            rotationLagSpeed = s.rotationLagSpeed;
        }

        private ALSCameraSettings GetTargetSettings()
        {
            if (target.MovementState == ALSMovementState.Ragdoll)
            {
                return ragdoll;
            }
            if (target.RotationMode == ALSRotationMode.Aiming)
            {
                return aiming;
            }
            if (target.Stance == ALSStance.Crouching)
            {
                return crouching;
            }
            return target.Gait == ALSGait.Sprinting ? sprinting : standing;
        }

        private void BlendSettings(float dt)
        {
            ALSCameraSettings s = GetTargetSettings();
            pivotOffset = ALSMath.InterpTo(pivotOffset, s.pivotOffset, dt, settingsBlendSpeed);
            cameraOffset = ALSMath.InterpTo(cameraOffset, s.cameraOffset, dt, settingsBlendSpeed);
            pivotLagSpeed = ALSMath.InterpTo(pivotLagSpeed, s.pivotLagSpeed, dt, settingsBlendSpeed);
            rotationLagSpeed = ALSMath.InterpTo(rotationLagSpeed, s.rotationLagSpeed, dt, settingsBlendSpeed);
        }

        private Vector3 GetPivotTarget()
        {
            if (target.MovementState == ALSMovementState.Ragdoll && target.Ragdoll != null)
            {
                return target.Ragdoll.HipsPosition;
            }
            if (target.Animation != null)
            {
                return target.Animation.GetPivotPosition();
            }
            return target.transform.position + Vector3.up * 0.9f;
        }

        private Vector3 GetTraceOrigin(Quaternion cameraRotation)
        {
            if (target.MovementState == ALSMovementState.Ragdoll && target.Ragdoll != null)
            {
                return target.Ragdoll.HipsPosition + Vector3.up * 0.3f;
            }
            Vector3 head = target.Animation != null
                ? target.Animation.GetHeadPosition()
                : target.transform.position + Vector3.up * 1.6f;
            // Keep the origin on the capsule axis so it can never end up inside a wall the character leans on.
            Vector3 origin = target.transform.position;
            origin.y = head.y;
            float side = target.IsRightShoulder ? traceShoulderOffset : -traceShoulderOffset;
            return origin + cameraRotation * Vector3.right * side;
        }

        private static Vector3 CalculateAxisIndependentLag(Vector3 current, Vector3 targetLocation, float cameraYaw,
            Vector3 lagSpeeds, float dt)
        {
            Quaternion yawRotation = Quaternion.Euler(0f, cameraYaw, 0f);
            Quaternion inverse = Quaternion.Inverse(yawRotation);
            Vector3 localCurrent = inverse * current;
            Vector3 localTarget = inverse * targetLocation;
            Vector3 result = new Vector3(
                ALSMath.InterpTo(localCurrent.x, localTarget.x, dt, lagSpeeds.x),
                ALSMath.InterpTo(localCurrent.y, localTarget.y, dt, lagSpeeds.y),
                ALSMath.InterpTo(localCurrent.z, localTarget.z, dt, lagSpeeds.z));
            return yawRotation * result;
        }
    }
}
