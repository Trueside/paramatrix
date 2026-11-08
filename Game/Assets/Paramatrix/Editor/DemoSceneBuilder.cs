using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Paramatrix.Unity.Editor
{
    /// <summary>
    /// Builds the content manifest, the placeholder sprite and the Bootstrap / location scenes from code,
    /// so scenes are reproducible and reviewable. Safe to re-run: it overwrites what it generates.
    /// Batch: Unity -executeMethod Paramatrix.Unity.Editor.DemoSceneBuilder.BuildAll
    /// </summary>
    public static class DemoSceneBuilder
    {
        private const string Root = "Assets/Paramatrix";
        private const string ContentFolder = Root + "/Content";
        private const string ManifestPath = Root + "/ContentManifest.asset";
        private const string SpritePath = Root + "/Art/Placeholder.png";
        private const string ScenesFolder = Root + "/Scenes";

        public const string BootstrapScene = ScenesFolder + "/Bootstrap.unity";
        public const string OfficeScene = ScenesFolder + "/Location_Office.unity";
        public const string CorridorScene = ScenesFolder + "/Location_Corridor.unity";

        [MenuItem("Paramatrix/Collect Content")]
        public static void CollectContent()
        {
            var paths = AssetDatabase.FindAssets("t:TextAsset", new[] { ContentFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".json"))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();

            var manifest = AssetDatabase.LoadAssetAtPath<ContentManifest>(ManifestPath);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<ContentManifest>();
                AssetDatabase.CreateAsset(manifest, ManifestPath);
            }

            manifest.SetEntries(paths.Select(p => new ContentManifest.Entry { path = p, asset = AssetDatabase.LoadAssetAtPath<TextAsset>(p) }));
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Paramatrix] Content manifest: {paths.Count} file(s).");
        }

        /// <summary>TextMesh Pro shaders and settings; run once before BuildAll in a fresh checkout.</summary>
        [MenuItem("Paramatrix/Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                TMP_PackageResourceImporter.ImportResources(true, false, false);
                AssetDatabase.Refresh();
            }
        }

        [MenuItem("Paramatrix/Build Demo Scenes")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                throw new System.InvalidOperationException("TextMesh Pro essentials are missing: run Paramatrix > Import TMP Essentials first.");
            }

            Directory.CreateDirectory(ScenesFolder);
            CollectContent();
            var sprite = EnsureSprite();

            BuildBootstrap(sprite);
            BuildOffice(sprite);
            BuildCorridor(sprite);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScene, true),
                new EditorBuildSettingsScene(OfficeScene, true),
                new EditorBuildSettingsScene(CorridorScene, true),
            };

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(BootstrapScene, OpenSceneMode.Single);
            Debug.Log("[Paramatrix] Demo scenes built.");
        }

        // ---------------- scenes ----------------

        private static void BuildBootstrap(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            cameraObject.transform.position = new Vector3(0f, 3.5f, -10f);
            cameraObject.tag = "MainCamera";

            var hero = Block("Hero (character.paranoik)", sprite, new Color(0.85f, 0.85f, 0.9f), new Vector2(-5f, 0.9f), new Vector2(0.8f, 1.8f), 10);
            hero.AddComponent<Walker>();
            var heroView = hero.AddComponent<CharacterView>();
            Set(heroView, "entityId", "character.paranoik");
            Set(heroView, "isPlayer", true);
            Animations(hero, ("bend_down", 0.4f), ("open_door", 0.4f));
            var heroController = hero.AddComponent<HeroController>();

            var systems = new GameObject("Systems");
            var bootstrap = systems.AddComponent<GameBootstrap>();
            var input = systems.AddComponent<InputRouter>();
            var overlay = systems.AddComponent<DiagnosticsOverlay>();
            var pause = systems.AddComponent<WorldPause>();
            Set(input, "worldCamera", camera);

            var eventSystem = new GameObject("EventSystem");
            var events = eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
            Set(input, "eventSystem", events);

            var canvas = BuildCanvas();
            var dialogue = BuildDialogueUi(canvas, camera);
            var inventory = BuildInventoryUi(canvas);
            Set(bootstrap, "manifest", AssetDatabase.LoadAssetAtPath<ContentManifest>(ManifestPath));
            Set(bootstrap, "hero", heroController);
            Set(bootstrap, "input", input);
            Set(bootstrap, "dialogueView", dialogue);
            Set(bootstrap, "inventoryView", inventory);
            Set(bootstrap, "worldPause", pause);
            Set(bootstrap, "saveControls", BuildSaveControls(canvas, systems));
            Set(bootstrap, "overlay", overlay);

            EditorSceneManager.SaveScene(scene, BootstrapScene);
        }

        private static void BuildOffice(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Location("location.office", -8f, 8f, new Color(0.25f, 0.22f, 0.2f), sprite);
            Entry("default", -5f, true);
            Entry("from_corridor", 6f, false);

            var door = Block("Door (object.office_door)", sprite, new Color(0.45f, 0.3f, 0.18f), new Vector2(7.2f, 1.2f), new Vector2(1.2f, 2.4f), 1);
            View<ObjectView>(door, "object.office_door", 6.2f);

            var window = Block("Window (object.office_window)", sprite, new Color(0.45f, 0.6f, 0.75f), new Vector2(0f, 3.2f), new Vector2(2.4f, 1.6f), 1);
            View<ObjectView>(window, "object.office_window", 0f);

            var key = Block("Key (item.key)", sprite, new Color(0.95f, 0.8f, 0.2f), new Vector2(-2.5f, 0.15f), new Vector2(0.4f, 0.25f), 5);
            View<ItemView>(key, "item.key", -2.5f);

            var trinity = Block("Trinity (character.trinity)", sprite, new Color(0.15f, 0.15f, 0.15f), new Vector2(3f, 0.9f), new Vector2(0.8f, 1.8f), 8);
            trinity.AddComponent<Walker>();
            View<CharacterView>(trinity, "character.trinity", 2f);
            Animations(trinity);

            EditorSceneManager.SaveScene(scene, OfficeScene);
        }

        private static void BuildCorridor(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Location("location.corridor", -8f, 12f, new Color(0.18f, 0.2f, 0.24f), sprite);
            Entry("default", 0f, true);
            Entry("from_office", -6f, false);

            var door = Block("Door (object.corridor_door)", sprite, new Color(0.45f, 0.3f, 0.18f), new Vector2(-7.2f, 1.2f), new Vector2(1.2f, 2.4f), 1);
            View<ObjectView>(door, "object.corridor_door", -6.2f);

            var morpheus = Block("Morpheus (character.morpheus)", sprite, new Color(0.35f, 0.1f, 0.12f), new Vector2(6f, 0.95f), new Vector2(0.85f, 1.9f), 8);
            morpheus.AddComponent<Walker>();
            View<CharacterView>(morpheus, "character.morpheus", 5f);
            Animations(morpheus);

            EditorSceneManager.SaveScene(scene, CorridorScene);
        }

        // ---------------- UI ----------------

        private static RectTransform BuildCanvas()
        {
            var canvasObject = new GameObject("UI", typeof(RectTransform));
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            return (RectTransform)canvasObject.transform;
        }

        private static DialogueView BuildDialogueUi(RectTransform canvas, Camera camera)
        {
            var root = new GameObject("Dialogue", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            Stretch(root);
            var view = root.gameObject.AddComponent<DialogueView>();

            // Speech bubble: anchored at its bottom centre, positioned above the speaker every frame.
            var bubble = Panel("Bubble", root, new Color(1f, 1f, 1f, 0.95f));
            bubble.pivot = new Vector2(0.5f, 0f);
            bubble.anchorMin = bubble.anchorMax = Vector2.zero;
            bubble.sizeDelta = new Vector2(440f, 0f);
            bubble.GetComponent<Image>().raycastTarget = false;
            var bubbleLayout = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(18, 18, 12, 12);
            bubbleLayout.childControlHeight = true;
            bubbleLayout.childControlWidth = true;
            bubble.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bubbleText = Text("Text", bubble, 28f, Color.black, TextAlignmentOptions.Center);

            // Visual novel panel across the bottom of the screen.
            var novel = Panel("Novel", root, new Color(0.05f, 0.06f, 0.08f, 0.9f));
            novel.anchorMin = new Vector2(0f, 0f);
            novel.anchorMax = new Vector2(1f, 0f);
            novel.pivot = new Vector2(0.5f, 0f);
            novel.sizeDelta = new Vector2(-80f, 240f);
            novel.anchoredPosition = new Vector2(0f, 40f);
            novel.GetComponent<Image>().raycastTarget = false;
            var speaker = Text("Name", novel, 32f, new Color(1f, 0.85f, 0.45f), TextAlignmentOptions.TopLeft);
            Place(speaker.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -64f), new Vector2(-32f, -16f));
            var text = Text("Text", novel, 30f, Color.white, TextAlignmentOptions.TopLeft);
            Place(text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(32f, 24f), new Vector2(-32f, -76f));

            Set(view, "worldCamera", camera);
            Set(view, "bubble", bubble);
            Set(view, "bubbleText", bubbleText);
            Set(view, "novelPanel", novel);
            Set(view, "novelName", speaker);
            Set(view, "novelText", text);
            bubble.gameObject.SetActive(false);
            novel.gameObject.SetActive(false);
            return view;
        }

        private static InventoryView BuildInventoryUi(RectTransform canvas)
        {
            var root = new GameObject("Inventory", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            Stretch(root);
            var view = root.gameObject.AddComponent<InventoryView>();

            var bar = Panel("Bar", root, new Color(0f, 0f, 0f, 0.45f));
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1f, 1f);
            bar.anchoredPosition = new Vector2(-24f, -24f);
            var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 10f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            var fitter = bar.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var hover = Panel("Hover", root, new Color(0.05f, 0.06f, 0.08f, 0.92f));
            hover.anchorMin = hover.anchorMax = hover.pivot = new Vector2(1f, 1f);
            hover.anchoredPosition = new Vector2(-24f, -132f);
            hover.sizeDelta = new Vector2(420f, 130f);
            hover.GetComponent<Image>().raycastTarget = false;
            var hoverName = Text("Name", hover, 28f, new Color(1f, 0.85f, 0.45f), TextAlignmentOptions.TopLeft);
            Place(hoverName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -50f), new Vector2(-16f, -10f));
            var hoverText = Text("Description", hover, 24f, Color.white, TextAlignmentOptions.TopLeft);
            Place(hoverText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 10f), new Vector2(-16f, -54f));

            Set(view, "slots", bar);
            Set(view, "hoverPanel", hover);
            Set(view, "hoverName", hoverName);
            Set(view, "hoverDescription", hoverText);
            hover.gameObject.SetActive(false);
            return view;
        }

        private static SaveControls BuildSaveControls(RectTransform canvas, GameObject systems)
        {
            var controls = systems.AddComponent<SaveControls>();
            var status = Text("Status", canvas, 30f, Color.white, TextAlignmentOptions.Top);
            Place(status.rectTransform, new Vector2(0.25f, 1f), new Vector2(0.75f, 1f), new Vector2(0f, -80f), new Vector2(0f, -24f));
            Set(controls, "status", status);
            status.gameObject.SetActive(false);
            return controls;
        }

        private static RectTransform Panel(string name, RectTransform parent, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panel.gameObject.AddComponent<Image>().color = color;
            return panel;
        }

        private static TextMeshProUGUI Text(string name, RectTransform parent, float size, Color color, TextAlignmentOptions alignment)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect) =>
            Place(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        // ---------------- helpers ----------------

        private static void Location(string id, float minX, float maxX, Color floorColor, Sprite sprite)
        {
            var root = new GameObject("Location (" + id + ")");
            var location = root.AddComponent<LocationRoot>();
            Set(location, "locationId", id);
            Set(location, "floorMinX", minX);
            Set(location, "floorMaxX", maxX);

            var floor = new GameObject("Floor");
            floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3((minX + maxX) / 2f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(maxX - minX + 2f, 0.2f, 1f);
            var renderer = floor.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = floorColor;
            renderer.sortingOrder = -10;
        }

        private static void Entry(string name, float x, bool isDefault)
        {
            var point = new GameObject("Entry " + name);
            point.transform.position = new Vector3(x, 0f, 0f);
            var entry = point.AddComponent<EntryPoint>();
            Set(entry, "entryName", name);
            Set(entry, "isDefault", isDefault);
        }

        private static GameObject Block(string name, Sprite sprite, Color color, Vector2 position, Vector2 size, int order)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.position = new Vector3(position.x, position.y, 0f);
            gameObject.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            gameObject.AddComponent<BoxCollider2D>();
            return gameObject;
        }

        private static T View<T>(GameObject gameObject, string id, float approachX)
            where T : EntityView
        {
            var view = gameObject.AddComponent<T>();
            Set(view, "entityId", id);
            var approach = new GameObject("Approach");
            approach.transform.SetParent(gameObject.transform, false);
            approach.transform.position = new Vector3(approachX, 0f, 0f);
            Set(view, "approachPoint", approach.transform);
            return view;
        }

        private static void Animations(GameObject gameObject, params (string Name, float Duration)[] clips)
        {
            var animations = gameObject.AddComponent<EntityAnimations>();
            animations.SetClips(clips.Select(c => new EntityAnimations.Clip { name = c.Name, duration = c.Duration }));
        }

        private static Sprite EnsureSprite()
        {
            if (!File.Exists(SpritePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SpritePath));
                var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                texture.SetPixels(Enumerable.Repeat(Color.white, 16 * 16).ToArray());
                File.WriteAllBytes(SpritePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(SpritePath);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            if (importer.textureType != TextureImporterType.Sprite || !Mathf.Approximately(importer.spritePixelsPerUnit, 16f))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 16f;
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }

        private static void Set(Object target, string field, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new System.InvalidOperationException($"{target.GetType().Name} has no serialized field '{field}'.");
            }

            switch (value)
            {
                case string text: property.stringValue = text; break;
                case bool flag: property.boolValue = flag; break;
                case float number: property.floatValue = number; break;
                case Object reference: property.objectReferenceValue = reference; break;
                default: throw new System.ArgumentException($"Unsupported value for '{field}'.");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
