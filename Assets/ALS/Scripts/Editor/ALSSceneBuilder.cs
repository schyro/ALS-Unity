using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ALSUnity.EditorTools
{
    /// <summary>
    /// Generates the test scene: flat ground, mantle obstacles, slopes, stairs with drop-off platforms, an
    /// uneven patch for foot IK and a crouch tunnel. Everything is built from code so the level is easy to tweak.
    /// </summary>
    public static class ALSSceneBuilder
    {
        private const string GridTexturePath = ALSAssetPaths.TexturesFolder + "/T_Grid.png";
        private const string LabelMaterialPath = ALSAssetPaths.MaterialsFolder + "/M_Label.mat";
        private const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";

        private static Texture2D gridTexture;
        private static Material labelMaterial;
        private static Font labelFont;
        private static Transform environment;

        [MenuItem("ALS/Build Steps/4. Build Test Scene")]
        public static void Build()
        {
            GameObject characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ALSAssetPaths.CharacterPrefab);
            if (characterPrefab == null)
            {
                characterPrefab = ALSCharacterPrefabBuilder.Build();
            }

            Directory.CreateDirectory(ALSAssetPaths.MaterialsFolder);
            Directory.CreateDirectory(ALSAssetPaths.TexturesFolder);
            Directory.CreateDirectory(ALSAssetPaths.ScenesFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            gridTexture = GetGridTexture();
            labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelMaterial = GetLabelMaterial();

            Material floorMat = GetMaterial("M_Floor", new Color(0.62f, 0.65f, 0.68f));
            Material mantleMat = GetMaterial("M_Mantle", new Color(0.95f, 0.62f, 0.25f));
            Material blockedMat = GetMaterial("M_TooHigh", new Color(0.85f, 0.32f, 0.3f));
            Material slopeMat = GetMaterial("M_Slope", new Color(0.35f, 0.62f, 0.9f));
            Material steepMat = GetMaterial("M_SlopeSteep", new Color(0.55f, 0.4f, 0.85f));
            Material stairMat = GetMaterial("M_Stairs", new Color(0.4f, 0.75f, 0.5f));
            Material platformMat = GetMaterial("M_Platform", new Color(0.8f, 0.82f, 0.85f));
            Material unevenMat = GetMaterial("M_Uneven", new Color(0.85f, 0.78f, 0.45f));
            Material wallMat = GetMaterial("M_Wall", new Color(0.45f, 0.48f, 0.55f));

            environment = new GameObject("Environment").transform;

            BuildLighting();
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(90f, 1f, 90f), floorMat);
            BuildMantleObstacles(mantleMat, blockedMat);
            BuildSlopes(slopeMat, steepMat, platformMat);
            BuildStairs(stairMat, platformMat);
            BuildUnevenGround(unevenMat);
            BuildCrouchTunnel(wallMat);
            BuildMisc(wallMat, mantleMat);

            // Player, camera and HUD
            var player = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            ALSCharacter character = player.GetComponent<ALSCharacter>();

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 300f;
            camera.fieldOfView = 60f;
            cameraGo.AddComponent<AudioListener>();
            UniversalAdditionalCameraData cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraGo.transform.SetPositionAndRotation(new Vector3(0.45f, 1.9f, -3f), Quaternion.Euler(10f, 0f, 0f));
            ALSCameraController cameraController = cameraGo.AddComponent<ALSCameraController>();
            cameraController.target = character;

            var hudGo = new GameObject("HUD");
            hudGo.AddComponent<ALSHud>().character = character;

            EditorSceneManager.SaveScene(scene, ALSAssetPaths.TestScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ALSAssetPaths.TestScene, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("ALS: built " + ALSAssetPaths.TestScene);
        }

        // ------------------------------------------------------------------------------------------
        // Level sections
        // ------------------------------------------------------------------------------------------

        private static void BuildLighting()
        {
            var lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = light;

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.1f;
            RenderSettings.fog = false;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null)
            {
                var volumeGo = new GameObject("Global Volume");
                Volume volume = volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = profile;
            }
        }

        private static void BuildMantleObstacles(Material mantleMat, Material blockedMat)
        {
            // A row of blocks north of the spawn. Up to 2.5 m can be mantled from the ground (a jump plus a
            // falling catch reaches about 3.3 m), the last one is too tall.
            float[] heights = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 4f };
            Transform parent = Group("Mantle Obstacles");
            for (int i = 0; i < heights.Length; i++)
            {
                float h = heights[i];
                float x = -17.5f + i * 5f;
                bool tooHigh = h > 3.3f;
                GameObject block = Box("Block " + h.ToString("0.##", CultureInfo.InvariantCulture) + "m", new Vector3(x, h * 0.5f, 16f),
                    new Vector3(3f, h, 4f), tooHigh ? blockedMat : mantleMat, parent);
                string text = h.ToString("0.##", CultureInfo.InvariantCulture) + " m" + (tooHigh ? "\ntoo high" : "");
                Label(text, new Vector3(x, Mathf.Max(0.3f, h * 0.5f), 13.98f), Quaternion.identity, 0.6f, block.transform);
            }
            Label("MANTLE: hold W + Space", new Vector3(0f, 0.02f, 11.5f), Quaternion.Euler(90f, 0f, 0f), 0.8f, parent);
        }

        private static void BuildSlopes(Material slopeMat, Material steepMat, Material platformMat)
        {
            // Ramps east of the spawn, all rising 2 m onto a shared platform. 50 degrees is beyond the walkable
            // limit, so the character slides back down.
            float[] angles = { 10f, 20f, 30f, 40f, 50f };
            const float height = 2f;
            const float platformX = 26f;
            Transform parent = Group("Slopes");
            for (int i = 0; i < angles.Length; i++)
            {
                float length = height / Mathf.Tan(angles[i] * Mathf.Deg2Rad);
                float z = -2f - i * 4f;
                bool steep = angles[i] > 46f;
                GameObject ramp = Wedge("Ramp " + angles[i].ToString("0", CultureInfo.InvariantCulture) + "deg", new Vector3(platformX - length, 0f, z), 90f,
                    3f, height, length, steep ? steepMat : slopeMat, parent);
                Label(angles[i].ToString("0", CultureInfo.InvariantCulture) + "°" + (steep ? "\ntoo steep" : ""), new Vector3(platformX - length - 1.2f, 0.02f, z),
                    Quaternion.Euler(90f, 90f, 0f), 0.8f, ramp.transform);
            }
            Box("Slope Platform", new Vector3(platformX + 3f, height * 0.5f, -10f), new Vector3(6f, height, 20f), platformMat, parent);
            Label("SLOPES", new Vector3(10.5f, 0.02f, -10f), Quaternion.Euler(90f, 90f, 0f), 0.8f, parent);
        }

        private static void BuildStairs(Material stairMat, Material platformMat)
        {
            // West of the spawn: stairs up to a 3 m platform (drop = breakfall roll), then steeper stairs up to
            // a 6.5 m platform (drop = ragdoll).
            Transform parent = Group("Stairs");

            const float lowHeight = 3f;
            Stairs("Stairs A", new Vector3(-12f, 0f, -3.5f), -90f, 3f, 0.2f, 0.3f, 15, stairMat, parent);
            Box("Platform 3m", new Vector3(-20.5f, lowHeight * 0.5f, -4f), new Vector3(8f, lowHeight, 10f), platformMat, parent);
            Label("3 m  drop: breakfall roll", new Vector3(-16.48f, 2.2f, -4f), Quaternion.Euler(0f, -90f, 0f), 0.5f, parent);

            const float highHeight = 6.5f;
            Stairs("Stairs B", new Vector3(-23.5f, lowHeight, -3.2f), 0f, 2f, 0.25f, 0.3f, 14, stairMat, parent);
            Box("Platform 6.5m", new Vector3(-20.5f, highHeight * 0.5f, 5f), new Vector3(8f, highHeight, 8f), platformMat, parent);
            Label("6.5 m  drop: ragdoll", new Vector3(-16.48f, 5.6f, 5f), Quaternion.Euler(0f, -90f, 0f), 0.5f, parent);

            Label("STAIRS", new Vector3(-10.5f, 0.02f, -3.5f), Quaternion.Euler(90f, -90f, 0f), 0.8f, parent);
        }

        private static void BuildUnevenGround(Material unevenMat)
        {
            // South of the spawn: tilted slabs and low steps to show the foot IK.
            Transform parent = Group("Uneven Ground");
            var random = new System.Random(12345);
            for (int ix = 0; ix < 6; ix++)
            {
                for (int iz = 0; iz < 5; iz++)
                {
                    float h = 0.04f + (float)random.NextDouble() * 0.14f;
                    float tiltX = ((float)random.NextDouble() - 0.5f) * 20f;
                    float tiltZ = ((float)random.NextDouble() - 0.5f) * 20f;
                    Vector3 position = new Vector3(-3.5f + ix * 1.4f, h * 0.5f - 0.05f, -10f - iz * 1.4f);
                    GameObject slab = Box("Slab", position, new Vector3(1.4f, h + 0.25f, 1.4f), unevenMat, parent);
                    slab.transform.rotation = Quaternion.Euler(tiltX, 0f, tiltZ);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                float h = 0.15f * (i + 1);
                Box("Low Step", new Vector3(7f + i * 0.9f, h * 0.5f, -12f), new Vector3(0.9f, h, 4f), unevenMat, parent);
            }
            Label("UNEVEN GROUND: foot IK", new Vector3(0f, 0.02f, -8.4f), Quaternion.Euler(90f, 180f, 0f), 0.8f, parent);
        }

        private static void BuildCrouchTunnel(Material wallMat)
        {
            Transform parent = Group("Crouch Tunnel");
            Vector3 center = new Vector3(-9f, 0f, -14f);
            const float clearance = 1.4f;
            Box("Tunnel Wall L", center + new Vector3(-1.4f, clearance * 0.5f, 0f), new Vector3(0.4f, clearance, 6f), wallMat, parent);
            Box("Tunnel Wall R", center + new Vector3(1.4f, clearance * 0.5f, 0f), new Vector3(0.4f, clearance, 6f), wallMat, parent);
            Box("Tunnel Roof", center + new Vector3(0f, clearance + 0.2f, 0f), new Vector3(3.2f, 0.4f, 6f), wallMat, parent);
            Label("CROUCH: C", center + new Vector3(0f, 0.02f, 4f), Quaternion.Euler(90f, 180f, 0f), 0.8f, parent);
        }

        private static void BuildMisc(Material wallMat, Material mantleMat)
        {
            Transform parent = Group("Misc");
            // Pillars and a wall to see the camera collision.
            Box("Pillar", new Vector3(5f, 2f, 5f), new Vector3(1f, 4f, 1f), wallMat, parent);
            Box("Pillar", new Vector3(-5f, 2f, 6f), new Vector3(1f, 4f, 1f), wallMat, parent);
            Box("Wall", new Vector3(9f, 1.75f, 7f), new Vector3(0.5f, 3.5f, 6f), wallMat, parent);
            // A thin wall that can be mantled onto and dropped off on the other side.
            GameObject thin = Box("Thin Wall 1m", new Vector3(-8f, 0.5f, 8f), new Vector3(4f, 1f, 0.5f), mantleMat, parent);
            Label("1 m wall", new Vector3(-8f, 0.5f, 7.73f), Quaternion.identity, 0.45f, thin.transform);
        }

        // ------------------------------------------------------------------------------------------
        // Geometry helpers
        // ------------------------------------------------------------------------------------------

        private static Transform Group(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(environment, false);
            return go.transform;
        }

        private static GameObject Box(string name, Vector3 center, Vector3 size, Material material, Transform parent = null)
        {
            var go = new GameObject(name) { isStatic = true };
            go.transform.SetParent(parent != null ? parent : environment, true);
            go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = BuildBoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>A ramp whose low edge sits at <paramref name="origin"/> and which rises along its local +Z.</summary>
        private static GameObject Wedge(string name, Vector3 origin, float yaw, float width, float height, float length,
            Material material, Transform parent)
        {
            var go = new GameObject(name) { isStatic = true };
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, yaw, 0f));
            Mesh mesh = BuildWedgeMesh(width, height, length);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// <summary>Solid stairs starting at <paramref name="origin"/> and rising along the local +Z.</summary>
        private static void Stairs(string name, Vector3 origin, float yaw, float width, float rise, float run, int steps,
            Material material, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, yaw, 0f));
            for (int i = 0; i < steps; i++)
            {
                float h = rise * (i + 1);
                GameObject step = Box("Step " + (i + 1), Vector3.zero, new Vector3(width, h, run), material, root.transform);
                step.transform.localPosition = new Vector3(0f, h * 0.5f, run * (i + 0.5f));
                step.transform.localRotation = Quaternion.identity;
            }
        }

        private static Mesh BuildBoxMesh(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];
            int v = 0;
            int t = 0;

            // Each face: normal, right axis, up axis. UVs are in metres so the grid texture keeps its scale.
            AddFace(Vector3.up, Vector3.right, Vector3.forward);
            AddFace(Vector3.down, Vector3.right, Vector3.back);
            AddFace(Vector3.forward, Vector3.left, Vector3.up);
            AddFace(Vector3.back, Vector3.right, Vector3.up);
            AddFace(Vector3.right, Vector3.forward, Vector3.up);
            AddFace(Vector3.left, Vector3.back, Vector3.up);

            var mesh = new Mesh { name = "Box" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;

            void AddFace(Vector3 normal, Vector3 right, Vector3 up)
            {
                float extentN = Mathf.Abs(Vector3.Dot(normal, h));
                float extentR = Mathf.Abs(Vector3.Dot(right, h));
                float extentU = Mathf.Abs(Vector3.Dot(up, h));
                Vector3 center = normal * extentN;
                Vector3 r = right * extentR;
                Vector3 u = up * extentU;
                vertices[v] = center - r - u;
                vertices[v + 1] = center - r + u;
                vertices[v + 2] = center + r + u;
                vertices[v + 3] = center + r - u;
                for (int i = 0; i < 4; i++)
                {
                    normals[v + i] = normal;
                }
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, extentU * 2f);
                uvs[v + 2] = new Vector2(extentR * 2f, extentU * 2f);
                uvs[v + 3] = new Vector2(extentR * 2f, 0f);
                // Wind so the face points along its normal.
                bool flip = Vector3.Dot(Vector3.Cross(up, right), normal) < 0f;
                if (!flip)
                {
                    triangles[t++] = v; triangles[t++] = v + 1; triangles[t++] = v + 2;
                    triangles[t++] = v; triangles[t++] = v + 2; triangles[t++] = v + 3;
                }
                else
                {
                    triangles[t++] = v; triangles[t++] = v + 2; triangles[t++] = v + 1;
                    triangles[t++] = v; triangles[t++] = v + 3; triangles[t++] = v + 2;
                }
                v += 4;
            }
        }

        private static Mesh BuildWedgeMesh(float width, float height, float length)
        {
            float w = width * 0.5f;
            float slope = Mathf.Sqrt(height * height + length * length);
            Vector3 a0 = new Vector3(-w, 0f, 0f);
            Vector3 a1 = new Vector3(w, 0f, 0f);
            Vector3 b0 = new Vector3(-w, 0f, length);
            Vector3 b1 = new Vector3(w, 0f, length);
            Vector3 c0 = new Vector3(-w, height, length);
            Vector3 c1 = new Vector3(w, height, length);

            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            // Slope
            Quad(a0, c0, c1, a1, new Vector2(0f, 0f), new Vector2(0f, slope), new Vector2(width, slope), new Vector2(width, 0f));
            // Back
            Quad(b1, c1, c0, b0, new Vector2(0f, 0f), new Vector2(0f, height), new Vector2(width, height), new Vector2(width, 0f));
            // Bottom
            Quad(a1, b1, b0, a0, new Vector2(0f, 0f), new Vector2(0f, length), new Vector2(width, length), new Vector2(width, 0f));
            // Sides
            Triangle(a0, b0, c0, new Vector2(0f, 0f), new Vector2(length, 0f), new Vector2(length, height));
            Triangle(a1, c1, b1, new Vector2(0f, 0f), new Vector2(length, height), new Vector2(length, 0f));

            var mesh = new Mesh { name = "Wedge" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
            {
                int i = vertices.Count;
                vertices.Add(p0); vertices.Add(p1); vertices.Add(p2); vertices.Add(p3);
                uvs.Add(u0); uvs.Add(u1); uvs.Add(u2); uvs.Add(u3);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
                triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
            }

            void Triangle(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 u0, Vector2 u1, Vector2 u2)
            {
                int i = vertices.Count;
                vertices.Add(p0); vertices.Add(p1); vertices.Add(p2);
                uvs.Add(u0); uvs.Add(u1); uvs.Add(u2);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            }
        }

        private static void Label(string text, Vector3 position, Quaternion rotation, float height, Transform parent)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(position, rotation);
            TextMesh textMesh = go.AddComponent<TextMesh>();
            textMesh.font = labelFont;
            textMesh.text = text;
            textMesh.fontSize = 96;
            textMesh.characterSize = height * 0.1f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.1f, 0.12f, 0.15f);
            go.GetComponent<MeshRenderer>().sharedMaterial = labelMaterial;
        }

        // ------------------------------------------------------------------------------------------
        // Assets
        // ------------------------------------------------------------------------------------------

        private static Material GetMaterial(string name, Color color)
        {
            string path = ALSAssetPaths.MaterialsFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", gridTexture);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetLabelMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(LabelMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("ALS/Label3D"));
                AssetDatabase.CreateAsset(material, LabelMaterialPath);
            }
            material.mainTexture = labelFont.material.mainTexture;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A 1 m prototype grid tile: light surface, a dark border and a faint half-metre cross.</summary>
        private static Texture2D GetGridTexture()
        {
            if (!File.Exists(GridTexturePath))
            {
                const int size = 256;
                var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                        int half = Mathf.Min(Mathf.Abs(x - size / 2), Mathf.Abs(y - size / 2));
                        float value = 0.92f;
                        if (edge < 3)
                        {
                            value = 0.62f;
                        }
                        else if (half < 1)
                        {
                            value = 0.8f;
                        }
                        texture.SetPixel(x, y, new Color(value, value, value));
                    }
                }
                texture.Apply();
                File.WriteAllBytes(GridTexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(GridTexturePath);

                var importer = (TextureImporter)AssetImporter.GetAtPath(GridTexturePath);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GridTexturePath);
        }
    }
}
