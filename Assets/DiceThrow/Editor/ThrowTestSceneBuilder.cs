using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace DiceThrow.EditorTools
{
    /// <summary>
    /// 投擲テスト用シーン（ThrowTest.unity）を生成する
    /// </summary>
    [InitializeOnLoad]
    public static class ThrowTestSceneBuilder
    {
        internal const string RootFolder = "Assets/DiceThrow";
        private const string ScenePath = RootFolder + "/Scenes/ThrowTest.unity";
        internal const string MaterialFolder = RootFolder + "/Materials";
        private const string RigPath = "Assets/Samples/XR Interaction Toolkit/3.1.3/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        private const string AutoBuiltKey = "DiceThrow.ThrowTestScene.AutoBuilt";

        private const float TableTop = 0.8f;

        internal enum ColliderMode { Keep, Convex, None }

        static ThrowTestSceneBuilder()
        {
            // シーンがまだ無ければ一度だけ自動生成する
            if (EditorPrefs.GetBool(AutoBuiltKey, false) || File.Exists(ScenePath))
                return;

            EditorApplication.delayCall += TryAutoBuild;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    TryAutoBuild();
            };
        }

        private static void TryAutoBuild()
        {
            if (EditorPrefs.GetBool(AutoBuiltKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EditorPrefs.SetBool(AutoBuiltKey, true);
            Build();
        }

        [MenuItem("Tools/Dice Throw/投擲テストシーンを作成")]
        private static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[ThrowTestSceneBuilder] Play モードを停止してから実行してください。");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(RootFolder + "/Scenes");
            EnsureFolder(MaterialFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var floorMat = GetMaterial("Floor", new Color(0.45f, 0.45f, 0.45f));
            var markerMat = GetMaterial("Marker", Color.white);
            var tableMat = GetMaterial("Table", new Color(0.55f, 0.4f, 0.25f));
            var targetMat = GetMaterial("Target", new Color(0.9f, 0.6f, 0.1f));

            CreateEnvironment(floorMat, markerMat, tableMat, targetMat);
            CreateItems();
            CreateManager();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[ThrowTestSceneBuilder] {ScenePath} を作成しました。");
        }

        internal static void CreateEnvironment(Material floorMat, Material markerMat, Material tableMat, Material targetMat, bool withTable = true)
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (rigPrefab != null)
                PrefabUtility.InstantiatePrefab(rigPrefab);
            else
                Debug.LogError($"[ThrowTestSceneBuilder] XR リグが見つかりません: {RigPath}");

            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

            // 80m 四方の床（手前 10m から奥 70m まで）
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0.0f, 0.0f, 30.0f);
            floor.transform.localScale = new Vector3(8.0f, 1.0f, 8.0f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            // 5m ごとの距離目盛り
            var markers = new GameObject("Distance Markers").transform;
            for (int distance = 5; distance <= 60; distance += 5)
            {
                var line = Part(markers, PrimitiveType.Cube, new Vector3(0.0f, 0.005f, distance), Vector3.zero, new Vector3(8.0f, 0.01f, 0.1f), markerMat, ColliderMode.None);
                line.name = $"{distance}m";
                CreateLabel(markers, $"{distance}m", new Vector3(-4.5f, 0.4f, distance), 3.0f);
            }

            if (withTable)
            {
                var table = Part(null, PrimitiveType.Cube, new Vector3(0.0f, TableTop / 2.0f, 0.75f), Vector3.zero, new Vector3(2.0f, TableTop, 0.7f), tableMat, ColliderMode.Keep);
                table.name = "Table";
            }

            // 12m 先に崩せる箱のピラミッド
            var targets = new GameObject("Target Boxes").transform;
            const float size = 0.5f;
            for (int row = 0; row < 3; row++)
            {
                for (int i = 0; i < 3 - row; i++)
                {
                    float x = (i - (2 - row) / 2.0f) * size;
                    var box = Part(targets, PrimitiveType.Cube, new Vector3(x, size / 2.0f + row * size, 12.0f), Vector3.zero, Vector3.one * size, targetMat, ColliderMode.Keep);
                    box.name = "Box";
                    box.AddComponent<Rigidbody>().mass = 2.0f;
                }
            }
        }

        private static void CreateItems()
        {
            var items = new GameObject("Items").transform;

            // 手前の列: 石・リンゴ・ロープ・盾・爆弾 / 奥の列: やり（+Z が穂先。横向きで穂先が右）
            PlaceItem(items, "Stone", new Vector3(-0.8f, TableTop + 0.045f, 0.55f), Quaternion.identity);
            PlaceItem(items, "Apple", new Vector3(-0.55f, TableTop + 0.05f, 0.55f), Quaternion.identity);
            PlaceItem(items, "Rope", new Vector3(-0.25f, TableTop + 0.045f, 0.55f), Quaternion.identity);
            PlaceItem(items, "Shield", new Vector3(0.2f, TableTop + 0.025f, 0.6f), Quaternion.identity);
            PlaceItem(items, "Bomb", new Vector3(0.75f, TableTop + 0.105f, 0.55f), Quaternion.identity);
            PlaceItem(items, "Spear", new Vector3(0.0f, TableTop + 0.03f, 0.95f), Quaternion.Euler(0.0f, 90.0f, 0.0f));

            // 机の前面に名前と重さ
            CreateItemLabel("Stone\n0.4kg", -0.8f);
            CreateItemLabel("Apple\n0.2kg", -0.55f);
            CreateItemLabel("Rope\n1.0kg", -0.25f);
            CreateItemLabel("Shield\n4.0kg", 0.2f);
            CreateItemLabel("Bomb\n3.0kg", 0.75f);
            CreateLabel(null, "Spear 2.0kg (back)", new Vector3(0.0f, 0.15f, 0.39f), 0.6f);
        }

        private static void CreateManager()
        {
            var board = CreateLabel(null, "Grab and throw!", new Vector3(0.0f, 2.3f, 5.0f), 3.0f);
            board.name = "Result Board";
            board.rectTransform.sizeDelta = new Vector2(6.0f, 2.0f);

            var manager = new GameObject("Throw Test Manager").AddComponent<ThrowTestManager>();
            manager.resultText = board;
        }

        private static void PlaceItem(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var item = CreateItem(name);
            item.transform.SetParent(parent);
            item.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// 道具を 1 つ原点に作る。さいころシーンのプレハブ作成でも使う
        /// </summary>
        internal static GameObject CreateItem(string name)
        {
            switch (name)
            {
                case "Stone":
                {
                    var stone = Item("Stone", 0.4f, 0.0f);
                    Part(stone, PrimitiveType.Sphere, Vector3.zero, Vector3.zero, new Vector3(0.09f, 0.07f, 0.08f), GetMaterial("Stone", new Color(0.35f, 0.35f, 0.38f)), ColliderMode.Keep);
                    return stone.gameObject;
                }
                case "Apple":
                {
                    var apple = Item("Apple", 0.2f, 0.15f);
                    Part(apple, PrimitiveType.Sphere, Vector3.zero, Vector3.zero, Vector3.one * 0.09f, GetMaterial("Apple", new Color(0.8f, 0.1f, 0.1f)), ColliderMode.Keep);
                    Part(apple, PrimitiveType.Cylinder, new Vector3(0.0f, 0.05f, 0.0f), Vector3.zero, new Vector3(0.01f, 0.015f, 0.01f), GetMaterial("Stem", new Color(0.3f, 0.2f, 0.1f)), ColliderMode.None);
                    return apple.gameObject;
                }
                case "Rope":
                {
                    var rope = Item("Rope", 1.0f, 0.3f);
                    Part(rope, PrimitiveType.Cylinder, Vector3.zero, Vector3.zero, new Vector3(0.25f, 0.04f, 0.25f), GetMaterial("Rope", new Color(0.75f, 0.6f, 0.35f)), ColliderMode.Convex);
                    return rope.gameObject;
                }
                case "Shield":
                {
                    var shield = Item("Shield", 4.0f, 0.1f);
                    Part(shield, PrimitiveType.Cylinder, Vector3.zero, Vector3.zero, new Vector3(0.6f, 0.02f, 0.6f), GetMaterial("Shield", new Color(0.2f, 0.35f, 0.8f)), ColliderMode.Convex);
                    return shield.gameObject;
                }
                case "Bomb":
                {
                    var bomb = Item("Bomb", 3.0f, 0.0f);
                    Part(bomb, PrimitiveType.Sphere, Vector3.zero, Vector3.zero, Vector3.one * 0.2f, GetMaterial("Bomb", new Color(0.1f, 0.1f, 0.1f)), ColliderMode.Keep);
                    Part(bomb, PrimitiveType.Cylinder, new Vector3(0.0f, 0.12f, 0.0f), Vector3.zero, new Vector3(0.02f, 0.03f, 0.02f), GetMaterial("Fuse", new Color(0.9f, 0.85f, 0.7f)), ColliderMode.None);
                    return bomb.gameObject;
                }
                case "Spear":
                {
                    // +Z が穂先
                    var spear = Item("Spear", 2.0f, 0.02f);
                    spear.GetComponent<ThrowableItem>().alignToVelocity = true;
                    var spearMat = GetMaterial("Spear", new Color(0.6f, 0.45f, 0.3f));
                    Part(spear, PrimitiveType.Cylinder, Vector3.zero, new Vector3(90.0f, 0.0f, 0.0f), new Vector3(0.035f, 0.85f, 0.035f), spearMat, ColliderMode.Keep);
                    Part(spear, PrimitiveType.Cube, new Vector3(0.0f, 0.0f, 0.9f), new Vector3(0.0f, 45.0f, 0.0f), new Vector3(0.07f, 0.02f, 0.07f), GetMaterial("SpearTip", new Color(0.8f, 0.8f, 0.85f)), ColliderMode.Keep);
                    return spear.gameObject;
                }
                default:
                    throw new System.ArgumentException($"未知の道具: {name}");
            }
        }

        private static Transform Item(string name, float mass, float drag)
        {
            var root = new GameObject(name);

            // Rigidbody と XRGrabInteractable は RequireComponent で自動追加される
            var item = root.AddComponent<ThrowableItem>();
            item.displayName = name;
            item.mass = mass;
            item.airDrag = drag;
            root.GetComponent<Rigidbody>().mass = mass;

            return root.transform;
        }

        internal static GameObject Part(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localEuler, Vector3 localScale, Material material, ColliderMode colliderMode)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(localEuler);
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;

            if (colliderMode != ColliderMode.Keep)
            {
                Object.DestroyImmediate(part.GetComponent<Collider>());

                // 平たい円柱や楕円の球はプリミティブのコライダーだと形が合わないので凸メッシュにする
                if (colliderMode == ColliderMode.Convex)
                    part.AddComponent<MeshCollider>().convex = true;
            }

            return part;
        }

        private static void CreateItemLabel(string text, float x)
        {
            CreateLabel(null, text, new Vector3(x, 0.65f, 0.39f), 0.5f);
        }

        internal static TextMeshPro CreateLabel(Transform parent, string text, Vector3 position, float fontSize)
        {
            var label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(parent);
            label.transform.position = position;
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.black;
            label.rectTransform.sizeDelta = new Vector2(2.0f, 0.5f);
            return label;
        }

        internal static Material GetMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
