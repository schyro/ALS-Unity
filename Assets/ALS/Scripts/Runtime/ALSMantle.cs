// Mantle (ledge climb) system. Trace logic and the position / correction blending follow ALS-Community's
// ALSMantleComponent (MIT, (C) 2020 Doğa Can Yanıkoğlu & LongmireLocomotion, see THIRD_PARTY_NOTICES.md).

using System;
using UnityEngine;

namespace ALSUnity
{
    [Serializable]
    public class ALSMantleTraceSettings
    {
        public float maxLedgeHeight;
        public float minLedgeHeight;
        public float reachDistance;
        public float forwardTraceRadius;
        public float downwardTraceRadius;

        public ALSMantleTraceSettings(float maxLedge, float minLedge, float reach, float forwardRadius, float downwardRadius)
        {
            maxLedgeHeight = maxLedge;
            minLedgeHeight = minLedge;
            reachDistance = reach;
            forwardTraceRadius = forwardRadius;
            downwardTraceRadius = downwardRadius;
        }
    }

    /// <summary>
    /// Describes one mantle animation. Curves are evaluated over the animation time in seconds:
    /// positionAlpha blends the capsule from the animated start offset to the ledge target,
    /// xy / z correction blend the actual start position into the animated start offset.
    /// </summary>
    [Serializable]
    public class ALSMantleAsset
    {
        public AnimationCurve positionAlpha;
        public AnimationCurve xyCorrectionAlpha;
        public AnimationCurve zCorrectionAlpha;
        [Tooltip("Length of the curves in seconds.")]
        public float duration = 1f;
        [Tooltip("Where the animation starts relative to the ledge target: x = distance behind, y = distance below.")]
        public Vector2 startingOffset = new Vector2(0.65f, 1f);
        public float lowHeight = 0.5f;
        public float lowPlayRate = 1f;
        public float lowStartPosition = 0.5f;
        public float highHeight = 1f;
        public float highPlayRate = 1f;
        public float highStartPosition;
    }

    [DefaultExecutionOrder(-40)]
    public class ALSMantle : MonoBehaviour
    {
        public ALSMantleTraceSettings groundedTraceSettings = new ALSMantleTraceSettings(2.5f, 0.4f, 0.75f, 0.3f, 0.3f);
        public ALSMantleTraceSettings fallingTraceSettings = new ALSMantleTraceSettings(1.5f, 0.4f, 0.7f, 0.3f, 0.3f);

        [Tooltip("Ledges above this height use the high mantle.")]
        public float highMantleHeight = 1.25f;

        [Tooltip("Low mantle / falling catch. Matches the 1 m climb clip: its root moves linearly from the start offset to the ledge.")]
        public ALSMantleAsset climb = new ALSMantleAsset
        {
            positionAlpha = ALSMath.LinearCurve(0f, 0f, 0.667f, 1f),
            xyCorrectionAlpha = ALSMath.EaseCurve(0f, 0f, 0.2f, 1f, 0.667f, 1f),
            zCorrectionAlpha = ALSMath.EaseCurve(0f, 0f, 0.2f, 1f, 0.667f, 1f),
            duration = 0.667f,
            startingOffset = new Vector2(0.65f, 1f),
            lowHeight = 0.5f, lowPlayRate = 1f, lowStartPosition = 0.333f,
            highHeight = 1f, highPlayRate = 1f, highStartPosition = 0f
        };

        [Tooltip("Initial blend from the actual start position into the position / correction curves.")]
        public AnimationCurve timelineBlendIn = ALSMath.LinearCurve(0f, 0f, 0.2f, 1f);

        [Header("High mantle")]
        [Tooltip("The high mantle is a jump up to the ledge that hands over to the climb clip at this clip time (seconds).")]
        public float highMantleClipEntry = 0.15f;
        public float highMantlePlayRate = 1f;
        public float highMantleMinJumpTime = 0.28f;
        public float highMantleMaxJumpTime = 0.7f;
        [Tooltip("How long before the end of the jump the climb clip starts blending in.")]
        public float highMantleClipLead = 0.1f;

        [Header("Misc")]
        [Tooltip("Ledges moving faster than this (m/s) can't be mantled.")]
        public float acceptableVelocityWhileMantling = 0.1f;
        public bool drawDebug;

        public bool IsMantling { get; private set; }
        public ALSMantleType CurrentType { get; private set; }

        private ALSCharacter character;
        private CharacterController controller;

        private float startingPosition;
        private float playRate;
        private float timelineTime;
        private float timelineLength;

        private Transform ledge;
        private Vector3 ledgeLocalPosition;
        private Quaternion ledgeLocalRotation;
        private Vector3 targetPosition;
        private float targetYaw;
        private Vector3 actualStartOffset;
        private float actualStartYawOffset;
        private Vector3 animatedStartOffset;

        // High mantle
        private bool clipStarted;
        private float elapsed;
        private float jumpDuration;
        private float clipPhaseDuration;
        private float clipEntryAlpha;
        private Vector3 startPosition;
        private float startYaw;

        private void Awake()
        {
            character = GetComponent<ALSCharacter>();
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (!IsMantling)
            {
                return;
            }

            UpdateTargetFromLedge();

            if (CurrentType == ALSMantleType.HighMantle)
            {
                HighMantleUpdate(Time.deltaTime);
                return;
            }

            timelineTime += Time.deltaTime * playRate;
            MantleUpdate(timelineBlendIn.Evaluate(Mathf.Min(timelineTime, timelineLength)));
            if (timelineTime >= timelineLength)
            {
                MantleEnd();
            }
        }

        /// <summary>Called when jump is pressed. Returns true if a mantle started.</summary>
        public bool OnJumpInput()
        {
            if (character.MovementAction != ALSMovementAction.None)
            {
                return false;
            }
            if (character.MovementState == ALSMovementState.Grounded)
            {
                return character.MoveInput.sqrMagnitude > 0.01f && MantleCheck(groundedTraceSettings);
            }
            if (character.MovementState == ALSMovementState.InAir)
            {
                return MantleCheck(fallingTraceSettings);
            }
            return false;
        }

        public bool FallingMantleCheck()
        {
            return MantleCheck(fallingTraceSettings);
        }

        public void Cancel()
        {
            if (!IsMantling)
            {
                return;
            }
            IsMantling = false;
            character.OnMantleEnd();
        }

        public bool MantleCheck(ALSMantleTraceSettings trace)
        {
            if (IsMantling)
            {
                return false;
            }

            int mask = character.worldMask;

            // Step 1: Trace forward to find a wall / object the character cannot walk on.
            Vector3 traceDirection = character.HasMovementInput
                ? ALSMath.DirectionOfYaw(character.LastMovementInputYaw)
                : transform.forward;
            Vector3 capsuleBase = transform.position + Vector3.up * 0.02f;
            Vector3 traceStart = capsuleBase - traceDirection * 0.3f;
            traceStart.y += (trace.maxLedgeHeight + trace.minLedgeHeight) * 0.5f;
            float halfHeight = 0.01f + (trace.maxLedgeHeight - trace.minLedgeHeight) * 0.5f;
            float pointOffset = Mathf.Max(0f, halfHeight - trace.forwardTraceRadius);

            if (!Physics.CapsuleCast(traceStart + Vector3.up * pointOffset, traceStart - Vector3.up * pointOffset,
                    trace.forwardTraceRadius, traceDirection, out RaycastHit forwardHit, trace.reachDistance, mask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            // The capsule reports a slanted normal when it touches the top edge of a low ledge, so read the
            // normal of the wall face itself with a ray just below the contact point.
            Vector3 hitNormal = forwardHit.normal;
            Vector3 rayOrigin = forwardHit.point - traceDirection * 0.3f - Vector3.up * 0.05f;
            if (Physics.Raycast(rayOrigin, traceDirection, out RaycastHit faceHit, 0.6f, mask, QueryTriggerInteraction.Ignore))
            {
                hitNormal = faceHit.normal;
            }
            if (character.IsWalkable(hitNormal))
            {
                // Not a valid surface to mantle
                return false;
            }
            if (forwardHit.rigidbody != null &&
                forwardHit.rigidbody.linearVelocity.magnitude > acceptableVelocityWhileMantling)
            {
                // The surface to mantle moves too fast
                return false;
            }

            Vector3 initialImpactPoint = forwardHit.point;
            Vector3 wallNormal = ALSMath.Flatten(hitNormal);
            if (wallNormal.sqrMagnitude < 1e-4f)
            {
                return false;
            }
            wallNormal.Normalize();

            // Step 2: Trace downward from the first trace's Impact Point and determine if the hit location is walkable.
            Vector3 downwardEnd = new Vector3(initialImpactPoint.x, capsuleBase.y, initialImpactPoint.z) - wallNormal * 0.15f;
            Vector3 downwardStart = downwardEnd + Vector3.up * (trace.maxLedgeHeight + trace.downwardTraceRadius + 0.01f);

            if (Physics.CheckSphere(downwardStart, trace.downwardTraceRadius, mask, QueryTriggerInteraction.Ignore))
            {
                // The wall is taller than anything we can reach.
                return false;
            }
            if (!Physics.SphereCast(downwardStart, trace.downwardTraceRadius, Vector3.down, out RaycastHit downHit,
                    downwardStart.y - downwardEnd.y, mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (!character.IsWalkable(downHit.normal))
            {
                // Not a valid surface to mantle
                return false;
            }

            Vector3 downTraceLocation = new Vector3(downwardStart.x, downHit.point.y + controller.skinWidth, downwardStart.z);

            // Step 3: Check if the capsule has room to stand at the downward trace's location.
            // If so, set that location as the Target Transform and calculate the mantle height.
            if (!CapsuleHasRoom(downTraceLocation + Vector3.up * 0.02f, mask))
            {
                return false;
            }

            float mantleHeight = downTraceLocation.y - transform.position.y;
            if (mantleHeight < trace.minLedgeHeight * 0.5f)
            {
                return false;
            }

            // Step 4: Determine the Mantle Type by checking the movement mode and Mantle Height.
            ALSMantleType type;
            if (character.MovementState == ALSMovementState.InAir)
            {
                type = ALSMantleType.FallingCatch;
            }
            else
            {
                type = mantleHeight > highMantleHeight ? ALSMantleType.HighMantle : ALSMantleType.LowMantle;
            }

            if (drawDebug)
            {
                Debug.DrawLine(traceStart, initialImpactPoint, Color.yellow, 2f);
                Debug.DrawLine(downwardStart, downTraceLocation, Color.cyan, 2f);
                Debug.DrawRay(downTraceLocation, Vector3.up * 1.8f, Color.green, 2f);
            }

            // Step 5: If everything checks out, start the Mantle
            MantleStart(mantleHeight, downHit.collider.transform, downTraceLocation, ALSMath.YawOf(-wallNormal), type);
            return true;
        }

        private bool CapsuleHasRoom(Vector3 basePosition, int mask)
        {
            float radius = controller.radius;
            Vector3 bottom = basePosition + Vector3.up * (radius + 0.01f);
            Vector3 top = basePosition + Vector3.up * (character.standingHeight - radius);
            return !Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore);
        }

        private void MantleStart(float mantleHeight, Transform ledgeTransform, Vector3 ledgePosition, float ledgeYaw,
            ALSMantleType type)
        {
            // Step 1: Use the Mantle Asset to set the new Mantle Params.
            startingPosition = ALSMath.MapRangeClamped(climb.lowHeight, climb.highHeight,
                climb.lowStartPosition, climb.highStartPosition, mantleHeight);
            playRate = ALSMath.MapRangeClamped(climb.lowHeight, climb.highHeight,
                climb.lowPlayRate, climb.highPlayRate, mantleHeight);

            // Step 2: Convert the world space target to the mantle component's local space for use in moving objects.
            ledge = ledgeTransform;
            ledgeLocalPosition = ledge.InverseTransformPoint(ledgePosition);
            ledgeLocalRotation = Quaternion.Inverse(ledge.rotation) * Quaternion.Euler(0f, ledgeYaw, 0f);

            // Step 3: Set the Mantle Target and calculate the Starting Offset
            // (offset amount between the actor and target transform).
            targetPosition = ledgePosition;
            targetYaw = ledgeYaw;
            startPosition = transform.position;
            startYaw = character.ActorYaw;
            actualStartOffset = startPosition - targetPosition;
            actualStartYawOffset = Mathf.DeltaAngle(targetYaw, startYaw);

            // Step 4: Calculate the Animated Start Offset from the Target Location.
            // This would be the location the actual animation starts at relative to the Target Transform.
            animatedStartOffset = GetAnimatedStartOffset();

            // Step 5: Clear the Character Movement Mode and set the Movement State to Mantling
            CurrentType = type;
            IsMantling = true;
            character.OnMantleStart(type);

            ALSCharacterAnimation anim = character.Animation;
            if (type == ALSMantleType.HighMantle)
            {
                // The animation library has no tall climb, so tall ledges are a jump up to the ledge followed by
                // the 1 m climb clip from the moment its hands reach the edge.
                playRate = highMantlePlayRate;
                clipEntryAlpha = Mathf.Clamp01(highMantleClipEntry / climb.duration);
                float clipHeight = climb.startingOffset.y * (1f - clipEntryAlpha);
                float jumpHeight = Mathf.Max(0f, mantleHeight - clipHeight);
                jumpDuration = Mathf.Clamp(Mathf.Sqrt(2f * jumpHeight / Mathf.Max(0.1f, character.gravity)),
                    highMantleMinJumpTime, highMantleMaxJumpTime);
                clipPhaseDuration = (climb.duration - highMantleClipEntry) / playRate;
                elapsed = 0f;
                clipStarted = false;
                if (anim != null)
                {
                    anim.PlayMantleJump();
                }
                return;
            }

            // Step 6: Configure the Mantle Timeline so that it is the same length as the Lerp/Correction curve
            // minus the starting position, and plays at the same speed as the animation. Then start the timeline.
            timelineLength = Mathf.Max(0.05f, climb.duration - startingPosition);
            timelineTime = 0f;

            // Step 7: Play the mantle animation.
            if (anim != null)
            {
                anim.PlayMantle(startingPosition / climb.duration, playRate, anim.mantleBlendInTime);
            }
        }

        private Vector3 GetAnimatedStartOffset()
        {
            return -ALSMath.DirectionOfYaw(targetYaw) * climb.startingOffset.x - Vector3.up * climb.startingOffset.y;
        }

        private void UpdateTargetFromLedge()
        {
            // Continually update the mantle target from the stored local transform to follow along with moving objects.
            if (ledge == null)
            {
                return;
            }
            targetPosition = ledge.TransformPoint(ledgeLocalPosition);
            targetYaw = (ledge.rotation * ledgeLocalRotation).eulerAngles.y;
            animatedStartOffset = GetAnimatedStartOffset();
        }

        private void MantleUpdate(float blendIn)
        {
            // Step 2: Update the Position and Correction Alphas using the Position/Correction curve set for each Mantle.
            float curveTime = startingPosition + Mathf.Min(timelineTime, timelineLength);
            float positionAlpha = climb.positionAlpha.Evaluate(curveTime);
            float xyCorrectionAlpha = climb.xyCorrectionAlpha.Evaluate(curveTime);
            float zCorrectionAlpha = climb.zCorrectionAlpha.Evaluate(curveTime);

            // Step 3: Lerp multiple transforms together for independent control over the horizontal and vertical
            // blend to the animated start position, as well as the target position.

            // Blend into the animated horizontal and rotation offset using the XY correction alpha.
            Vector3 targetHz = new Vector3(animatedStartOffset.x, actualStartOffset.y, animatedStartOffset.z);
            Vector3 hzResult = Vector3.Lerp(actualStartOffset, targetHz, xyCorrectionAlpha);
            float hzYaw = Mathf.Lerp(actualStartYawOffset, 0f, xyCorrectionAlpha);

            // Blend into the animated vertical offset using the Z correction alpha.
            float vtResult = Mathf.Lerp(actualStartOffset.y, animatedStartOffset.y, zCorrectionAlpha);

            Vector3 resultOffset = new Vector3(hzResult.x, vtResult, hzResult.z);

            // Blend from the currently blending transforms into the final mantle target using the position alpha.
            Vector3 resultPosition = Vector3.Lerp(targetPosition + resultOffset, targetPosition, positionAlpha);
            float resultYaw = Mathf.Lerp(hzYaw, 0f, positionAlpha);

            // Initial Blend In (controlled in the timeline curve) to allow the actor to blend into the
            // Position/Correction curve at the midpoint. This prevents pops when mantling an object lower than
            // the animated mantle.
            Vector3 finalPosition = Vector3.Lerp(targetPosition + actualStartOffset, resultPosition, blendIn);
            float finalYaw = targetYaw + Mathf.Lerp(actualStartYawOffset, resultYaw, blendIn);

            // Step 4: Set the actors location and rotation to the Lerped Target.
            character.SetActorLocationAndTargetRotation(finalPosition, finalYaw);
        }

        private void HighMantleUpdate(float dt)
        {
            elapsed += dt;
            ALSCharacterAnimation anim = character.Animation;

            if (elapsed < jumpDuration)
            {
                // Jump: ballistic rise to the point where the climb clip takes over.
                float t = elapsed / jumpDuration;
                Vector3 entryPosition = targetPosition + animatedStartOffset * (1f - clipEntryAlpha);
                float rise = 2f * t - t * t;
                float slide = Mathf.SmoothStep(0f, 1f, t);
                Vector3 position = Vector3.Lerp(startPosition, entryPosition, slide);
                position.y = Mathf.Lerp(startPosition.y, entryPosition.y, rise);
                float yaw = startYaw + Mathf.DeltaAngle(startYaw, targetYaw) * slide;
                character.SetActorLocationAndTargetRotation(position, yaw);

                float lead = Mathf.Min(highMantleClipLead, highMantleClipEntry / playRate);
                if (!clipStarted && elapsed >= jumpDuration - lead && anim != null)
                {
                    clipStarted = true;
                    anim.PlayMantle((highMantleClipEntry - lead * playRate) / climb.duration, playRate, lead + 0.05f);
                }
                return;
            }

            if (!clipStarted && anim != null)
            {
                clipStarted = true;
                anim.PlayMantle(clipEntryAlpha, playRate, 0.1f);
            }

            // Climb: follow the clip's (linear) root motion onto the ledge.
            float u = Mathf.Clamp01((elapsed - jumpDuration) / clipPhaseDuration);
            float alpha = Mathf.Lerp(clipEntryAlpha, 1f, u);
            character.SetActorLocationAndTargetRotation(
                Vector3.Lerp(targetPosition + animatedStartOffset, targetPosition, alpha), targetYaw);
            if (u >= 1f)
            {
                MantleEnd();
            }
        }

        private void MantleEnd()
        {
            IsMantling = false;
            character.OnMantleEnd();
        }
    }
}
