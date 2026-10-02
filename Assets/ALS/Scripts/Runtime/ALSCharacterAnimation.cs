// Animation driver: turns the character's essential values into Animator states, lean, landing response,
// turn in place, head look and foot IK. The value calculations follow ALS-Community's ALSCharacterAnimInstance
// (MIT, (C) 2020 Doğa Can Yanıkoğlu & LongmireLocomotion, see THIRD_PARTY_NOTICES.md); the Unreal AnimGraph is
// replaced by a small code-driven Mecanim state set.

using UnityEngine;

namespace ALSUnity
{
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(-20)]
    public class ALSCharacterAnimation : MonoBehaviour
    {
        // Names match the states created by ALSAnimatorBuilder.
        public enum AnimState
        {
            None,
            Idle,
            Move,
            Strafe,
            CrouchIdle,
            CrouchMove,
            JumpStart,
            Fall,
            Land,
            Roll,
            Mantle,
            GetUpBack,
            GetUpFront,
            TurnLeft90,
            TurnRight90,
            TurnLeft180,
            TurnRight180
        }

        [Header("Locomotion blending")]
        public float moveBlendTime = 0.2f;
        public float stopBlendTime = 0.25f;
        public float stanceBlendTime = 0.25f;
        public float directionBlendTime = 0.25f;
        [Tooltip("Slowest playback used for the walk cycle when moving slower than the walk speed.")]
        public float minMoveRate = 0.55f;
        [Tooltip("Ground speed (m/s) of the crouch walk clip at playback rate 1.")]
        public float crouchClipSpeed = 0.75f;
        [Tooltip("Ground speed (m/s) of the strafe blend tree at playback rate 1.")]
        public float strafeClipSpeed = 1.2f;
        [Tooltip("Damping (seconds) of the speed that drives the locomotion blend.")]
        public float speedDampTime = 0.06f;

        [Header("Jump / fall / land")]
        [Tooltip("Normalized time the jump start clip is entered at (skips the anticipation crouch).")]
        public float jumpStartOffset = 0.07f;
        public float jumpBlendTime = 0.1f;
        public float fallBlendTime = 0.3f;
        public float landBlendTime = 0.1f;
        [Tooltip("Normalized time the land clip is entered at.")]
        public float landStartOffset;
        [Tooltip("Normalized time of the land clip at which locomotion takes over again.")]
        public float landExitTime = 0.62f;
        [Tooltip("Fall speed (m/s) that plays the full landing clip when standing still.")]
        public float landClipMinFallSpeed = 3f;

        [Header("Roll")]
        [Tooltip("Distance covered by a roll at playback rate 1.")]
        public float rollDistance = 3.6f;
        [Tooltip("Roll travel (0-1) over the normalized clip time.")]
        public AnimationCurve rollTravel = ALSMath.LinearCurve(0f, 0f, 0.06f, 0.02f, 0.5f, 0.88f, 0.66f, 1f, 1f, 1f);
        [Tooltip("Normalized clip time at which the roll action ends and locomotion blends back in.")]
        public float rollExitTime = 0.72f;
        public float rollBlendOutTime = 0.25f;

        [Header("Mantle")]
        public float mantleBlendInTime = 0.12f;
        public float mantleBlendOutTime = 0.25f;

        [Header("Get up")]
        public float getUpExitTime = 0.9f;
        public float getUpBlendOutTime = 0.3f;
        [Tooltip("Face down get-up: normalized time the push-up clip is entered at (bottom of the push-up).")]
        public float getUpFrontStart = 0.5f;
        [Tooltip("Face down get-up: normalized time of the push-up clip at which it hands over to the crouch.")]
        public float getUpFrontEnd = 0.95f;
        public float getUpFrontPlayRate = 1.15f;
        [Tooltip("Face down get-up: blend time from the push-up into the landing crouch, which then stands up.")]
        public float getUpFrontCrouchBlend = 0.4f;
        public float getUpFrontCrouchOffset = 0.15f;

        [Header("Turn in place (Looking Direction / Aiming, standing still)")]
        public float turnCheckMinAngle = 45f;
        public float turn180Threshold = 130f;
        public float aimYawRateLimit = 50f;
        [Tooltip("Delay before turning when the camera is just past the minimum angle / at 180 degrees.")]
        public float minAngleDelay = 0.75f;
        public float maxAngleDelay;
        public float turnPlayRate = 1.6f;
        public float turnBlendTime = 0.2f;
        [Tooltip("Normalized clip time at which the turn hands back to idle.")]
        public float turnExitTime = 0.82f;

        [Header("Lean")]
        public float leanInterpSpeed = 4f;
        public float maxLeanSide = 9f;
        public float maxLeanForward = 5f;
        public float airLeanPivotHeight = 0.9f;
        [Tooltip("Multiplier on the in-air lean over the vertical velocity (m/s).")]
        public AnimationCurve leanInAir = ALSMath.SmoothCurve(-40f, 0f, -32.8f, -0.12f, -22.5f, -0.93f, -12.5f, -0.79f, 0f, 1f, 10f, 1f);

        [Header("Head look")]
        public bool enableHeadLook = true;
        [Range(0f, 1f)] public float headLookWeight = 0.75f;
        [Tooltip("The head stops following the camera when it points further than this away from the body.")]
        public float headLookMaxAngle = 100f;
        public float headLookInterpSpeed = 5f;

        [Header("Foot IK")]
        public bool enableFootIK = true;
        public float ikTraceAboveFoot = 0.5f;
        public float ikTraceBelowFoot = 0.45f;
        public float maxFootTilt = 35f;
        public float ikWeightInterpSpeed = 10f;

        [Header("Landing response")]
        public float landDipStiffness = 180f;
        public float landDipDamping = 24f;
        public float maxLandDip = 0.3f;

        [Header("Step smoothing")]
        public float stepSmoothSpeed = 12f;

        // Clip data (seconds / degrees), filled in by the editor build step.
        [Header("Clip data (set by the builder)")]
        public float jumpStartLength = 1.333f;
        public float landLength = 1.267f;
        public float rollLength = 1.467f;
        public float mantleLength = 0.667f;
        public float getUpBackLength = 1.533f;
        public float getUpFrontLength = 1.5f;
        public float turn90Length = 2f;
        public float turn180Length = 2f;
        [Tooltip("How far (0-1) each turn clip has rotated over its normalized time. Order: left 90, right 90, left 180, right 180.")]
        public AnimationCurve[] turnRotationCurves =
        {
            ALSMath.EaseCurve(0f, 0f, 0.8f, 1f, 1f, 1f), ALSMath.EaseCurve(0f, 0f, 0.8f, 1f, 1f, 1f),
            ALSMath.EaseCurve(0f, 0f, 0.8f, 1f, 1f, 1f), ALSMath.EaseCurve(0f, 0f, 0.8f, 1f, 1f, 1f)
        };

        public AnimState CurrentState { get; private set; } = AnimState.None;
        public Vector2 LeanAmount => lean;
        public float FootIKWeight => ikWeight;
        public Animator Animator => animator;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int MoveRateParam = Animator.StringToHash("MoveRate");
        private static readonly int CrouchRateParam = Animator.StringToHash("CrouchRate");
        private static readonly int StrafeRateParam = Animator.StringToHash("StrafeRate");
        private static readonly int ActionRateParam = Animator.StringToHash("ActionRate");
        private static readonly int DirectionXParam = Animator.StringToHash("DirectionX");
        private static readonly int DirectionYParam = Animator.StringToHash("DirectionY");

        private int[] stateHashes;

        private ALSCharacter character;
        private Animator animator;
        private Transform head;
        private Vector3 baseLocalPosition;

        // Current state timing
        private float stateTime;
        private float stateRate = 1f;
        private float stateLength = 1f;
        private float stateStartOffset;

        // A state queued to follow the current one (used by the face down get-up).
        private AnimState queuedState = AnimState.None;
        private float queuedDelay;
        private float queuedBlend;
        private float queuedOffset;
        private float queuedLength;

        private Vector2 lean;
        private Vector2 strafeDirection = new Vector2(0f, 1f);
        private float stepOffset;
        private float landDip;
        private float landDipVelocity;
        private float rollPreviousTravel;
        private float turnDelay;
        private float turnAngle;
        private float turnProgress;
        private AnimationCurve turnCurve;
        private float lookWeight;

        private float ikWeight;
        private float pelvisOffset;
        private FootIK leftFoot;
        private FootIK rightFoot;

        private struct FootIK
        {
            public float offset;
            public float target;
            public Quaternion rotation;
        }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            character = GetComponentInParent<ALSCharacter>();
            head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            leftFoot.rotation = Quaternion.identity;
            rightFoot.rotation = Quaternion.identity;

            string[] names = System.Enum.GetNames(typeof(AnimState));
            stateHashes = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                stateHashes[i] = Animator.StringToHash(names[i]);
            }
        }

        private void OnEnable()
        {
            if (character != null)
            {
                character.Jumped += OnJumped;
                character.Landed += OnLanded;
            }
        }

        private void OnDisable()
        {
            if (character != null)
            {
                character.Jumped -= OnJumped;
                character.Landed -= OnLanded;
            }
        }

        private void Start()
        {
            baseLocalPosition = new Vector3(0f, -character.Controller.skinWidth, 0f);
            transform.localPosition = baseLocalPosition;
            SetState(AnimState.Idle, 0f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || character.MovementState == ALSMovementState.Ragdoll)
            {
                return;
            }

            stateTime += dt;
            UpdateQueuedState();
            UpdateTurnRotation();
            UpdateStateMachine(dt);
            UpdateParameters(dt);
            UpdateLean(dt);
            UpdateLandDip(dt);
            UpdateModelTransform(dt);
        }

        // ------------------------------------------------------------------------------------------
        // State selection
        // ------------------------------------------------------------------------------------------

        private float StateNormalizedTime => stateStartOffset + stateTime * stateRate / Mathf.Max(0.01f, stateLength);

        private static bool IsTurnState(AnimState state)
        {
            return state >= AnimState.TurnLeft90 && state <= AnimState.TurnRight180;
        }

        private static bool IsCrouchState(AnimState state)
        {
            return state == AnimState.CrouchIdle || state == AnimState.CrouchMove;
        }

        private bool IsLocomotionState(AnimState state)
        {
            return state == AnimState.Idle || state == AnimState.Move || state == AnimState.Strafe ||
                   IsCrouchState(state) || state == AnimState.Land || IsTurnState(state);
        }

        private void UpdateStateMachine(float dt)
        {
            // Actions own the animation until the character clears them.
            if (character.MovementState == ALSMovementState.Mantling ||
                character.MovementAction == ALSMovementAction.Rolling ||
                character.MovementAction == ALSMovementAction.GettingUp)
            {
                return;
            }

            if (character.MovementState == ALSMovementState.InAir)
            {
                if (CurrentState == AnimState.JumpStart)
                {
                    // Hand over to the fall loop at the apex of the jump.
                    if (character.Velocity.y < 0f || StateNormalizedTime >= 0.95f)
                    {
                        SetState(AnimState.Fall, fallBlendTime);
                    }
                }
                else if (CurrentState != AnimState.Fall)
                {
                    SetState(AnimState.Fall, fallBlendTime);
                }
                return;
            }

            if (character.MovementState != ALSMovementState.Grounded)
            {
                return;
            }

            bool shouldMove = ShouldMove();
            bool crouching = character.Stance == ALSStance.Crouching;

            if (!shouldMove && !crouching)
            {
                if (CurrentState == AnimState.Land && StateNormalizedTime < landExitTime)
                {
                    return;
                }
                if (IsTurnState(CurrentState))
                {
                    if (StateNormalizedTime < turnExitTime)
                    {
                        return;
                    }
                }
                else if (CurrentState == AnimState.Idle && TurnInPlaceCheck(dt))
                {
                    return;
                }
            }
            else
            {
                turnDelay = 0f;
            }

            AnimState desired;
            if (crouching)
            {
                desired = shouldMove ? AnimState.CrouchMove : AnimState.CrouchIdle;
            }
            else if (shouldMove)
            {
                // Camera relative modes: forward keeps the normal locomotion (the character turns into the
                // movement), everything else and aiming use the directional strafe blend.
                bool strafing = character.UsesDirectionalMovement &&
                                (character.RotationMode == ALSRotationMode.Aiming ||
                                 character.MovementDirection != ALSMovementDirection.Forward);
                desired = strafing ? AnimState.Strafe : AnimState.Move;
            }
            else
            {
                desired = AnimState.Idle;
            }

            if (desired != CurrentState)
            {
                SetState(desired, GetBlendTime(CurrentState, desired));
            }
        }

        private bool ShouldMove()
        {
            // Enable Movement Animations if IsMoving and HasMovementInput, or if the Speed is greater than 1.5 m/s.
            return (character.IsMoving && character.HasMovementInput) || character.Speed > 1.5f;
        }

        private float GetBlendTime(AnimState from, AnimState to)
        {
            switch (from)
            {
                case AnimState.Roll:
                    return rollBlendOutTime;
                case AnimState.Mantle:
                    return mantleBlendOutTime;
                case AnimState.GetUpBack:
                case AnimState.GetUpFront:
                    return getUpBlendOutTime;
                case AnimState.Fall:
                case AnimState.JumpStart:
                    return moveBlendTime;
                case AnimState.Land:
                    return stopBlendTime;
            }

            if (IsTurnState(from))
            {
                return to == AnimState.Idle ? stopBlendTime : moveBlendTime;
            }
            if (IsCrouchState(from) != IsCrouchState(to))
            {
                return stanceBlendTime;
            }
            if ((from == AnimState.Move && to == AnimState.Strafe) || (from == AnimState.Strafe && to == AnimState.Move))
            {
                return directionBlendTime;
            }
            return to == AnimState.Idle || to == AnimState.CrouchIdle ? stopBlendTime : moveBlendTime;
        }

        private void SetState(AnimState state, float blendTime, float normalizedOffset = 0f, float rate = 1f, float length = 1f)
        {
            CurrentState = state;
            stateTime = 0f;
            stateRate = rate;
            stateLength = length;
            stateStartOffset = normalizedOffset;
            queuedState = AnimState.None;
            if (!animator.isActiveAndEnabled)
            {
                return;
            }
            int hash = stateHashes[(int)state];
            if (blendTime <= 0f)
            {
                animator.Play(hash, 0, normalizedOffset);
            }
            else
            {
                animator.CrossFadeInFixedTime(hash, blendTime, 0, normalizedOffset * length);
            }
        }

        private void QueueState(AnimState state, float delay, float blendTime, float normalizedOffset, float length)
        {
            queuedState = state;
            queuedDelay = delay;
            queuedBlend = blendTime;
            queuedOffset = normalizedOffset;
            queuedLength = length;
        }

        private void UpdateQueuedState()
        {
            if (queuedState != AnimState.None && stateTime >= queuedDelay)
            {
                SetState(queuedState, queuedBlend, queuedOffset, 1f, queuedLength);
            }
        }

        private void UpdateParameters(float dt)
        {
            float speed = character.Speed;
            animator.SetFloat(SpeedParam, speed, speedDampTime, dt);

            // Below the walk speed the walk cycle is slowed down instead of blended with idle.
            float walkSpeed = character.standingSettings.walkSpeed;
            float moveRate = speed < walkSpeed ? Mathf.Lerp(minMoveRate, 1f, speed / walkSpeed) : 1f;
            float sprintSpeed = character.standingSettings.sprintSpeed;
            if (speed > sprintSpeed)
            {
                moveRate = speed / sprintSpeed;
            }
            animator.SetFloat(MoveRateParam, moveRate);

            // Calculate the Crouching Play Rate by dividing the Character's speed by the Animated Speed.
            animator.SetFloat(CrouchRateParam, Mathf.Clamp(speed / Mathf.Max(0.01f, crouchClipSpeed), 0.4f, 3f));
            animator.SetFloat(StrafeRateParam, Mathf.Clamp(speed / Mathf.Max(0.01f, strafeClipSpeed), 0.5f, 2f));

            // Velocity direction relative to the character for the strafe blend (ALS' velocity blend).
            if (speed > 0.1f)
            {
                Vector3 local = Quaternion.Inverse(character.transform.rotation) * ALSMath.Flatten(character.Velocity).normalized;
                strafeDirection.x = ALSMath.InterpTo(strafeDirection.x, local.x, dt, 12f);
                strafeDirection.y = ALSMath.InterpTo(strafeDirection.y, local.z, dt, 12f);
            }
            animator.SetFloat(DirectionXParam, strafeDirection.x);
            animator.SetFloat(DirectionYParam, strafeDirection.y);
        }

        // ------------------------------------------------------------------------------------------
        // Turn in place (ALSCharacterAnimInstance::TurnInPlaceCheck / TurnInPlace)
        // ------------------------------------------------------------------------------------------

        private bool TurnInPlaceCheck(float dt)
        {
            if (character.RotationMode == ALSRotationMode.VelocityDirection)
            {
                turnDelay = 0f;
                return false;
            }

            // Step 1: Check if Aiming angle is outside of the Turn Check Min Angle, and if the Aim Yaw Rate is
            // below the Aim Yaw Rate Limit. If so, begin counting the Elapsed Delay Time. This ensures the
            // conditions remain true for a sustained period of time before turning in place.
            float aimAngle = Mathf.DeltaAngle(character.ActorYaw, character.AimingYaw);
            if (Mathf.Abs(aimAngle) <= turnCheckMinAngle || character.AimYawRate >= aimYawRateLimit)
            {
                turnDelay = 0f;
                return false;
            }

            turnDelay += dt;
            float delay = ALSMath.MapRangeClamped(turnCheckMinAngle, 180f, minAngleDelay, maxAngleDelay, Mathf.Abs(aimAngle));
            if (character.RotationMode == ALSRotationMode.Aiming)
            {
                delay *= 0.25f;
            }

            // Step 2: Check if the Elapsed Delay time exceeds the set delay (mapped to the turn angle range).
            // If so, trigger a Turn In Place.
            if (turnDelay <= delay)
            {
                return false;
            }

            // Step 3: Choose the turn clip based on the turn angle and play it. The rotation itself is applied
            // from the clip's rotation curve, scaled to the angle that is actually needed.
            turnDelay = 0f;
            bool small = Mathf.Abs(aimAngle) < turn180Threshold;
            AnimState state = small
                ? (aimAngle < 0f ? AnimState.TurnLeft90 : AnimState.TurnRight90)
                : (aimAngle < 0f ? AnimState.TurnLeft180 : AnimState.TurnRight180);
            turnCurve = turnRotationCurves[state - AnimState.TurnLeft90];
            turnAngle = aimAngle;
            turnProgress = 0f;
            animator.SetFloat(ActionRateParam, turnPlayRate);
            SetState(state, turnBlendTime, 0f, turnPlayRate, small ? turn90Length : turn180Length);
            return true;
        }

        private void UpdateTurnRotation()
        {
            // The equivalent of the ALS "RotationAmount" curve: rotate the character in step with the clip.
            if (!IsTurnState(CurrentState) || turnCurve == null ||
                character.MovementState != ALSMovementState.Grounded)
            {
                return;
            }
            float full = Mathf.Max(0.01f, turnCurve.Evaluate(turnExitTime));
            float progress = Mathf.Clamp01(turnCurve.Evaluate(Mathf.Clamp01(StateNormalizedTime)) / full);
            if (progress > turnProgress)
            {
                character.AddTurnInPlaceYaw((progress - turnProgress) * turnAngle);
                turnProgress = progress;
            }
        }

        private void OnAnimatorMove()
        {
            // Implementing this callback keeps the Animator from applying root motion itself: the character is
            // moved by ALSCharacter, and the turn in place rotation comes from UpdateTurnRotation.
        }

        // ------------------------------------------------------------------------------------------
        // Events and actions
        // ------------------------------------------------------------------------------------------

        private void OnJumped()
        {
            SetState(AnimState.JumpStart, jumpBlendTime, jumpStartOffset, 1f, jumpStartLength);
        }

        private void OnLanded(float fallSpeed)
        {
            // Compress on impact; the feet stay planted through the foot IK.
            landDipVelocity += ALSMath.MapRangeClamped(2f, 12f, 0.8f, 4.5f, fallSpeed);

            if (!ShouldMove() && fallSpeed >= landClipMinFallSpeed)
            {
                SetState(AnimState.Land, landBlendTime, landStartOffset, 1f, landLength);
            }
        }

        /// <summary>Starts the roll and returns how long (seconds) the roll action lasts.</summary>
        public float PlayRoll(float playRate)
        {
            animator.SetFloat(ActionRateParam, playRate);
            SetState(AnimState.Roll, 0.1f, 0f, playRate, rollLength);
            rollPreviousTravel = 0f;
            return rollLength * rollExitTime / playRate;
        }

        /// <summary>Plays the climb clip from a normalized start time.</summary>
        public void PlayMantle(float startFraction, float playRate, float blendTime)
        {
            animator.SetFloat(ActionRateParam, playRate);
            SetState(AnimState.Mantle, blendTime, Mathf.Clamp01(startFraction), playRate, mantleLength);
        }

        /// <summary>Jump phase of the high mantle.</summary>
        public void PlayMantleJump()
        {
            SetState(AnimState.JumpStart, jumpBlendTime, jumpStartOffset, 1f, jumpStartLength);
        }

        public void OnRagdollStart()
        {
            animator.enabled = false;
            CurrentState = AnimState.None;
            queuedState = AnimState.None;
            lean = Vector2.zero;
            stepOffset = 0f;
            landDip = 0f;
            landDipVelocity = 0f;
            ikWeight = 0f;
            lookWeight = 0f;
            transform.localPosition = baseLocalPosition;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>Re-enables the Animator after a ragdoll. Returns the get-up action duration.</summary>
        public float OnRagdollEnd(bool onGround, bool faceUp, bool playGetUp)
        {
            animator.enabled = true;
            if (!playGetUp)
            {
                SetState(AnimState.Idle, 0f);
                return 0f;
            }
            if (!onGround)
            {
                SetState(AnimState.Fall, 0f);
                animator.Update(0f);
                return 0f;
            }

            float duration;
            if (faceUp)
            {
                animator.SetFloat(ActionRateParam, 1f);
                SetState(AnimState.GetUpBack, 0f, 0f, 1f, getUpBackLength);
                duration = getUpBackLength * getUpExitTime;
            }
            else
            {
                // Face down: push up from the floor, then blend into the landing crouch, which stands up.
                animator.SetFloat(ActionRateParam, getUpFrontPlayRate);
                SetState(AnimState.GetUpFront, 0f, getUpFrontStart, getUpFrontPlayRate, getUpFrontLength);
                float pushUpTime = (getUpFrontEnd - getUpFrontStart) * getUpFrontLength / getUpFrontPlayRate;
                QueueState(AnimState.Land, pushUpTime, getUpFrontCrouchBlend, getUpFrontCrouchOffset, landLength);
                duration = pushUpTime + (landExitTime - getUpFrontCrouchOffset) * landLength;
            }
            animator.Update(0f);
            return duration;
        }

        /// <summary>
        /// Movement produced by the current action animation since the last call. The library clips are in
        /// place, so the travel of the roll is reconstructed from a curve.
        /// </summary>
        public Vector3 ConsumeRootMotion()
        {
            if (CurrentState != AnimState.Roll)
            {
                return Vector3.zero;
            }
            float travel = rollTravel.Evaluate(Mathf.Clamp01(StateNormalizedTime));
            float delta = Mathf.Max(0f, travel - rollPreviousTravel) * rollDistance;
            rollPreviousTravel = travel;
            return character.transform.forward * delta;
        }

        /// <summary>Camera pivot target: halfway between the head and the feet (ALS third person pivot).</summary>
        public Vector3 GetPivotPosition()
        {
            Vector3 feet = character.transform.position;
            return head != null ? (head.position + feet) * 0.5f : feet + Vector3.up * 0.9f;
        }

        public Vector3 GetHeadPosition()
        {
            return head != null ? head.position : character.transform.position + Vector3.up * 1.6f;
        }

        // ------------------------------------------------------------------------------------------
        // Lean, landing dip, step smoothing
        // ------------------------------------------------------------------------------------------

        private void UpdateLean(float dt)
        {
            Vector2 target = Vector2.zero;
            Quaternion inverseRotation = Quaternion.Inverse(character.transform.rotation);

            if (character.MovementState == ALSMovementState.Grounded && character.MovementAction == ALSMovementAction.None)
            {
                // The Relative Acceleration Amount represents the current amount of acceleration / deceleration
                // relative to the actor rotation, normalized to -1..1 by the max acceleration / deceleration.
                Vector3 acceleration = ALSMath.Flatten(character.Acceleration);
                float limit = Vector3.Dot(acceleration, character.Velocity) > 0f
                    ? character.MaxAcceleration
                    : character.MaxBrakingDeceleration;
                if (limit > 0.01f)
                {
                    Vector3 relative = inverseRotation * (Vector3.ClampMagnitude(acceleration, limit) / limit);
                    target = new Vector2(relative.x, relative.z);
                }
            }
            else if (character.MovementState == ALSMovementState.InAir)
            {
                // Use the relative Velocity direction and amount to determine how much the character should
                // lean while in air. The Lean In Air curve reverses the lean when moving downwards.
                Vector3 relative = inverseRotation * ALSMath.Flatten(character.Velocity) / 3.5f;
                float multiplier = leanInAir.Evaluate(character.Velocity.y);
                target = Vector2.ClampMagnitude(new Vector2(relative.x, relative.z) * multiplier, 1f);
            }

            lean.x = ALSMath.InterpTo(lean.x, target.x, dt, leanInterpSpeed);
            lean.y = ALSMath.InterpTo(lean.y, target.y, dt, leanInterpSpeed);
        }

        private void UpdateLandDip(float dt)
        {
            // Damped spring pushed down by landing impacts.
            float acceleration = -landDipStiffness * landDip - landDipDamping * landDipVelocity;
            landDipVelocity += acceleration * dt;
            landDip = Mathf.Clamp(landDip + landDipVelocity * dt, 0f, maxLandDip);
            if (landDip <= 0f && landDipVelocity < 0f)
            {
                landDipVelocity = 0f;
            }
        }

        private void UpdateModelTransform(float dt)
        {
            // Smooth out the vertical pops of stair steps and ledge snaps on the visual mesh only.
            if (character.MovementState == ALSMovementState.Grounded)
            {
                stepOffset -= character.PendingStepOffset;
                stepOffset = Mathf.Clamp(stepOffset, -0.6f, 0.6f);
            }
            else
            {
                stepOffset = 0f;
            }
            character.PendingStepOffset = 0f;
            stepOffset = ALSMath.InterpTo(stepOffset, 0f, dt, stepSmoothSpeed);

            bool leanAllowed = character.MovementState == ALSMovementState.Grounded ||
                               character.MovementState == ALSMovementState.InAir;
            Quaternion tilt = leanAllowed
                ? Quaternion.Euler(lean.y * maxLeanForward, 0f, -lean.x * maxLeanSide)
                : Quaternion.identity;

            // Lean around the feet on the ground and around the hips in the air.
            float pivotHeight = character.MovementState == ALSMovementState.InAir ? airLeanPivotHeight : 0f;
            Vector3 pivot = new Vector3(0f, pivotHeight, 0f);
            transform.localRotation = tilt;
            transform.localPosition = baseLocalPosition + Vector3.up * stepOffset + (pivot - tilt * pivot);
        }

        // ------------------------------------------------------------------------------------------
        // IK pass: head look and foot IK (ALSCharacterAnimInstance::UpdateFootIK / SetFootOffsets /
        // SetPelvisIKOffset)
        // ------------------------------------------------------------------------------------------

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || character == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool grounded = character.MovementState == ALSMovementState.Grounded &&
                            character.MovementAction == ALSMovementAction.None;
            bool locomotion = grounded && IsLocomotionState(CurrentState);

            UpdateHeadLook(dt, locomotion || (character.MovementState == ALSMovementState.InAir &&
                                              character.MovementAction == ALSMovementAction.None));
            UpdateFootIK(dt, enableFootIK && locomotion);
        }

        private void UpdateHeadLook(float dt, bool allowed)
        {
            // Look where the camera looks, as long as that is not behind the character.
            float aimAngle = Mathf.Abs(Mathf.DeltaAngle(character.ActorYaw, character.AimingYaw));
            bool active = enableHeadLook && allowed && aimAngle < headLookMaxAngle;
            lookWeight = ALSMath.InterpTo(lookWeight, active ? 1f : 0f, dt, headLookInterpSpeed);
            if (lookWeight <= 0.001f)
            {
                return;
            }

            Vector3 aimDirection = Quaternion.Euler(character.AimingPitch, character.AimingYaw, 0f) * Vector3.forward;
            animator.SetLookAtWeight(lookWeight * headLookWeight, 0.15f, 0.7f, 0f, 0.6f);
            animator.SetLookAtPosition(GetHeadPosition() + aimDirection * 10f);
        }

        private void UpdateFootIK(float dt, bool active)
        {
            ikWeight = ALSMath.InterpTo(ikWeight, active ? 1f : 0f, dt, ikWeightInterpSpeed);
            if (ikWeight <= 0.001f)
            {
                // Reset IK Offsets
                leftFoot.offset = rightFoot.offset = 0f;
                leftFoot.rotation = rightFoot.rotation = Quaternion.identity;
                pelvisOffset = 0f;
                return;
            }

            Vector3 leftPosition = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 rightPosition = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            Quaternion leftRotation = animator.GetIKRotation(AvatarIKGoal.LeftFoot);
            Quaternion rightRotation = animator.GetIKRotation(AvatarIKGoal.RightFoot);

            UpdateFootOffset(ref leftFoot, leftPosition, dt);
            UpdateFootOffset(ref rightFoot, rightPosition, dt);

            // Set the new Pelvis Target to be the lowest Foot Offset, and interp the current offset to it at
            // different speeds based on whether the new target is above or below the current one.
            float pelvisTarget = Mathf.Min(leftFoot.target, rightFoot.target);
            float pelvisSpeed = pelvisTarget > pelvisOffset ? 10f : 15f;
            pelvisOffset = ALSMath.InterpTo(pelvisOffset, pelvisTarget, dt, pelvisSpeed);

            animator.bodyPosition += Vector3.up * ((pelvisOffset - landDip) * ikWeight);

            ApplyFoot(AvatarIKGoal.LeftFoot, leftFoot, leftPosition, leftRotation);
            ApplyFoot(AvatarIKGoal.RightFoot, rightFoot, rightPosition, rightRotation);
        }

        private void UpdateFootOffset(ref FootIK foot, Vector3 footPosition, float dt)
        {
            // Step 1: Trace downward from the foot location to find the geometry. If the surface is walkable,
            // save the difference between the impact point and the expected (flat) floor location.
            float floorY = transform.position.y;
            Vector3 origin = new Vector3(footPosition.x, floorY + ikTraceAboveFoot, footPosition.z);
            float targetOffset = 0f;
            Quaternion targetRotation = Quaternion.identity;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ikTraceAboveFoot + ikTraceBelowFoot,
                    character.worldMask, QueryTriggerInteraction.Ignore) && character.IsWalkable(hit.normal))
            {
                targetOffset = hit.point.y - floorY;
                targetRotation = Quaternion.RotateTowards(Quaternion.identity,
                    Quaternion.FromToRotation(Vector3.up, hit.normal), maxFootTilt);
            }

            // Step 2: Interp the Current Location Offset to the new target value, at different speeds based on
            // whether the new target is above or below the current one.
            foot.target = targetOffset;
            float interpSpeed = foot.offset > targetOffset ? 30f : 15f;
            foot.offset = ALSMath.InterpTo(foot.offset, targetOffset, dt, interpSpeed);

            // Step 3: Interp the Current Rotation Offset to the new target value.
            foot.rotation = ALSMath.InterpTo(foot.rotation, targetRotation, dt, 30f);
        }

        private void ApplyFoot(AvatarIKGoal goal, FootIK foot, Vector3 animatedPosition, Quaternion animatedRotation)
        {
            animator.SetIKPositionWeight(goal, ikWeight);
            animator.SetIKRotationWeight(goal, ikWeight);
            animator.SetIKPosition(goal, animatedPosition + Vector3.up * foot.offset);
            animator.SetIKRotation(goal, foot.rotation * animatedRotation);
        }
    }
}
