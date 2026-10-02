using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ALSUnity
{
    /// <summary>
    /// On-screen controls reference and live character state. F1 toggles the panels, F2 switches between
    /// English and Turkish.
    /// </summary>
    public class ALSHud : MonoBehaviour
    {
        public enum Language
        {
            Auto,
            English,
            Turkish
        }

        public ALSCharacter character;
        public bool visible = true;
        [Tooltip("Auto follows the system language.")]
        public Language language = Language.Auto;

        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle textStyle;
        private GUIStyle keyStyle;
        private Texture2D panelTexture;
        private bool turkish;

        private static readonly string[] Keys =
        {
            "W A S D", "Mouse", "Space", "Left Shift", "Left Ctrl", "C / Left Alt", "Q", "X", "Right Mouse",
            "1 / 2", "V", "R", "F1 / F2 / Esc"
        };

        private static readonly string[] English =
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
            "Rotation mode: velocity / looking direction",
            "Switch camera shoulder",
            "Reset to start",
            "Panel / language / release the mouse"
        };

        private static readonly string[] Turkish =
        {
            "Hareket (kameraya göre)",
            "Bakış",
            "Zıpla / engele tırman (yön tuşuyla) / ayağa kalk",
            "Sprint (basılı tut)",
            "Yürüme / koşma geçişi",
            "Çömel (çift bas: yuvarlan)",
            "Yuvarlan",
            "Ragdoll aç / kapat",
            "Nişan al (basılı tut)",
            "Dönüş modu: hız yönü / bakış yönü",
            "Kamera omzunu değiştir",
            "Başlangıca dön",
            "Panel / dil / fareyi serbest bırak"
        };

        private static readonly string[] StateLabelsEnglish =
        {
            "Controls", "Character state", "State", "Action", "Gait", "Stance", "Rotation", "Speed", "Animation"
        };

        private static readonly string[] StateLabelsTurkish =
        {
            "Kontroller", "Karakter durumu", "Durum", "Eylem", "Yürüyüş", "Duruş", "Dönüş", "Hız", "Animasyon"
        };

        private void Awake()
        {
            turkish = language == Language.Turkish ||
                      (language == Language.Auto && Application.systemLanguage == SystemLanguage.Turkish);
        }

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
            if (keyboard.f2Key.wasPressedThisFrame)
            {
                turkish = !turkish;
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

            string[] descriptions = turkish ? Turkish : English;
            string[] labels = turkish ? StateLabelsTurkish : StateLabelsEnglish;

            GUILayout.BeginArea(new Rect(12f, 12f, 440f, height - 24f));
            GUILayout.BeginVertical(panelStyle);
            GUILayout.Label(labels[0], titleStyle);
            for (int i = 0; i < Keys.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Keys[i], keyStyle, GUILayout.Width(112f));
                GUILayout.Label(descriptions[i], textStyle);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();

            if (character != null)
            {
                GUILayout.BeginArea(new Rect(width - 272f, 12f, 260f, height - 24f));
                GUILayout.BeginVertical(panelStyle);
                GUILayout.Label(labels[1], titleStyle);
                StateRow(labels[2], character.MovementState.ToString());
                StateRow(labels[3], character.MovementAction.ToString());
                StateRow(labels[4], character.Gait.ToString());
                StateRow(labels[5], character.Stance.ToString());
                StateRow(labels[6], character.RotationMode.ToString());
                StateRow(labels[7], character.Speed.ToString("0.00", CultureInfo.InvariantCulture) + " m/s");
                if (character.Animation != null)
                {
                    StateRow(labels[8], character.Animation.CurrentState.ToString());
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
