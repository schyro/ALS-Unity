using UnityEngine;
using UnityEngine.InputSystem;

namespace ALSUnity
{
    /// <summary>
    /// Keyboard + mouse and gamepad bindings for the prototype. Actions are created in code so the character
    /// prefab has no dependency on an input actions asset.
    /// </summary>
    [RequireComponent(typeof(ALSCharacter))]
    [DefaultExecutionOrder(-100)]
    public class ALSPlayerInput : MonoBehaviour
    {
        [Header("Look")]
        [Tooltip("Degrees per mouse count.")]
        public float mouseSensitivity = 0.12f;
        [Tooltip("Degrees per second at full stick deflection.")]
        public float stickSensitivity = 160f;
        public float minPitch = -70f;
        public float maxPitch = 75f;
        public bool invertY;
        public bool lockCursorOnStart = true;

        private ALSCharacter character;
        private InputAction move;
        private InputAction lookMouse;
        private InputAction lookStick;
        private InputAction jump;
        private InputAction sprint;
        private InputAction walk;
        private InputAction stance;
        private InputAction roll;
        private InputAction ragdoll;
        private InputAction shoulder;
        private InputAction aim;
        private InputAction velocityMode;
        private InputAction lookingMode;
        private InputAction reset;
        private InputAction releaseCursor;
        private InputAction captureCursor;

        private void Awake()
        {
            character = GetComponent<ALSCharacter>();

            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");

            lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");

            jump = Button("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            sprint = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            walk = Button("Walk", "<Keyboard>/leftCtrl", "<Gamepad>/dpad/down");
            stance = Button("Stance", "<Keyboard>/c", "<Keyboard>/leftAlt", "<Gamepad>/buttonEast");
            roll = Button("Roll", "<Keyboard>/q", "<Gamepad>/buttonWest");
            ragdoll = Button("Ragdoll", "<Keyboard>/x", "<Gamepad>/buttonNorth");
            shoulder = Button("Shoulder", "<Keyboard>/v", "<Gamepad>/rightStickPress");
            aim = Button("Aim", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            velocityMode = Button("VelocityMode", "<Keyboard>/1", "<Gamepad>/dpad/left");
            lookingMode = Button("LookingMode", "<Keyboard>/2", "<Gamepad>/dpad/right");
            reset = Button("Reset", "<Keyboard>/r", "<Gamepad>/select");
            releaseCursor = Button("ReleaseCursor", "<Keyboard>/escape");
            captureCursor = Button("CaptureCursor", "<Mouse>/leftButton");
        }

        private static InputAction Button(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (string binding in bindings)
            {
                action.AddBinding(binding);
            }
            return action;
        }

        private InputAction[] AllActions => new[]
        {
            move, lookMouse, lookStick, jump, sprint, walk, stance, roll, ragdoll, shoulder, aim, velocityMode,
            lookingMode, reset, releaseCursor, captureCursor
        };

        private void OnEnable()
        {
            foreach (InputAction action in AllActions)
            {
                action.Enable();
            }
            if (lockCursorOnStart)
            {
                SetCursorLocked(true);
            }
        }

        private void OnDisable()
        {
            foreach (InputAction action in AllActions)
            {
                action.Disable();
            }
            SetCursorLocked(false);
        }

        private void OnDestroy()
        {
            foreach (InputAction action in AllActions)
            {
                action.Dispose();
            }
        }

        private void Update()
        {
            if (releaseCursor.WasPressedThisFrame())
            {
                SetCursorLocked(false);
            }
            else if (captureCursor.WasPressedThisFrame() && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
            }

            character.MoveInput = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);

            // Camera / control rotation
            Vector2 look = lookStick.ReadValue<Vector2>() * (stickSensitivity * Time.unscaledDeltaTime);
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                look += lookMouse.ReadValue<Vector2>() * mouseSensitivity;
            }
            if (invertY)
            {
                look.y = -look.y;
            }
            character.ControlYaw = Mathf.Repeat(character.ControlYaw + look.x + 180f, 360f) - 180f;
            character.ControlPitch = Mathf.Clamp(character.ControlPitch - look.y, minPitch, maxPitch);

            if (jump.WasPressedThisFrame())
            {
                character.JumpAction(true);
            }
            if (sprint.WasPressedThisFrame())
            {
                character.SprintAction(true);
            }
            if (sprint.WasReleasedThisFrame())
            {
                character.SprintAction(false);
            }
            if (walk.WasPressedThisFrame())
            {
                character.WalkAction();
            }
            if (stance.WasPressedThisFrame())
            {
                character.StanceAction();
            }
            if (roll.WasPressedThisFrame())
            {
                character.RollAction();
            }
            if (ragdoll.WasPressedThisFrame())
            {
                character.RagdollAction();
            }
            if (shoulder.WasPressedThisFrame())
            {
                character.SwitchShoulderAction();
            }
            if (aim.WasPressedThisFrame())
            {
                character.AimAction(true);
            }
            if (aim.WasReleasedThisFrame())
            {
                character.AimAction(false);
            }
            if (velocityMode.WasPressedThisFrame())
            {
                character.SetDesiredRotationMode(ALSRotationMode.VelocityDirection);
            }
            if (lookingMode.WasPressedThisFrame())
            {
                character.SetDesiredRotationMode(ALSRotationMode.LookingDirection);
            }
            if (reset.WasPressedThisFrame())
            {
                character.ResetToSpawn();
            }
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
