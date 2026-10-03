using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ALSUnity
{
    /// <summary>
    /// On-screen controls reference and live character state. F1 toggles the panels.
    /// </summary>
    public class ALSHud : MonoBehaviour
    {
        public ALSCharacter character;
        public bool visible = true;

        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle textStyle;
        private GUIStyle keyStyle;
        private Texture2D panelTexture;

        private static readonly string[] Keys =
        {
            "W A S D", "Mouse", "Space", "Left Shift", "Left Ctrl", "C / Left Alt", "Q", "X", "Right Mouse",
            "1 / 2", "V", "R", "F1 / Esc"
        };

        private static readonly string[] Descriptions =
        {
            "Move (camera relative)",
            "Look",
            "Jump / mantle a ledge (hold a direction) / get up",
            "Sprint (hold)",
            "Toggle walk / run",
            "Crouch (double tap: roll)",
            "Roll",
            "Ragdoll on / off",
            "Aim (hold)",
            "Rotation mode: velocity direction / looking direction",
            "Switch camera shoulder",
            "Reset to start",
            "Panel / release the mouse"
        };

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            if (keyboard.f1Key.wasPressedThisFrame)
            {
                visible = !visible;
            }
        }

        private void OnDestroy()
        {
            if (panelTexture != null)
            {
                Destroy(panelTexture);
            }
        }

        private void EnsureStyles()
        {
            if (panelStyle != null)
            {
                return;
            }

            panelTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            panelTexture.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.72f));
            panelTexture.Apply();

            panelStyle = new GUIStyle
            {
                padding = new RectOffset(14, 14, 10, 12)
            };
            panelStyle.normal.background = panelTexture;

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);

            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            textStyle.normal.textColor = new Color(0.92f, 0.94f, 0.96f);

            keyStyle = new GUIStyle(textStyle) { fontStyle = FontStyle.Bold };
            keyStyle.normal.textColor = new Color(0.55f, 0.85f, 1f);
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }
            EnsureStyles();

            // Scale the UI with the screen height so it stays readable at high resolutions.
            float scale = Mathf.Max(1f, Screen.height / 900f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            GUILayout.BeginArea(new Rect(12f, 12f, 440f, height - 24f));
            GUILayout.BeginVertical(panelStyle);
            GUILayout.Label("Controls", titleStyle);
            for (int i = 0; i < Keys.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Keys[i], keyStyle, GUILayout.Width(112f));
                GUILayout.Label(Descriptions[i], textStyle);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();

            if (character != null)
            {
                GUILayout.BeginArea(new Rect(width - 272f, 12f, 260f, height - 24f));
                GUILayout.BeginVertical(panelStyle);
                GUILayout.Label("Character state", titleStyle);
                StateRow("State", character.MovementState.ToString());
                StateRow("Action", character.MovementAction.ToString());
                StateRow("Gait", character.Gait.ToString());
                StateRow("Stance", character.Stance.ToString());
                StateRow("Rotation", character.RotationMode.ToString());
                StateRow("Speed", character.Speed.ToString("0.00", CultureInfo.InvariantCulture) + " m/s");
                if (character.Animation != null)
                {
                    StateRow("Animation", character.Animation.CurrentState.ToString());
                    StateRow("Play rate", character.Animation.PlayRate.ToString("0.00", CultureInfo.InvariantCulture));
                    StateRow("Stride", character.Animation.StrideScale.ToString("0.00", CultureInfo.InvariantCulture));
                }
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }

            GUI.matrix = previousMatrix;
        }

        private void StateRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, keyStyle, GUILayout.Width(90f));
            GUILayout.Label(value, textStyle);
            GUILayout.EndHorizontal();
        }
    }
}
