using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DiceThrow.EditorTools
{
    /// <summary>
    /// さいころテスト用シーン（DiceRoll.unity）と、出目ごとの道具プレハブを生成する
    /// </summary>
    public static class DiceRollSceneBuilder
    {
        private const string ScenePath = ThrowTestSceneBuilder.RootFolder + "/Scenes/DiceRoll.unity";
        private const string PrefabFolder = ThrowTestSceneBuilder.RootFolder + "/Prefabs/Items";

        // 出目 1〜6 の順
        private static readonly string[] ItemNames = { "Stone", "Apple", "Rope", "Spear", "Shield", "Bomb" };

        private const float DiceSize = 0.06f;

        [MenuItem("Tools/Dice Throw/さいころシーンを作成")]
        private static void BuildFromMenu()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[DiceRollSceneBuilder] Play モードを停止してから実行してください。");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Build();
        }

        /// <summary>バッチモードから -executeMethod DiceThrow.EditorTools.DiceRollSceneBuilder.Build でも呼べる</summary>
        public static void Build()
        {
            ThrowTestSceneBuilder.EnsureFolder(ThrowTestSceneBuilder.RootFolder + "/Scenes");
            ThrowTestSceneBuilder.EnsureFolder(PrefabFolder);
            ThrowTestSceneBuilder.EnsureFolder(ThrowTestSceneBuilder.MaterialFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prefabs = CreateItemPrefabs();

            ThrowTestSceneBuilder.CreateEnvironment(
                ThrowTestSceneBuilder.GetMaterial("Floor", new Color(0.45f, 0.45f, 0.45f)),
                ThrowTestSceneBuilder.GetMaterial("Marker", Color.white),
                ThrowTestSceneBuilder.GetMaterial("Table", new Color(0.55f, 0.4f, 0.25f)),
                ThrowTestSceneBuilder.GetMaterial("Target", new Color(0.9f, 0.6f, 0.1f)),
                withTable: false);

            var origin = Object.FindAnyObjectByType<XROrigin>();
            var leftController = origin != null ? origin.transform.Find("Camera Offset/Left Controller") : null;
            var rightController = origin != null ? origin.transform.Find("Camera Offset/Right Controller") : null;
            var camera = origin != null ? origin.transform.Find("Camera Offset/Main Camera") : null;
            if (leftController == null || rightController == null || camera == null)
            {
                Debug.LogError("[DiceRollSceneBuilder] XR リグの Left Controller / Right Controller / Main Camera が見つかりません。");
                return;
            }

            var holder = CreateDice(leftController, rightController, prefabs);
            CreatePlayer(origin.gameObject, holder, camera);
            CreateRopeLedge();
            CreateBreakableWall();
            CreateEnemy(new Vector3(-2.5f, 0.0f, 9.0f));
            // 右の敵は離れて弾を投げてくる（盾で跳ね返せる）
            CreateEnemy(new Vector3(2.5f, 0.0f, 9.0f), ranged: true);
            CreateManager();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[DiceRollSceneBuilder] {ScenePath} を作成しました。");
        }

        private static GameObject[] CreateItemPrefabs()
        {
            var prefabs = new GameObject[ItemNames.Length];
            for (int i = 0; i < ItemNames.Length; i++)
            {
                var item = ThrowTestSceneBuilder.CreateItem(ItemNames[i]);
                AddBehaviour(item, ItemNames[i]);
                prefabs[i] = PrefabUtility.SaveAsPrefabAsset(item, $"{PrefabFolder}/{ItemNames[i]}.prefab");
                Object.DestroyImmediate(item);
            }
            return prefabs;
        }

        /// <summary>道具ごとの「投げた後の挙動」を付ける（投擲テストの道具には付けない）</summary>
        private static void AddBehaviour(GameObject item, string name)
        {
            var throwable = item.GetComponent<ThrowableItem>();

            switch (name)
            {
                case "Stone":
                    item.AddComponent<StoneBehaviour>();
                    throwable.aimTargets = ThrowTargetKind.Enemy;
                    break;
                case "Apple":
                    // リンゴは囮なので、狙いの補正はしない
                    item.AddComponent<AppleBehaviour>();
                    var lure = item.GetComponent<Lure>();
                    lure.strength = 3.0f;
                    lure.radius = 12.0f;
                    lure.edible = true;
                    break;
                case "Rope":
                    item.AddComponent<RopeBehaviour>();
                    throwable.aimTargets = ThrowTargetKind.RopeAnchor;
                    break;
                case "Spear":
                    item.AddComponent<SpearBehaviour>();
                    throwable.aimTargets = ThrowTargetKind.Enemy;
                    break;
                case "Shield":
                    item.AddComponent<ShieldBehaviour>();
                    throwable.aimTargets = ThrowTargetKind.Enemy;
                    throwable.spawnUpright = true;
                    throwable.straightFlight = true;
                    break;
                case "Bomb":
                    throwable.aimTargets = ThrowTargetKind.Enemy | ThrowTargetKind.Wall;
                    var bomb = item.AddComponent<BombBehaviour>();
                    bomb.explosionMaterial = GetTransparentMaterial("Explosion", new Color(1.0f, 0.55f, 0.1f, 0.6f));
                    // 子の 2 番目が導火線（ThrowTestSceneBuilder.CreateItem の作り方に合わせる）
                    var renderers = item.GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 1)
                        bomb.fuse = renderers[1];
                    break;
            }
        }

        private static Transform CreateDice(Transform leftController, Transform rightController, GameObject[] prefabs)
        {
            // 左手の少し上にさいころを持つ
            var holder = new GameObject("Dice Holder");
            holder.transform.SetParent(leftController, false);
            holder.transform.localPosition = new Vector3(0.0f, 0.05f, 0.03f);

            var dice = ThrowTestSceneBuilder.Part(holder.transform, PrimitiveType.Cube, Vector3.zero, Vector3.zero, Vector3.one * DiceSize,
                ThrowTestSceneBuilder.GetMaterial("Dice", new Color(0.95f, 0.93f, 0.88f)), ThrowTestSceneBuilder.ColliderMode.None);
            dice.name = "Dice";
            CreatePips(dice.transform, ThrowTestSceneBuilder.GetMaterial("Pip", new Color(0.1f, 0.1f, 0.1f)));
            var visual = dice.AddComponent<DiceVisual>();

            var status = CreateHandLabel(holder, "Dice Status", "Shake x3", new Vector3(0.0f, 0.12f, 0.0f), new Color(1.0f, 0.85f, 0.2f));

            var roller = holder.AddComponent<DiceRoller>();
            roller.itemPrefabs = prefabs;
            roller.itemNames = (string[])ItemNames.Clone();
            roller.diceVisual = visual;
            roller.statusText = status;
            roller.rightHand = rightController;

            return holder.transform;
        }

        private static void CreatePlayer(GameObject origin, Transform diceHolder, Transform camera)
        {
            origin.AddComponent<PlayerRig>();
            var health = origin.AddComponent<PlayerHealth>();

            // 体力はさいころの下に出す
            var hp = CreateHandLabel(diceHolder.gameObject, "Health", "HP", new Vector3(0.0f, -0.06f, 0.0f), new Color(1.0f, 0.4f, 0.4f));
            hp.gameObject.AddComponent<FaceCamera>();
            health.healthText = hp;

            // ダメージを受けたときに視界を赤くする板
            var flash = ThrowTestSceneBuilder.Part(camera, PrimitiveType.Quad, new Vector3(0.0f, 0.0f, 0.12f), Vector3.zero, new Vector3(0.6f, 0.4f, 1.0f),
                GetTransparentMaterial("DamageFlash", new Color(1.0f, 0.0f, 0.0f, 0.5f)), ThrowTestSceneBuilder.ColliderMode.None);
            flash.name = "Damage Flash";
            var flashRenderer = flash.GetComponent<Renderer>();
            flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
            flashRenderer.enabled = false;
            health.damageFlash = flashRenderer;
        }

        private static TextMeshPro CreateHandLabel(GameObject parent, string name, string text, Vector3 localPosition, Color color)
        {
            var label = ThrowTestSceneBuilder.CreateLabel(parent.transform, text, Vector3.zero, 0.2f);
            label.name = name;
            label.transform.localPosition = localPosition;
            label.rectTransform.sizeDelta = new Vector2(0.4f, 0.08f);
            label.color = color;
            label.outlineWidth = 0.2f;
            label.outlineColor = Color.black;
            return label;
        }

        /// <summary>
        /// ロープの練習場所：高さ 3m の台。手前の角に引っかけ用の柱があり、ロープを当てると台の上へ引っ張り上げられる
        /// </summary>
        private static void CreateRopeLedge()
        {
            var root = new GameObject("Rope Ledge").transform;
            var ledgeMat = ThrowTestSceneBuilder.GetMaterial("Ledge", new Color(0.4f, 0.38f, 0.35f));
            var hookMat = ThrowTestSceneBuilder.GetMaterial("Hook", new Color(1.0f, 0.8f, 0.1f));

            var ledge = ThrowTestSceneBuilder.Part(root, PrimitiveType.Cube, new Vector3(-6.0f, 1.5f, 6.0f), Vector3.zero, new Vector3(4.0f, 3.0f, 4.0f), ledgeMat, ThrowTestSceneBuilder.ColliderMode.Keep);
            ledge.name = "Ledge";

            var post = ThrowTestSceneBuilder.Part(root, PrimitiveType.Cylinder, new Vector3(-4.5f, 3.5f, 4.5f), Vector3.zero, new Vector3(0.12f, 0.5f, 0.12f), hookMat, ThrowTestSceneBuilder.ColliderMode.Keep);
            post.name = "Hook Post";

            var hook = ThrowTestSceneBuilder.Part(root, PrimitiveType.Sphere, new Vector3(-4.5f, 4.05f, 4.5f), Vector3.zero, Vector3.one * 0.25f, hookMat, ThrowTestSceneBuilder.ColliderMode.None);
            hook.name = "Rope Anchor";

            // 多少外れても引っかかるよう、大きめのトリガーにする（Part の拡大縮小で半径 0.8m になる）
            var trigger = hook.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.8f / 0.25f;

            var landing = new GameObject("Landing Point").transform;
            landing.SetParent(root);
            landing.position = new Vector3(-6.0f, 3.0f, 6.0f);

            hook.AddComponent<RopeAnchor>().landingPoint = landing;
            hook.AddComponent<ThrowTarget>().kind = ThrowTargetKind.RopeAnchor;

            ThrowTestSceneBuilder.CreateLabel(root, "ROPE", new Vector3(-6.0f, 2.2f, 3.98f), 3.0f).transform.rotation = Quaternion.identity;
        }

        /// <summary>爆弾の練習場所：壊せる壁</summary>
        private static void CreateBreakableWall()
        {
            var root = new GameObject("Breakable Wall");
            root.transform.position = new Vector3(6.0f, 1.25f, 6.0f);
            root.AddComponent<BreakableWall>();
            root.AddComponent<ThrowTarget>().kind = ThrowTargetKind.Wall;

            var wall = ThrowTestSceneBuilder.Part(root.transform, PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(3.0f, 2.5f, 0.3f),
                ThrowTestSceneBuilder.GetMaterial("BreakWall", new Color(0.6f, 0.35f, 0.2f)), ThrowTestSceneBuilder.ColliderMode.Keep);
            wall.name = "Wall";

            ThrowTestSceneBuilder.CreateLabel(null, "BOMB", new Vector3(6.0f, 2.9f, 6.0f), 3.0f);
        }

        /// <summary>試作用の敵。カプセルの体に目が付いている。ranged なら離れて弾を投げてくる</summary>
        private static void CreateEnemy(Vector3 position, bool ranged = false)
        {
            var root = new GameObject("Enemy");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0.0f, 180.0f, 0.0f));

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0.0f, 0.8f, 0.0f);
            capsule.height = 1.6f;
            capsule.radius = 0.35f;

            var enemyMat = ThrowTestSceneBuilder.GetMaterial("Enemy", new Color(0.5f, 0.2f, 0.6f));
            var eyeMat = ThrowTestSceneBuilder.GetMaterial("EnemyEye", Color.white);
            ThrowTestSceneBuilder.Part(root.transform, PrimitiveType.Capsule, new Vector3(0.0f, 0.8f, 0.0f), Vector3.zero, new Vector3(0.7f, 0.8f, 0.7f), enemyMat, ThrowTestSceneBuilder.ColliderMode.None).name = "Body";
            ThrowTestSceneBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(-0.12f, 1.25f, 0.3f), Vector3.zero, Vector3.one * 0.1f, eyeMat, ThrowTestSceneBuilder.ColliderMode.None).name = "Eye L";
            ThrowTestSceneBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(0.12f, 1.25f, 0.3f), Vector3.zero, Vector3.one * 0.1f, eyeMat, ThrowTestSceneBuilder.ColliderMode.None).name = "Eye R";

            root.AddComponent<Damageable>().maxHealth = 6;

            var target = root.AddComponent<ThrowTarget>();
            target.kind = ThrowTargetKind.Enemy;
            target.localOffset = new Vector3(0.0f, 1.0f, 0.0f);

            var hp = ThrowTestSceneBuilder.CreateLabel(root.transform, "######", Vector3.zero, 1.5f);
            hp.name = "Health";
            hp.transform.localPosition = new Vector3(0.0f, 1.9f, 0.0f);
            hp.rectTransform.sizeDelta = new Vector2(1.5f, 0.3f);
            hp.color = new Color(1.0f, 0.3f, 0.3f);
            hp.gameObject.AddComponent<FaceCamera>();

            var enemy = root.AddComponent<SimpleEnemy>();
            enemy.healthText = hp;
            enemy.rangedAttack = ranged;
            enemy.projectileMaterial = ThrowTestSceneBuilder.GetMaterial("EnemyShot", new Color(1.0f, 0.35f, 0.25f));
        }

        /// <summary>
        /// 各面に目（黒い点）を付ける。立方体の大きさを 1 としたローカル座標で置く
        /// </summary>
        private static void CreatePips(Transform dice, Material pipMat)
        {
            const float spacing = 0.27f;
            const float pipSize = 0.18f;

            for (int face = 1; face <= 6; face++)
            {
                Vector3 normal = DiceVisual.FaceNormal(face);
                Vector3 u = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(normal, u);

                foreach (Vector2 p in PipPattern(face))
                {
                    Vector3 position = normal * 0.47f + (u * p.x + v * p.y) * spacing;
                    var pip = ThrowTestSceneBuilder.Part(dice, PrimitiveType.Sphere, position, Vector3.zero, Vector3.one * pipSize, pipMat, ThrowTestSceneBuilder.ColliderMode.None);
                    pip.name = $"Pip {face}";
                }
            }
        }

        private static Vector2[] PipPattern(int face)
        {
            switch (face)
            {
                case 1: return new[] { Vector2.zero };
                case 2: return new[] { new Vector2(-1, -1), new Vector2(1, 1) };
                case 3: return new[] { new Vector2(-1, -1), Vector2.zero, new Vector2(1, 1) };
                case 4: return new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) };
                case 5: return new[] { new Vector2(-1, -1), new Vector2(-1, 1), Vector2.zero, new Vector2(1, -1), new Vector2(1, 1) };
                default: return new[] { new Vector2(-1, -1), new Vector2(-1, 0), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 0), new Vector2(1, 1) };
            }
        }

        /// <summary>URP の Unlit で半透明のマテリアルを作る（爆発・ダメージ表示用）</summary>
        private static Material GetTransparentMaterial(string name, Color color)
        {
            string path = $"{ThrowTestSceneBuilder.MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetFloat("_Surface", 1.0f);
            material.SetFloat("_Blend", 0.0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0.0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", color);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateManager()
        {
            var board = ThrowTestSceneBuilder.CreateLabel(null, "Grab and throw!", new Vector3(0.0f, 2.3f, 5.0f), 3.0f);
            board.name = "Result Board";
            board.rectTransform.sizeDelta = new Vector2(6.0f, 2.0f);

            var manager = new GameObject("Throw Test Manager").AddComponent<ThrowTestManager>();
            manager.resultText = board;
            manager.hint = "Shake left hand 3 times to roll (T key)";
        }
    }
}
