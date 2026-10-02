// Character state + movement logic. Structure and behaviour follow ALS-Community's ALSBaseCharacter and
// ALSCharacterMovementComponent (MIT, (C) 2020 Doğa Can Yanıkoğlu & LongmireLocomotion, see
// THIRD_PARTY_NOTICES.md), re-implemented for Unity's CharacterController. Units are metres and degrees.

using System;
using UnityEngine;

namespace ALSUnity
{
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-50)]
    public class ALSCharacter : MonoBehaviour
    {
        [Header("Movement")]
        public ALSMovementSettings standingSettings = new ALSMovementSettings(1.65f, 3.5f, 6f);
        public ALSMovementSettings crouchingSettings = new ALSMovementSettings(1.1f, 1.8f, 1.8f);
        public ALSMovementModel movementModel = new ALSMovementModel();

        [Header("Air")]
        public float jumpVelocity = 6f;
        public float gravity = 9.8f;
        public float airAcceleration = 15f;
        [Range(0f, 1f)] public float airControl = 0.15f;
        public float terminalVelocity = 40f;

        [Header("Landing")]
        [Tooltip("Roll out of a hard landing when there is movement input.")]
        public bool breakfallOnLand = true;
        public float breakfallOnLandVelocity = 7f;
        [Tooltip("Go ragdoll when hitting the ground faster than Ragdoll On Land Velocity.")]
        public bool ragdollOnLand = true;
        public float ragdollOnLandVelocity = 10f;

        [Header("Capsule")]
        public float standingHeight = 1.8f;
        public float crouchingHeight = 1.2f;

        [Header("Actions")]
        public float rollDoubleTapTimeout = 0.3f;
        public float rollPlayRate = 1.15f;
        public float breakfallPlayRate = 1.35f;

        [Header("Desired state")]
        public ALSRotationMode desiredRotationMode = ALSRotationMode.VelocityDirection;
        public ALSGait desiredGait = ALSGait.Running;
        public ALSStance desiredStance = ALSStance.Standing;

        [Header("Looking direction / aiming")]
        [Tooltip("Top speed (m/s) while side-stepping in the Looking Direction and Aiming rotation modes.")]
        public float strafeSpeed = 1.1f;
        [Tooltip("Top speed (m/s) while moving backwards in the Looking Direction and Aiming rotation modes.")]
        public float backwardSpeed = 1.2f;
        public float yawOffsetInterpSpeed = 8f;

        [Header("Collision")]
        [Tooltip("Layers treated as world geometry by ground, mantle, camera and foot IK traces.")]
        public LayerMask worldMask = Physics.DefaultRaycastLayers;

        // ----- Input (written by ALSPlayerInput or any other driver) -----
        public Vector2 MoveInput { get; set; }
        public float ControlYaw { get; set; }
        public float ControlPitch { get; set; }

        // ----- States -----
        public ALSMovementState MovementState { get; private set; } = ALSMovementState.None;
        public ALSMovementState PrevMovementState { get; private set; } = ALSMovementState.None;
        public ALSMovementAction MovementAction { get; private set; } = ALSMovementAction.None;
        public ALSRotationMode RotationMode { get; private set; } = ALSRotationMode.VelocityDirection;
        public ALSGait Gait { get; private set; } = ALSGait.Walking;
        public ALSGait AllowedGait { get; private set; } = ALSGait.Running;
        public ALSStance Stance { get; private set; } = ALSStance.Standing;

        // ----- Essential values -----
        public Vector3 Velocity { get; private set; }
        public Vector3 Acceleration { get; private set; }
        public Vector3 CurrentAcceleration { get; private set; }
        public float MaxAcceleration { get; private set; } = 20f;
        public float MaxBrakingDeceleration { get; private set; } = 15f;
        public float Speed { get; private set; }
        public bool IsMoving { get; private set; }
        public bool HasMovementInput { get; private set; }
        public float MovementInputAmount { get; private set; }
        public float AimingYaw { get; private set; }
        public float AimingPitch { get; private set; }
        public float AimYawRate { get; private set; }
        public float ActorYaw { get; private set; }
        public float TargetYaw { get; private set; }
        public float LastVelocityYaw { get; private set; }
        public float LastMovementInputYaw { get; private set; }
        public float InAirYaw { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>
        /// Direction the character moves in relative to the camera in the Looking Direction / Aiming modes.
        /// Always Forward in the Velocity Direction mode, while sprinting and while crouching.
        /// </summary>
        public ALSMovementDirection MovementDirection { get; private set; } = ALSMovementDirection.Forward;

        /// <summary>
        /// Yaw offset (degrees) from the camera direction used in Looking Direction mode, so that the forward /
        /// backward / strafe animations line up with the velocity (ALS' YawOffset curves).
        /// </summary>
        public float YawOffset { get; private set; }

        /// <summary>True when movement is animated relative to the camera instead of along the velocity.</summary>
        public bool UsesDirectionalMovement =>
            RotationMode != ALSRotationMode.VelocityDirection && Stance == ALSStance.Standing &&
            Gait != ALSGait.Sprinting;

        /// <summary>
        /// Vertical movement that was not explained by walking along the ground plane (stair steps, ledge snaps).
        /// The animation layer consumes this to smooth the visual mesh.
        /// </summary>
        public float PendingStepOffset { get; set; }

        public bool IsRightShoulder { get; private set; } = true;
        public ALSMovementSettings CurrentSettings => Stance == ALSStance.Crouching ? crouchingSettings : standingSettings;
        public CharacterController Controller => controller;
        public ALSCharacterAnimation Animation => anim;
        public ALSMantle Mantle => mantle;
        public ALSRagdoll Ragdoll => ragdoll;

        // ----- Events -----
        public event Action Jumped;
        public event Action<float> Landed;
        public event Action<ALSMovementState> MovementStateChanged;
        public event Action<ALSMovementAction> MovementActionChanged;
        public event Action<ALSStance> StanceChanged;
        public event Action<ALSGait> GaitChanged;
        public event Action<ALSRotationMode> RotationModeChanged;

        private CharacterController controller;
        private ALSCharacterAnimation anim;
        private ALSMantle mantle;
        private ALSRagdoll ragdoll;

        private Vector3 previousVelocity;
        private float previousAimYaw;
        private float verticalVelocity;
        private bool wantsToCrouch;
        private float brakingFrictionFactor;
        private float landFrictionTimer;
        private float lastStanceInputTime = -10f;
        private float actionTimer;
        private bool aiming;
        private Vector3 spawnPosition;
        private float spawnYaw;
        private float defaultStepOffset;

        private float blockedTime;

        // How long the capsule may be held up before its velocity is taken from the blocked movement.
        private const float BlockedTolerance = 0.08f;
        private const float GroundProbeLift = 0.05f;
        private const float GroundProbeRadiusScale = 0.9f;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            defaultStepOffset = controller.stepOffset;
            anim = GetComponentInChildren<ALSCharacterAnimation>();
            mantle = GetComponent<ALSMantle>();
            ragdoll = GetComponent<ALSRagdoll>();

            // Keep our own colliders out of every world trace.
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = ignoreRaycast;
            }
            worldMask &= ~(1 << ignoreRaycast);

            ActorYaw = transform.eulerAngles.y;
            TargetYaw = ActorYaw;
            LastVelocityYaw = ActorYaw;
            LastMovementInputYaw = ActorYaw;
            InAirYaw = ActorYaw;
            ControlYaw = ActorYaw;
            AimingYaw = ActorYaw;
            previousAimYaw = ActorYaw;
            spawnPosition = transform.position;
            spawnYaw = ActorYaw;

            ApplyCapsuleHeight(standingHeight);
        }

        private void Start()
        {
            // Force update states to use the initial desired values.
            wantsToCrouch = desiredStance == ALSStance.Crouching;
            SetGait(desiredGait, true);
            SetStance(desiredStance, true);
            SetRotationMode(desiredRotationMode, true);
            SetMovementState(ALSMovementState.Grounded, true);
            SetMovementAction(ALSMovementAction.None, true);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            UpdateAiming(dt);
            UpdateTimers(dt);

            if (MovementState == ALSMovementState.Grounded)
            {
                UpdateStance();
                GroundMove(dt);
            }
            else if (MovementState == ALSMovementState.InAir)
            {
                AirMove(dt);
            }

            SetEssentialValues(dt);

            if (MovementState == ALSMovementState.Grounded)
            {
                UpdateCharacterMovement();
                UpdateMovementDirection(dt);
                UpdateGroundedRotation(dt);
            }
            else if (MovementState == ALSMovementState.InAir)
            {
                UpdateInAirRotation(dt);
                // Perform a mantle check if falling while movement input is pressed.
                if (HasMovementInput && mantle != null && MovementState == ALSMovementState.InAir)
                {
                    mantle.FallingMantleCheck();
                }
            }

            previousVelocity = Velocity;
            previousAimYaw = AimingYaw;
        }

        // ------------------------------------------------------------------------------------------
        // State setters
        // ------------------------------------------------------------------------------------------

        public void SetMovementState(ALSMovementState newState, bool force = false)
        {
            if (!force && MovementState == newState)
            {
                return;
            }
            PrevMovementState = MovementState;
            MovementState = newState;

            if (MovementState == ALSMovementState.InAir)
            {
                if (MovementAction == ALSMovementAction.None)
                {
                    // If the character enters the air, set the In Air Rotation and uncrouch if crouched.
                    InAirYaw = ActorYaw;
                    if (Stance == ALSStance.Crouching)
                    {
                        wantsToCrouch = false;
                        SetStance(ALSStance.Standing);
                    }
                }
                else if (MovementAction == ALSMovementAction.Rolling)
                {
                    // If the character is currently rolling, enable the ragdoll.
                    RagdollStart();
                    return;
                }
            }

            MovementStateChanged?.Invoke(PrevMovementState);
        }

        public void SetMovementAction(ALSMovementAction newAction, bool force = false)
        {
            if (!force && MovementAction == newAction)
            {
                return;
            }
            ALSMovementAction prev = MovementAction;
            MovementAction = newAction;

            // Make the character crouch if performing a roll.
            if (MovementAction == ALSMovementAction.Rolling)
            {
                wantsToCrouch = true;
                SetStance(ALSStance.Crouching);
            }
            if (prev == ALSMovementAction.Rolling)
            {
                wantsToCrouch = desiredStance == ALSStance.Crouching;
            }

            MovementActionChanged?.Invoke(prev);
        }

        public void SetStance(ALSStance newStance, bool force = false)
        {
            if (!force && Stance == newStance)
            {
                return;
            }
            ALSStance prev = Stance;
            Stance = newStance;
            ApplyCapsuleHeight(Stance == ALSStance.Crouching ? crouchingHeight : standingHeight);
            StanceChanged?.Invoke(prev);
        }

        public void SetGait(ALSGait newGait, bool force = false)
        {
            if (!force && Gait == newGait)
            {
                return;
            }
            ALSGait prev = Gait;
            Gait = newGait;
            GaitChanged?.Invoke(prev);
        }

        public void SetRotationMode(ALSRotationMode newMode, bool force = false)
        {
            if (!force && RotationMode == newMode)
            {
                return;
            }
            ALSRotationMode prev = RotationMode;
            RotationMode = newMode;
            RotationModeChanged?.Invoke(prev);
        }

        public void SetDesiredRotationMode(ALSRotationMode newMode)
        {
            desiredRotationMode = newMode;
            if (!aiming)
            {
                SetRotationMode(newMode);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Input actions (ALSBaseCharacter::*Action_Implementation)
        // ------------------------------------------------------------------------------------------

        public void JumpAction(bool pressed)
        {
            if (!pressed)
            {
                return;
            }

            // Jump Action: end the ragdoll if ragdolling, mantle if a ledge is in reach,
            // stand up if crouching, or jump if standing.
            if (MovementAction != ALSMovementAction.None)
            {
                return;
            }

            if (MovementState == ALSMovementState.Ragdoll)
            {
                RagdollEnd();
                return;
            }

            if (mantle != null && mantle.OnJumpInput())
            {
                return;
            }

            if (MovementState == ALSMovementState.Grounded)
            {
                if (Stance == ALSStance.Standing)
                {
                    Jump();
                }
                else
                {
                    desiredStance = ALSStance.Standing;
                    wantsToCrouch = false;
                }
            }
        }

        public void SprintAction(bool pressed)
        {
            desiredGait = pressed ? ALSGait.Sprinting : ALSGait.Running;
        }

        public void WalkAction()
        {
            if (desiredGait == ALSGait.Walking)
            {
                desiredGait = ALSGait.Running;
            }
            else if (desiredGait == ALSGait.Running)
            {
                desiredGait = ALSGait.Walking;
            }
        }

        public void AimAction(bool pressed)
        {
            aiming = pressed;
            SetRotationMode(pressed ? ALSRotationMode.Aiming : desiredRotationMode);
        }

        public void StanceAction()
        {
            // Stance Action: toggle Standing / Crouching, double tap to Roll.
            if (MovementAction != ALSMovementAction.None)
            {
                return;
            }

            float prevInputTime = lastStanceInputTime;
            lastStanceInputTime = Time.time;

            if (lastStanceInputTime - prevInputTime <= rollDoubleTapTimeout)
            {
                // Roll, and revert the stance toggle made by the first tap.
                desiredStance = Stance == ALSStance.Standing ? ALSStance.Crouching : ALSStance.Standing;
                StartRoll(rollPlayRate);
                return;
            }

            if (MovementState == ALSMovementState.Grounded)
            {
                if (Stance == ALSStance.Standing)
                {
                    desiredStance = ALSStance.Crouching;
                    wantsToCrouch = true;
                }
                else
                {
                    desiredStance = ALSStance.Standing;
                    wantsToCrouch = false;
                }
            }
        }

        public void RollAction()
        {
            StartRoll(rollPlayRate);
        }

        public void RagdollAction()
        {
            if (MovementState == ALSMovementState.Ragdoll)
            {
                RagdollEnd();
            }
            else
            {
                RagdollStart();
            }
        }

        public void SwitchShoulderAction()
        {
            IsRightShoulder = !IsRightShoulder;
        }

        public void ResetToSpawn()
        {
            if (MovementState == ALSMovementState.Ragdoll && ragdoll != null)
            {
                ragdoll.End(out _, out _, out _);
                if (anim != null)
                {
                    anim.OnRagdollEnd(false, false, false);
                }
            }
            if (mantle != null)
            {
                mantle.Cancel();
            }
            Teleport(spawnPosition, spawnYaw);
            SetMovementAction(ALSMovementAction.None);
            SetMovementState(ALSMovementState.Grounded, true);
        }

        public void Teleport(Vector3 position, float yaw)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = wasEnabled || MovementState != ALSMovementState.Ragdoll;
            ActorYaw = yaw;
            TargetYaw = yaw;
            Velocity = Vector3.zero;
            previousVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        // ------------------------------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------------------------------

        public void Jump()
        {
            verticalVelocity = jumpVelocity;
            Velocity = new Vector3(Velocity.x, verticalVelocity, Velocity.z);
            SetMovementState(ALSMovementState.InAir);
            // Set the new In Air Rotation to the velocity rotation if speed is greater than 1 m/s.
            InAirYaw = Speed > 1f ? LastVelocityYaw : ActorYaw;
            Jumped?.Invoke();
        }

        public void StartRoll(float playRate)
        {
            if (MovementAction != ALSMovementAction.None || MovementState != ALSMovementState.Grounded || anim == null)
            {
                return;
            }
            actionTimer = anim.PlayRoll(playRate);
            SetMovementAction(ALSMovementAction.Rolling);
        }

        public void RagdollStart()
        {
            if (MovementState == ALSMovementState.Ragdoll || ragdoll == null)
            {
                return;
            }
            if (mantle != null)
            {
                mantle.Cancel();
            }
            Vector3 startVelocity = Velocity;
            actionTimer = 0f;
            SetMovementAction(ALSMovementAction.None);
            SetMovementState(ALSMovementState.Ragdoll);
            controller.enabled = false;
            if (anim != null)
            {
                anim.OnRagdollStart();
            }
            ragdoll.Begin(startVelocity);
        }

        public void RagdollEnd()
        {
            if (MovementState != ALSMovementState.Ragdoll || ragdoll == null)
            {
                return;
            }

            ragdoll.End(out bool onGround, out bool faceUp, out Vector3 ragdollVelocity);
            ActorYaw = transform.eulerAngles.y;
            TargetYaw = ActorYaw;
            controller.enabled = true;

            // If the ragdoll is on the ground, go back to walking and play a Get Up animation. If not, start
            // falling and carry over the last ragdoll velocity.
            if (onGround)
            {
                Velocity = Vector3.zero;
                verticalVelocity = 0f;
                SetMovementState(ALSMovementState.Grounded);
                if (anim != null)
                {
                    actionTimer = anim.OnRagdollEnd(true, faceUp, true);
                    SetMovementAction(ALSMovementAction.GettingUp);
                }
            }
            else
            {
                Velocity = ragdollVelocity;
                verticalVelocity = ragdollVelocity.y;
                SetMovementState(ALSMovementState.InAir);
                if (anim != null)
                {
                    anim.OnRagdollEnd(false, faceUp, true);
                }
            }
            previousVelocity = Velocity;
        }

        // Called by ALSMantle.
        public void OnMantleStart(ALSMantleType type)
        {
            Velocity = Vector3.zero;
            verticalVelocity = 0f;
            controller.enabled = false;
            SetMovementState(ALSMovementState.Mantling);
            SetMovementAction(type == ALSMantleType.HighMantle ? ALSMovementAction.HighMantle : ALSMovementAction.LowMantle);
            if (Stance == ALSStance.Crouching)
            {
                wantsToCrouch = false;
                desiredStance = ALSStance.Standing;
                SetStance(ALSStance.Standing);
            }
        }

        public void OnMantleEnd()
        {
            controller.enabled = true;
            ActorYaw = transform.eulerAngles.y;
            TargetYaw = ActorYaw;
            if (MovementState == ALSMovementState.Mantling)
            {
                SetMovementState(ALSMovementState.Grounded);
            }
            SetMovementAction(ALSMovementAction.None);
        }

        public void SetActorLocationAndTargetRotation(Vector3 position, float yaw)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            ActorYaw = yaw;
            TargetYaw = yaw;
        }

        /// <summary>Adds turn-in-place rotation (the ALS "RotationAmount" curve equivalent).</summary>
        public void AddTurnInPlaceYaw(float deltaYaw)
        {
            TargetYaw = Mathf.Repeat(TargetYaw + deltaYaw + 180f, 360f) - 180f;
            ApplyYaw(TargetYaw);
        }

        // ------------------------------------------------------------------------------------------
        // Essential values
        // ------------------------------------------------------------------------------------------

        private void UpdateAiming(float dt)
        {
            // Interp AimingRotation to current control rotation for smooth character rotation movement.
            AimingYaw = ALSMath.AngleInterpTo(AimingYaw, ControlYaw, dt, 30f);
            AimingPitch = ALSMath.AngleInterpTo(AimingPitch, ControlPitch, dt, 30f);
        }

        private void UpdateTimers(float dt)
        {
            if (landFrictionTimer > 0f)
            {
                landFrictionTimer -= dt;
                if (landFrictionTimer <= 0f)
                {
                    brakingFrictionFactor = 0f;
                }
            }

            if (MovementAction == ALSMovementAction.Rolling || MovementAction == ALSMovementAction.GettingUp)
            {
                actionTimer -= dt;
                if (actionTimer <= 0f)
                {
                    SetMovementAction(ALSMovementAction.None);
                }
            }
        }

        private void SetEssentialValues(float dt)
        {
            if (MovementState == ALSMovementState.Ragdoll && ragdoll != null)
            {
                Velocity = ragdoll.Velocity;
            }

            // These values represent how the capsule is moving as well as how it wants to move, and therefore
            // are essential for any data driven animation system.
            Acceleration = (Velocity - previousVelocity) / dt;

            // The Speed equals the length of the horizontal velocity, so it does not take vertical movement
            // into account. If the character is moving, update the last velocity rotation.
            Vector3 horizontal = ALSMath.Flatten(Velocity);
            Speed = horizontal.magnitude;
            IsMoving = Speed > 0.01f;
            if (IsMoving)
            {
                LastVelocityYaw = ALSMath.YawOf(horizontal);
            }

            // The Movement Input Amount is equal to the current acceleration divided by the max acceleration
            // so that it has a range of 0-1.
            MovementInputAmount = MaxAcceleration > 0f ? CurrentAcceleration.magnitude / MaxAcceleration : 0f;
            HasMovementInput = MovementInputAmount > 0f;
            if (HasMovementInput)
            {
                LastMovementInputYaw = ALSMath.YawOf(CurrentAcceleration);
            }

            // The Aim Yaw Rate represents the speed the camera is rotating left to right.
            AimYawRate = Mathf.Abs(Mathf.DeltaAngle(previousAimYaw, AimingYaw) / dt);
        }

        private Vector3 GetInputDirection()
        {
            if (MovementAction != ALSMovementAction.None)
            {
                return Vector3.zero;
            }
            // Default camera relative movement behavior
            Vector3 input = Quaternion.Euler(0f, AimingYaw, 0f) * new Vector3(MoveInput.x, 0f, MoveInput.y);
            return Vector3.ClampMagnitude(input, 1f);
        }

        // ------------------------------------------------------------------------------------------
        // Movement
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Map the character's current speed to the configured movement speeds with a range of 0-3,
        /// with 0 = stopped, 1 = the Walk Speed, 2 = the Run Speed, and 3 = the Sprint Speed.
        /// </summary>
        public float GetMappedSpeed()
        {
            float speed = ALSMath.Flatten(Velocity).magnitude;
            ALSMovementSettings s = CurrentSettings;
            if (speed > s.runSpeed)
            {
                return ALSMath.MapRangeClamped(s.runSpeed, s.sprintSpeed, 2f, 3f, speed);
            }
            if (speed > s.walkSpeed)
            {
                return ALSMath.MapRangeClamped(s.walkSpeed, s.runSpeed, 1f, 2f, speed);
            }
            return ALSMath.MapRangeClamped(0f, s.walkSpeed, 0f, 1f, speed);
        }

        private void GroundMove(float dt)
        {
            // Update acceleration, deceleration and friction using the movement curves.
            float mappedSpeed = GetMappedSpeed();
            MaxAcceleration = movementModel.maxAcceleration.Evaluate(mappedSpeed);
            MaxBrakingDeceleration = movementModel.brakingDeceleration.Evaluate(mappedSpeed);
            float friction = movementModel.groundFriction.Evaluate(mappedSpeed);

            CurrentAcceleration = GetInputDirection() * MaxAcceleration;

            Vector3 horizontal = ALSMath.Flatten(Velocity);
            bool rootMotion = MovementAction == ALSMovementAction.Rolling || MovementAction == ALSMovementAction.GettingUp;
            if (rootMotion && anim != null)
            {
                horizontal = ALSMath.Flatten(anim.ConsumeRootMotion()) / dt;
            }
            else
            {
                CalcVelocity(ref horizontal, CurrentAcceleration, GetMaxGroundSpeed(), friction, MaxBrakingDeceleration, dt);
            }

            // Walk along the ground plane so the horizontal speed stays the same on slopes.
            Vector3 delta = horizontal * dt;
            float plannedY = 0f;
            if (GroundNormal.y > 0.01f)
            {
                plannedY = -(GroundNormal.x * delta.x + GroundNormal.z * delta.z) / GroundNormal.y;
            }
            delta.y = plannedY;

            Vector3 start = transform.position;
            UpdateStepOffset(delta);
            controller.Move(delta);

            // Find the floor, snap down to it (stairs, slopes) or start falling.
            float snapDistance = defaultStepOffset + 0.05f;
            if (ProbeGround(snapDistance, out float gap, out Vector3 normal, out bool walkable) && walkable)
            {
                GroundNormal = normal;
                if (gap > 0.002f)
                {
                    controller.Move(Vector3.down * gap);
                }
            }
            else
            {
                Vector3 movedBeforeFall = transform.position - start;
                Velocity = ALSMath.Flatten(movedBeforeFall) / dt;
                verticalVelocity = 0f;
                GroundNormal = Vector3.up;
                SetMovementState(ALSMovementState.InAir);
                return;
            }

            // The velocity follows what the capsule actually did (walls slow it down and redirect it), but
            // being pushed out of geometry never adds speed, and the short snags of the capsule on stair
            // steps do not cost any.
            Vector3 moved = transform.position - start;
            PendingStepOffset += moved.y - plannedY;
            Vector3 actual = Vector3.ClampMagnitude(ALSMath.Flatten(moved) / dt, horizontal.magnitude);
            bool blocked = actual.sqrMagnitude < horizontal.sqrMagnitude * 0.81f;
            blockedTime = blocked ? blockedTime + dt : 0f;
            bool wasMoving = Velocity.sqrMagnitude > 1f;
            Velocity = blocked && wasMoving && blockedTime < BlockedTolerance ? horizontal : actual;
        }

        private float GetMaxGroundSpeed()
        {
            float maxSpeed = CurrentSettings.GetSpeedForGait(AllowedGait);
            if (RotationMode == ALSRotationMode.VelocityDirection || Stance != ALSStance.Standing ||
                CurrentAcceleration.sqrMagnitude < 1e-6f)
            {
                return maxSpeed;
            }

            // The animation set only has slow side and back steps, so the character only runs when heading
            // (roughly) where the camera looks, and walks while aiming.
            if (RotationMode == ALSRotationMode.Aiming)
            {
                maxSpeed = Mathf.Min(maxSpeed, CurrentSettings.walkSpeed);
            }
            float delta = Mathf.Abs(Mathf.DeltaAngle(AimingYaw, ALSMath.YawOf(CurrentAcceleration)));
            if (delta > 110f)
            {
                return Mathf.Min(maxSpeed, backwardSpeed);
            }
            if (delta > 70f)
            {
                return Mathf.Min(maxSpeed, strafeSpeed);
            }
            return maxSpeed;
        }

        private void AirMove(float dt)
        {
            MaxAcceleration = airAcceleration;
            CurrentAcceleration = GetInputDirection() * MaxAcceleration;

            // Lateral air control: no friction and no braking while falling.
            Vector3 horizontal = ALSMath.Flatten(Velocity);
            Vector3 lateralAcceleration = CurrentAcceleration * airControl;
            if (lateralAcceleration.sqrMagnitude > 1e-8f)
            {
                float maxSpeed = CurrentSettings.GetSpeedForGait(AllowedGait);
                float limit = Mathf.Max(horizontal.magnitude, maxSpeed);
                horizontal = Vector3.ClampMagnitude(horizontal + lateralAcceleration * dt, limit);
            }

            float oldVertical = verticalVelocity;
            verticalVelocity = Mathf.Max(verticalVelocity - gravity * dt, -terminalVelocity);

            Vector3 start = transform.position;
            Vector3 delta = horizontal * dt;
            delta.y = 0.5f * (oldVertical + verticalVelocity) * dt;
            CollisionFlags flags = controller.Move(delta);

            Vector3 moved = transform.position - start;
            horizontal = ALSMath.Flatten(moved) / dt;
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            {
                verticalVelocity = 0f;
            }
            Velocity = new Vector3(horizontal.x, verticalVelocity, horizontal.z);

            if (verticalVelocity > 0f)
            {
                return;
            }

            if (ProbeGround(0.03f, out _, out Vector3 normal, out bool walkable))
            {
                if (walkable)
                {
                    float fallSpeed = -oldVertical;
                    verticalVelocity = 0f;
                    Velocity = horizontal;
                    GroundNormal = normal;
                    SetMovementState(ALSMovementState.Grounded);
                    EventOnLanded(fallSpeed);
                }
                else if ((flags & CollisionFlags.Below) != 0)
                {
                    // Resting on a surface that is too steep to walk on: slide along it.
                    Vector3 slide = Vector3.ProjectOnPlane(Velocity, normal);
                    verticalVelocity = Mathf.Min(slide.y, 0f);
                    Velocity = new Vector3(slide.x, verticalVelocity, slide.z);
                }
            }
        }

        /// <summary>
        /// The CharacterController lifts the capsule by the step offset while it moves, so it catches on any
        /// ceiling lower than height + step offset (a crouch tunnel, a low doorway). Shrink the step offset to
        /// the head room that is actually available ahead.
        /// </summary>
        private void UpdateStepOffset(Vector3 delta)
        {
            float stepOffset = defaultStepOffset;
            Vector3 flat = ALSMath.Flatten(delta);
            float distance = flat.magnitude;
            if (distance > 1e-5f)
            {
                float radius = controller.radius * 0.95f;
                Vector3 ahead = transform.position + flat / distance * (distance + 0.15f);
                Vector3 topCenter = ahead + Vector3.up * (controller.height - controller.radius);
                if (Physics.SphereCast(topCenter, radius, Vector3.up, out RaycastHit hit, defaultStepOffset + 0.1f,
                        worldMask, QueryTriggerInteraction.Ignore))
                {
                    stepOffset = Mathf.Clamp(hit.distance - 0.08f, 0.01f, defaultStepOffset);
                }
            }
            controller.stepOffset = stepOffset;
        }

        // UCharacterMovementComponent::CalcVelocity
        private void CalcVelocity(ref Vector3 velocity, Vector3 acceleration, float maxSpeed, float friction,
            float brakingDeceleration, float dt)
        {
            bool zeroAcceleration = acceleration.sqrMagnitude < 1e-8f;
            bool velocityOverMax = IsExceedingMaxSpeed(velocity, maxSpeed);

            if (zeroAcceleration || velocityOverMax)
            {
                // Only apply braking if there is no acceleration, or we are over our max speed and need to slow down to it.
                Vector3 oldVelocity = velocity;
                ApplyVelocityBraking(ref velocity, friction * brakingFrictionFactor, brakingDeceleration, dt);

                // Don't allow braking to lower us below max speed if we started above it.
                if (velocityOverMax && velocity.sqrMagnitude < maxSpeed * maxSpeed &&
                    Vector3.Dot(acceleration, oldVelocity) > 0f)
                {
                    velocity = oldVelocity.normalized * maxSpeed;
                }
            }
            else
            {
                // Friction affects our ability to change direction.
                Vector3 accelDir = acceleration.normalized;
                float velSize = velocity.magnitude;
                velocity -= (velocity - accelDir * velSize) * Mathf.Min(dt * friction, 1f);
            }

            if (!zeroAcceleration)
            {
                float newMaxSpeed = IsExceedingMaxSpeed(velocity, maxSpeed) ? velocity.magnitude : maxSpeed;
                velocity += acceleration * dt;
                velocity = Vector3.ClampMagnitude(velocity, newMaxSpeed);
            }
        }

        private static bool IsExceedingMaxSpeed(Vector3 velocity, float maxSpeed)
        {
            // Allow 1% error tolerance, to account for numeric imprecision.
            maxSpeed = Mathf.Max(0f, maxSpeed);
            return velocity.sqrMagnitude > maxSpeed * maxSpeed * 1.01f * 1.01f;
        }

        // UCharacterMovementComponent::ApplyVelocityBraking
        private static void ApplyVelocityBraking(ref Vector3 velocity, float friction, float brakingDeceleration, float dt)
        {
            if (velocity.sqrMagnitude < 1e-12f)
            {
                velocity = Vector3.zero;
                return;
            }

            friction = Mathf.Max(0f, friction);
            brakingDeceleration = Mathf.Max(0f, brakingDeceleration);
            bool zeroFriction = friction == 0f;
            bool zeroBraking = brakingDeceleration == 0f;
            if (zeroFriction && zeroBraking)
            {
                return;
            }

            Vector3 oldVelocity = velocity;
            Vector3 revAccel = zeroBraking ? Vector3.zero : -brakingDeceleration * velocity.normalized;

            // Subdivide braking to get reasonably consistent results at lower frame rates.
            const float maxTimeStep = 1f / 33f;
            float remaining = dt;
            while (remaining >= 1e-6f)
            {
                float step = remaining > maxTimeStep && !zeroFriction ? Mathf.Min(maxTimeStep, remaining * 0.5f) : remaining;
                remaining -= step;

                velocity += (-friction * velocity + revAccel) * step;

                // Don't reverse direction.
                if (Vector3.Dot(velocity, oldVelocity) <= 0f)
                {
                    velocity = Vector3.zero;
                    return;
                }
            }

            // Clamp to zero if nearly zero, or if below min threshold and braking.
            const float brakeToStopVelocity = 0.1f;
            if (velocity.sqrMagnitude <= 1e-8f ||
                (!zeroBraking && velocity.sqrMagnitude <= brakeToStopVelocity * brakeToStopVelocity))
            {
                velocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Sphere-probe below the capsule. <paramref name="gap"/> is the free distance between the capsule
        /// bottom and the surface.
        /// </summary>
        private bool ProbeGround(float maxDistance, out float gap, out Vector3 normal, out bool walkable)
        {
            float radius = controller.radius * GroundProbeRadiusScale;
            float lift = controller.radius - radius + GroundProbeLift;
            Vector3 origin = transform.position + Vector3.up * (controller.radius + GroundProbeLift);
            float castDistance = lift + maxDistance + controller.skinWidth;

            gap = 0f;
            normal = Vector3.up;
            walkable = false;

            if (!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, castDistance, worldMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            gap = Mathf.Max(0f, hit.distance - lift - controller.skinWidth);
            normal = hit.normal;
            walkable = IsWalkable(normal);

            // The sphere reports a slanted normal on step edges. Look straight down next to the contact point to
            // read the real surface normal.
            if (!walkable)
            {
                Vector3 toAxis = ALSMath.Flatten(transform.position - hit.point);
                Vector3 nudge = toAxis.sqrMagnitude > 1e-6f ? toAxis.normalized * 0.02f : Vector3.zero;
                for (int i = 0; i < 2 && !walkable; i++)
                {
                    Vector3 rayOrigin = hit.point + Vector3.up * 0.1f + (i == 0 ? -nudge : nudge);
                    if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit rayHit, 0.2f, worldMask,
                            QueryTriggerInteraction.Ignore) && IsWalkable(rayHit.normal))
                    {
                        normal = rayHit.normal;
                        walkable = true;
                    }
                }
            }
            return true;
        }

        public bool IsWalkable(Vector3 surfaceNormal)
        {
            return Vector3.Angle(surfaceNormal, Vector3.up) <= controller.slopeLimit + 0.5f;
        }

        // ------------------------------------------------------------------------------------------
        // Stance / capsule
        // ------------------------------------------------------------------------------------------

        private void UpdateStance()
        {
            if (wantsToCrouch && Stance == ALSStance.Standing)
            {
                SetStance(ALSStance.Crouching);
            }
            else if (!wantsToCrouch && Stance == ALSStance.Crouching && HasRoomToStand())
            {
                SetStance(ALSStance.Standing);
            }
        }

        private void ApplyCapsuleHeight(float height)
        {
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        public bool HasRoomToStand()
        {
            // Slightly larger than the capsule so that standing up never leaves it touching a ceiling edge.
            float radius = controller.radius + controller.skinWidth;
            Vector3 bottom = transform.position + Vector3.up * (radius + controller.skinWidth + 0.02f);
            Vector3 top = transform.position + Vector3.up * (standingHeight - controller.radius + controller.skinWidth);
            return !Physics.CheckCapsule(bottom, top, radius, worldMask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------------------------------
        // Gait
        // ------------------------------------------------------------------------------------------

        private void UpdateCharacterMovement()
        {
            // Set the Allowed Gait
            AllowedGait = GetAllowedGait();

            // Determine the Actual Gait. If it is different from the current Gait, Set the new Gait Event.
            ALSGait actualGait = GetActualGait(AllowedGait);
            if (actualGait != Gait)
            {
                SetGait(actualGait);
            }
        }

        private ALSGait GetAllowedGait()
        {
            // The Allowed Gait is the maximum Gait the character is currently allowed to be in, and can be
            // determined by the desired gait, the rotation mode, the stance, etc.
            if (Stance == ALSStance.Standing && RotationMode != ALSRotationMode.Aiming)
            {
                if (desiredGait == ALSGait.Sprinting)
                {
                    return CanSprint() ? ALSGait.Sprinting : ALSGait.Running;
                }
                return desiredGait;
            }

            // Crouching stance & Aiming rot mode has same behaviour
            return desiredGait == ALSGait.Sprinting ? ALSGait.Running : desiredGait;
        }

        private ALSGait GetActualGait(ALSGait allowedGait)
        {
            // The Actual Gait is calculated by the actual movement of the character, so it can be different from
            // the desired or allowed gait. For instance, if the Allowed Gait becomes walking, the Actual gait
            // will still be running until the character decelerates to the walking speed.
            float walkSpeed = CurrentSettings.walkSpeed;
            float runSpeed = CurrentSettings.runSpeed;

            if (Speed > runSpeed + 0.1f)
            {
                return allowedGait == ALSGait.Sprinting ? ALSGait.Sprinting : ALSGait.Running;
            }
            if (Speed >= walkSpeed + 0.1f)
            {
                return ALSGait.Running;
            }
            return ALSGait.Walking;
        }

        private bool CanSprint()
        {
            // Determine if the character is currently able to sprint based on the Rotation mode and current
            // acceleration (input) rotation. In the Looking Rotation mode, only allow sprinting if there is full
            // movement input and it is faced forward relative to the camera + or - 50 degrees.
            if (!HasMovementInput || RotationMode == ALSRotationMode.Aiming)
            {
                return false;
            }

            bool validInputAmount = MovementInputAmount > 0.9f;
            if (RotationMode == ALSRotationMode.VelocityDirection)
            {
                return validInputAmount;
            }
            if (RotationMode == ALSRotationMode.LookingDirection)
            {
                float delta = Mathf.DeltaAngle(AimingYaw, ALSMath.YawOf(CurrentAcceleration));
                return validInputAmount && Mathf.Abs(delta) < 50f;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------
        // Rotation
        // ------------------------------------------------------------------------------------------

        // ALS YawOffset_FB / YawOffset_LR curves: character yaw offset over the velocity angle relative to the camera.
        private static readonly AnimationCurve YawOffsetForwardBack = ALSMath.LinearCurve(
            -180f, 0f, -120f, 60f, -90f, 0f, -60f, -60f, 0f, 0f, 60f, 60f, 90f, 0f, 120f, -60f, 180f, 0f);
        private static readonly AnimationCurve YawOffsetLeft = ALSMath.LinearCurve(
            -180f, 0f, -135f, -45f, -90f, 0f, -45f, 0f, 0f, 0f, 45f, -45f, 90f, 0f, 135f, 45f, 180f, 0f);
        private static readonly AnimationCurve YawOffsetRight = ALSMath.LinearCurve(
            -180f, 0f, -135f, -45f, -90f, 0f, -45f, 45f, 0f, 0f, 45f, 0f, 90f, 0f, 135f, 45f, 180f, 0f);

        private void UpdateMovementDirection(float dt)
        {
            float targetOffset = 0f;
            if (!UsesDirectionalMovement)
            {
                MovementDirection = ALSMovementDirection.Forward;
            }
            else if ((IsMoving && HasMovementInput) || Speed > 1.5f)
            {
                // The Movement Direction represents the direction the character is moving relative to the camera
                // during the Looking Direction / Aiming rotation modes.
                float delta = Mathf.DeltaAngle(AimingYaw, LastVelocityYaw);
                MovementDirection = ALSMath.CalculateQuadrant(MovementDirection, 70f, -70f, 110f, -110f, 5f, delta);

                if (RotationMode == ALSRotationMode.LookingDirection)
                {
                    switch (MovementDirection)
                    {
                        case ALSMovementDirection.Left:
                            targetOffset = YawOffsetLeft.Evaluate(delta);
                            break;
                        case ALSMovementDirection.Right:
                            targetOffset = YawOffsetRight.Evaluate(delta);
                            break;
                        default:
                            targetOffset = YawOffsetForwardBack.Evaluate(delta);
                            break;
                    }
                }
            }
            YawOffset = ALSMath.InterpTo(YawOffset, targetOffset, dt, yawOffsetInterpSpeed);
        }

        private void UpdateGroundedRotation(float dt)
        {
            if (MovementAction == ALSMovementAction.None)
            {
                bool canUpdateMovingRot = (IsMoving && HasMovementInput) || Speed > 1.5f;
                if (canUpdateMovingRot)
                {
                    float groundedRotationRate = CalculateGroundedRotationRate();
                    if (RotationMode == ALSRotationMode.VelocityDirection)
                    {
                        // Velocity Direction Rotation
                        SmoothCharacterRotation(LastVelocityYaw, 800f, groundedRotationRate, dt);
                    }
                    else if (RotationMode == ALSRotationMode.LookingDirection)
                    {
                        // Looking Direction Rotation
                        // Sprinting and crouching only have forward animations, so they follow the velocity.
                        float yaw = UsesDirectionalMovement ? AimingYaw + YawOffset : LastVelocityYaw;
                        SmoothCharacterRotation(yaw, 500f, groundedRotationRate, dt);
                    }
                    else
                    {
                        SmoothCharacterRotation(AimingYaw, 1000f, 20f, dt);
                    }
                }
                else if (RotationMode == ALSRotationMode.Aiming)
                {
                    // Not moving: keep the character within 100 degrees of the camera.
                    LimitRotation(-100f, 100f, 20f, dt);
                }
            }
            else if (MovementAction == ALSMovementAction.Rolling)
            {
                // Rolling Rotation
                if (HasRollInput())
                {
                    SmoothCharacterRotation(LastMovementInputYaw, 0f, 2f, dt);
                }
            }
            // Other actions are ignored...
        }

        private bool HasRollInput()
        {
            // Movement input is blocked while an action plays, so read the raw stick for roll steering.
            if (MoveInput.sqrMagnitude < 0.01f)
            {
                return false;
            }
            Vector3 input = Quaternion.Euler(0f, AimingYaw, 0f) * new Vector3(MoveInput.x, 0f, MoveInput.y);
            LastMovementInputYaw = ALSMath.YawOf(input);
            return true;
        }

        private void UpdateInAirRotation(float dt)
        {
            if (RotationMode == ALSRotationMode.Aiming)
            {
                // Aiming Rotation
                SmoothCharacterRotation(AimingYaw, 0f, 15f, dt);
                InAirYaw = ActorYaw;
            }
            else
            {
                // Velocity / Looking Direction Rotation
                SmoothCharacterRotation(InAirYaw, 0f, 5f, dt);
            }
        }

        private void SmoothCharacterRotation(float targetYaw, float targetInterpSpeed, float actorInterpSpeed, float dt)
        {
            // Interpolate the Target Rotation for extra smooth rotation behavior
            TargetYaw = ALSMath.AngleInterpConstantTo(TargetYaw, targetYaw, dt, targetInterpSpeed);
            ApplyYaw(ALSMath.AngleInterpTo(ActorYaw, TargetYaw, dt, actorInterpSpeed));
        }

        private float CalculateGroundedRotationRate()
        {
            // Calculate the rotation rate by using the current Rotation Rate Curve in the Movement Settings.
            // Using the curve in conjunction with the mapped speed gives you a high level of control over the
            // rotation rates for each speed. Increase the speed if the camera is rotating quickly for more
            // responsive rotation.
            float curveValue = movementModel.rotationRate.Evaluate(GetMappedSpeed());
            float clampedAimYawRate = ALSMath.MapRangeClamped(0f, 300f, 1f, 3f, AimYawRate);
            return curveValue * clampedAimYawRate;
        }

        private void LimitRotation(float aimYawMin, float aimYawMax, float interpSpeed, float dt)
        {
            // Prevent the character from rotating past a certain angle.
            float range = Mathf.DeltaAngle(ActorYaw, AimingYaw);
            if (range < aimYawMin || range > aimYawMax)
            {
                float targetYaw = AimingYaw + (range > 0f ? aimYawMin : aimYawMax);
                SmoothCharacterRotation(targetYaw, 0f, interpSpeed, dt);
            }
        }

        private void ApplyYaw(float yaw)
        {
            ActorYaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            transform.rotation = Quaternion.Euler(0f, ActorYaw, 0f);
        }

        // ------------------------------------------------------------------------------------------
        // Landing
        // ------------------------------------------------------------------------------------------

        private void EventOnLanded(float fallSpeed)
        {
            Landed?.Invoke(fallSpeed);

            if (ragdollOnLand && fallSpeed > ragdollOnLandVelocity)
            {
                RagdollStart();
            }
            else if (breakfallOnLand && MoveInput.sqrMagnitude > 0.01f && fallSpeed >= breakfallOnLandVelocity)
            {
                StartRoll(breakfallPlayRate);
            }
            else
            {
                // Brake harder for half a second after landing, more so without movement input.
                brakingFrictionFactor = MoveInput.sqrMagnitude > 0.01f ? 0.5f : 3f;
                landFrictionTimer = 0.5f;
            }
        }
    }
}
