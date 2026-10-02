using System;
using UnityEngine;

namespace ALSUnity
{
    /// <summary>Target speeds (m/s) for each gait. Mirrors FALSMovementSettings.</summary>
    [Serializable]
    public class ALSMovementSettings
    {
        public float walkSpeed = 1.65f;
        public float runSpeed = 3.5f;
        public float sprintSpeed = 6f;

        public ALSMovementSettings()
        {
        }

        public ALSMovementSettings(float walk, float run, float sprint)
        {
            walkSpeed = walk;
            runSpeed = run;
            sprintSpeed = sprint;
        }

        public float GetSpeedForGait(ALSGait gait)
        {
            switch (gait)
            {
                case ALSGait.Running:
                    return runSpeed;
                case ALSGait.Sprinting:
                    return sprintSpeed;
                default:
                    return walkSpeed;
            }
        }
    }

    /// <summary>
    /// Curves evaluated over the "mapped speed" (0 = stopped, 1 = walk, 2 = run, 3 = sprint), the same idea
    /// as the ALS movement / rotation-rate curves. Default key values follow ALS' "Normal" movement model,
    /// converted from centimetres to metres.
    /// </summary>
    [Serializable]
    public class ALSMovementModel
    {
        [Tooltip("Max acceleration (m/s²) over mapped speed.")]
        public AnimationCurve maxAcceleration = ALSMath.LinearCurve(0f, 20f, 1f, 20f, 2f, 20f, 2.1f, 7.5f, 3f, 7.5f);

        [Tooltip("Braking deceleration (m/s²) over mapped speed.")]
        public AnimationCurve brakingDeceleration = ALSMath.LinearCurve(0f, 15f, 1f, 15f, 2f, 12.5f, 3f, 5f);

        [Tooltip("Ground friction over mapped speed. Controls how quickly the velocity turns toward the input.")]
        public AnimationCurve groundFriction = ALSMath.LinearCurve(0f, 5f, 1f, 5f, 2f, 4f, 3f, 0.5f);

        [Tooltip("Character rotation interpolation speed over mapped speed.")]
        public AnimationCurve rotationRate = new AnimationCurve(
            new Keyframe(0f, 0.5f, 6.49f, 6.49f),
            new Keyframe(1f, 4f, 0f, 0f),
            new Keyframe(2f, 5f, 3.79f, 3.79f),
            new Keyframe(3f, 20f, 26.6f, 26.6f));
    }
}
