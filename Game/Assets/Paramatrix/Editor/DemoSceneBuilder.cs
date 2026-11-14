using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
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

        /// <summary>
        /// The hero's walk cycle: experiment E-9 of the visual standard (SpriteCook, 8 frames, 1:1 pixel art), cut into
        /// frames with tools/art/split-frames.ps1. Standing: the clean profile (E-7 attempt 4) placed on the same canvas,
        /// head and feet aligned with the walk frames.
        /// </summary>
        private const string HeroWalkPrefix = Root + "/Art/Paranoik_Walk_";

        private const int HeroWalkFrames = 7;

        private const string HeroStandPath = Root + "/Art/Paranoik_Stand.png";

        // Characters standing: made from the user's clean cut-outs by tools/art/prepare-characters.ps1. Heights live in
        // tools/art/characters.json (a character's own height, or the common default); a test checks the sprites against it.
        private const string TrinityStandPath = Root + "/Art/Trinity_Stand.png";
        private const string MorpheusStandPath = Root + "/Art/Morpheus_Stand.png";
        private const string MrSritStandPath = Root + "/Art/MrSrit_Stand.png";

        /// <summary>The female version of the hero (her height is fixed in tools/art/characters.json).</summary>
        private const string FemParanoikStandPath = Root + "/Art/FemParanoik_Stand.png";

        /// <summary>The street backdrop: the user's generated picture 6144×2160 reduced 1:4 with tools/art/reduce-backdrop.ps1.</summary>
        private const string HallBackdropPath = Root + "/Art/Hall_Backdrop.png";

        // Generated scene backgrounds (2026-11-10), reduced to the grid with tools/art/reduce-backdrop.ps1:
        // rooms 1672x941 -> 960x540, streets 2115x743 -> 1536x540. Doors and other exits are drawn in them, so
        // every exit is an invisible click area over the drawn door (sizes measured on the reduced pictures).
        private const string OfficeBackdropPath = Root + "/Art/Backdrop_Office.png";
        private const string BarBackdropPath = Root + "/Art/Backdrop_Bar.png";
        private const string NiiStreetBackdropPath = Root + "/Art/Backdrop_NiiStreet.png";
        private const string DkStreetBackdropPath = Root + "/Art/Backdrop_DkStreet.png";
        private const string FactoryStreetBackdropPath = Root + "/Art/Backdrop_FactoryStreet.png";
        private const string ShopStreetBackdropPath = Root + "/Art/Backdrop_ShopStreet.png";
        private const string WorkshopBackdropPath = Root + "/Art/Backdrop_Workshop.png";
        private const string ScenesFolder = Root + "/Scenes";

        public const string BootstrapScene = ScenesFolder + "/Bootstrap.unity";
        public const string OfficeScene = ScenesFolder + "/Location_Office.unity";
        public const string CorridorScene = ScenesFolder + "/Location_Corridor.unity";
        public const string HallScene = ScenesFolder + "/Location_Hall.unity";
        public const string NiiStreetScene = ScenesFolder + "/Location_NiiStreet.unity";
        public const string DkStreetScene = ScenesFolder + "/Location_DkStreet.unity";
        public const string BarScene = ScenesFolder + "/Location_Bar.unity";
        public const string FactoryStreetScene = ScenesFolder + "/Location_FactoryStreet.unity";
        public const string ShopStreetScene = ScenesFolder + "/Location_ShopStreet.unity";
        public const string WorkshopScene = ScenesFolder + "/Location_Workshop.unity";

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
            var avatars = FindAvatars();
            manifest.SetAvatars(avatars);
            var novel = FindNovelPictures();
            manifest.SetNovelPictures(novel);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Paramatrix] Content manifest: {paths.Count} file(s), {avatars.Count} avatar(s), {novel.Count} novel picture(s).");
        }

        /// <summary>Folder of avatars (Р10): &lt;name&gt;/emotions/&lt;emotion&gt;.png belongs to character.&lt;name&gt;.</summary>
        public const string AvatarsFolder = Root + "/Art/Characters";

        /// <summary>
        /// Every PNG under <see cref="AvatarsFolder"/> that is not a novel picture, sorted: &lt;name&gt;/emotions/&lt;emotion&gt;.png;
        /// whether it is declared is checked by the validator.
        /// </summary>
        public static List<ContentManifest.Avatar> FindAvatars() => FindPictures("emotions", wanted: false);

        /// <summary>Every PNG under <see cref="AvatarsFolder"/>/&lt;name&gt;/novel (Р12), sorted.</summary>
        public static List<ContentManifest.Avatar> FindNovelPictures() => FindPictures("novel", wanted: true);

        /// <summary>
        /// Pictures of the folder kind <paramref name="kind"/>; with <paramref name="wanted"/> false, also every PNG that is not
        /// in a novel folder, so a file put elsewhere keeps an empty ID and the validator names it.
        /// </summary>
        private static List<ContentManifest.Avatar> FindPictures(string kind, bool wanted)
        {
            var pictures = new List<ContentManifest.Avatar>();
            if (!AssetDatabase.IsValidFolder(AvatarsFolder))
            {
                return pictures;
            }

            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { AvatarsFolder }).Select(AssetDatabase.GUIDToAssetPath)
                         .Where(p => p.EndsWith(".png")).OrderBy(p => p, System.StringComparer.Ordinal))
            {
                var parts = path.Substring(AvatarsFolder.Length + 1).Split('/');
                var isNovel = parts.Length == 3 && parts[1] == "novel";
                if (wanted ? !isNovel : isNovel)
                {
                    continue;
                }

                var ok = parts.Length == 3 && parts[1] == kind;
                pictures.Add(new ContentManifest.Avatar
                {
                    characterId = ok ? "character." + parts[0] : string.Empty,
                    emotion = ok ? Path.GetFileNameWithoutExtension(parts[2]) : path,
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path),
                });
            }

            return pictures;
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

        /// <summary>
        /// Rebuilds the Bootstrap scene (systems, camera, hero, UI: always generated) and the content manifest.
        /// Location scenes are assembled by hand in the editor (scene kit document), so an existing location scene is
        /// never touched; a demo location is generated only when its scene is missing. Build Settings: Bootstrap and
        /// every Location_*.unity in the scenes folder.
        /// </summary>
        [MenuItem("Paramatrix/Build Scenes")]
        public static void BuildAll() => Build(overwriteLocations: false);

        /// <summary>Re-generates the demo locations from code, discarding whatever was assembled in them by hand.</summary>
        [MenuItem("Paramatrix/Rebuild Demo Locations (overwrite)")]
        public static void RebuildDemoLocations()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Rebuild demo locations",
                    "All demo locations will be generated from code again. Everything assembled in them by hand is lost.",
                    "Overwrite", "Cancel"))
            {
                return;
            }

            Build(overwriteLocations: true);
        }

        private static void Build(bool overwriteLocations)
        {
            if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                throw new System.InvalidOperationException("TextMesh Pro essentials are missing: run Paramatrix > Import TMP Essentials first.");
            }

            Directory.CreateDirectory(ScenesFolder);
            CollectContent();
            var sprite = EnsureSprite();

            BuildBootstrap(sprite);
            var demo = new (string Path, System.Action<Sprite> Build)[]
            {
                (OfficeScene, BuildOffice),
                (CorridorScene, BuildCorridor),
                (HallScene, BuildHall),
                (NiiStreetScene, BuildNiiStreet),
                (DkStreetScene, BuildDkStreet),
                (BarScene, BuildBar),
                (FactoryStreetScene, BuildFactoryStreet),
                (ShopStreetScene, BuildShopStreet),
                (WorkshopScene, BuildWorkshop),
            };
            foreach (var (path, build) in demo)
            {
                if (overwriteLocations || !File.Exists(path))
                {
                    build(sprite);
                    Debug.Log($"[Paramatrix] Generated {path}.");
                }
                else
                {
                    Debug.Log($"[Paramatrix] Kept {path} as it is (assembled in the editor).");
                }
            }

            var locations = Directory.GetFiles(ScenesFolder, "Location_*.unity")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p, System.StringComparer.Ordinal);
            EditorBuildSettings.scenes = new[] { BootstrapScene }.Concat(locations)
                .Select(p => new EditorBuildSettingsScene(p, true))
                .ToArray();

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(BootstrapScene, OpenSceneMode.Single);
            Debug.Log("[Paramatrix] Scenes built.");
        }

        // ---------------- scenes ----------------

        private static void BuildBootstrap(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = BuildCamera();
            var worldCamera = camera.gameObject.AddComponent<WorldCamera>();

            var walk = Enumerable.Range(0, HeroWalkFrames).Select(i => LoadGridSprite(HeroWalkPrefix + i + ".png")).ToArray();
            var stand = LoadGridSprite(HeroStandPath);
            var hero = Figure("Hero (character.paranoik)", stand, -288, 10);
            hero.AddComponent<Walker>();
            hero.AddComponent<WalkAnimation>().SetFrames(stand, walk, 18f);
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
            Set(bootstrap, "worldCamera", worldCamera);
            Set(bootstrap, "dialogueView", dialogue);
            Set(bootstrap, "inventoryView", inventory);
            Set(bootstrap, "worldPause", pause);
            Set(bootstrap, "saveControls", BuildSaveControls(canvas, systems));
            Set(bootstrap, "transitionCurtain", BuildCurtain(canvas, systems));
            Set(bootstrap, "overlay", overlay);

            EditorSceneManager.SaveScene(scene, BootstrapScene);
        }

        private static void BuildOffice(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The hero's room ("Комната героя v2"): its only door, on the left, is the way out (locked, the key) to
            // Morpheus's room; the hexagon poster on the wall.
            BackdropLocation("location.office", OneScreen, 92, OfficeBackdropPath);
            Entry("default", -288, true, Facing.Right);
            Entry("from_corridor", -380, false, Facing.Right); // back to the door
            Entry("from_street", -380, false, Facing.Right);

            Exit("object.office_door", sprite, -411, 11, 75, 244, -380);
            Exit("object.office_poster", sprite, -73, 179, 62, 88, -73);

            var key = Block("Key (item.key)", sprite, new Color(0.95f, 0.8f, 0.2f), -120, 0, 18, 9, 5);
            View<ItemView>(key, "item.key", -120);

            // Drawn looking right, mirrored to look left, where the hero enters.
            Character("character.trinity", "Trinity", TrinityStandPath, 168, Facing.Left);

            EditorSceneManager.SaveScene(scene, OfficeScene);
        }

        private static void BuildCorridor(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Morpheus's room, still placeholder: the door back to the office on the left, the door to the street on the right.
            Location("location.corridor", -432, 432, OneScreen, FloorDepth, new Color(0.18f, 0.2f, 0.24f), sprite);
            Entry("default", -192, true, Facing.Right);
            Entry("from_office", -336, false, Facing.Right);
            Entry("from_hall", 336, false, Facing.Left);

            var door = Block("Door (object.corridor_door)", sprite, new Color(0.45f, 0.3f, 0.18f), -408, 0, StandardDoorWidth, StandardDoorHeight, 1);
            View<ObjectView>(door, "object.corridor_door", -336);

            var hallDoor = Block("Door (object.corridor_hall_door)", sprite, new Color(0.3f, 0.32f, 0.4f), 408, 0, StandardDoorWidth, StandardDoorHeight, 1);
            View<ObjectView>(hallDoor, "object.corridor_hall_door", 336);

            // Drawn three-quarters to the right, mirrored to look left at the hero coming from the office.
            Character("character.morpheus", "Morpheus", MorpheusStandPath, 96, Facing.Left);

            EditorSceneManager.SaveScene(scene, CorridorScene);
        }

        /// <summary>
        /// A scrolling street (В-19): the first real backdrop. The camera follows the hero across 1.6 screens.
        /// The backdrop is drawn art with its own pavement, so the floor line is where the pavement is, and the doors are
        /// part of the picture: the way back is an invisible click area over the drawn door. The foreground layer is
        /// still placeholder posts, for testing parallax.
        /// </summary>
        private static void BuildHall(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Backdrop 1536×540; the middle of the pavement is 100 grid pixels above its bottom edge.
            const int halfView = 768;
            const int floorDepth = 100;
            Location("location.hall", -720, 720, halfView, floorDepth, null, sprite);
            Backdrop(LoadGridSprite(HallBackdropPath), floorDepth, -50);
            Entry("default", -447, true, Facing.Right);
            Entry("from_corridor", -447, false, Facing.Right); // out of the left entrance

            // The left entrance door of the picture: x -476..-418, from 37 to 156 above the floor line.
            var door = Block("Door (object.hall_door)", sprite, Color.clear, -447, 37, 58, 119, 1);
            View<ObjectView>(door, "object.hall_door", -447);
            Entry("from_east", 696, false, Facing.Left);
            EdgeExit("object.hall_east_edge", sprite, 768, true);

            // Drawn three-quarters to the right, mirrored to look left at the hero walking in.
            Character("character.fem_paranoik", "Fem Paranoik", FemParanoikStandPath, -150, Facing.Left);

            // By the right entrance, looking left at the hero coming from the door.
            Character("character.mr_srit", "Mr Srit", MrSritStandPath, 520, Facing.Left);

            // The camera moves ±288 grid pixels from the middle; a layer following by f must cover ±(480 + 288 × |1 - f|).
            // Foreground: dark posts passing in front of the hero, faster than the world.
            var near = Layer("Layer foreground (-0.5)", -0.5f);
            for (var x = -960; x <= 960; x += 480)
            {
                Child(near, Block("Post", sprite, new Color(0.08f, 0.08f, 0.1f), x, -floorDepth, 30, PixelGrid.Height, 20));
            }

            EditorSceneManager.SaveScene(scene, HallScene);
        }

        /// <summary>A whole-location picture at its own size (one texture pixel = one grid pixel), centred, its bottom edge at the bottom of the screen.</summary>
        private static void Backdrop(Sprite sprite, int floorDepth, int order)
        {
            var gameObject = new GameObject("Backdrop");
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            gameObject.transform.position = new Vector3(0f, PixelGrid.Units(-floorDepth), 0f); // pivot: bottom middle (PixelArtImport)
        }

        private static void BuildNiiStreet(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BackdropLocation("location.nii_street", 768, 100, NiiStreetBackdropPath);
            Entry("default", -403, true, Facing.Right);
            Entry("from_office", -403, false, Facing.Right);
            Entry("from_west", -696, false, Facing.Right);
            Entry("from_east", 696, false, Facing.Left);
            Exit("object.nii_entrance", sprite, -403, 56, 92, 104, -403);
            EdgeExit("object.nii_west_edge", sprite, 768, false);
            EdgeExit("object.nii_east_edge", sprite, 768, true);
            EditorSceneManager.SaveScene(scene, NiiStreetScene);
        }

        private static void BuildDkStreet(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BackdropLocation("location.dk_street", 768, 100, DkStreetBackdropPath);
            Entry("default", 9, true, Facing.Right);
            Entry("from_bar", 9, false, Facing.Right);
            Entry("from_west", -696, false, Facing.Right);
            Entry("from_east", 696, false, Facing.Left);
            Exit("object.dk_entrance", sprite, 9, 48, 134, 138, 9);
            EdgeExit("object.dk_west_edge", sprite, 768, false);
            EdgeExit("object.dk_east_edge", sprite, 768, true);
            EditorSceneManager.SaveScene(scene, DkStreetScene);
        }

        private static void BuildBar(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BackdropLocation("location.bar", OneScreen, 90, BarBackdropPath);
            Entry("default", 341, true, Facing.Left);
            Entry("from_street", 341, false, Facing.Left);
            Exit("object.bar_curtain", sprite, 341, 21, 92, 205, 341);
            EditorSceneManager.SaveScene(scene, BarScene);
        }

        private static void BuildFactoryStreet(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BackdropLocation("location.factory_street", 768, 100, FactoryStreetBackdropPath);
            Entry("default", -149, true, Facing.Right);
            Entry("from_shop", -149, false, Facing.Right);
            Entry("from_west", -696, false, Facing.Right);
            Exit("object.factory_gates", sprite, -149, 25, 197, 154, -149);
            EdgeExit("object.factory_west_edge", sprite, 768, false);
            EditorSceneManager.SaveScene(scene, FactoryStreetScene);
        }

        private static void BuildShopStreet(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BackdropLocation("location.shop_street", 768, 100, ShopStreetBackdropPath);
            Entry("default", -42, true, Facing.Right);
            Entry("from_factory", -42, false, Facing.Right);
            Entry("from_workshop", -408, false, Facing.Right);
            Exit("object.shop_door", sprite, -408, 38, 67, 114, -408);
            Exit("object.shop_gates", sprite, -42, 34, 215, 179, -42);
            EditorSceneManager.SaveScene(scene, ShopStreetScene);
        }

        private static void BuildWorkshop(Sprite sprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BackdropLocation("location.workshop", 768, 100, WorkshopBackdropPath);
            Entry("default", 571, true, Facing.Left);
            Entry("from_street", 571, false, Facing.Left);
            Exit("object.workshop_door", sprite, 571, 41, 129, 155, 571);
            Exit("object.workshop_upper_door", sprite, -580, 248, 57, 106, -560); // on the gallery: the hero stays below
            EditorSceneManager.SaveScene(scene, WorkshopScene);
        }

        /// <summary>
        /// A location drawn by one picture (the backdrop) of ±<paramref name="halfView"/> grid pixels; the floor ends
        /// 48 grid pixels short of each edge (one screen: ±432).
        /// </summary>
        private static void BackdropLocation(string id, int halfView, int floorDepth, string backdropPath)
        {
            Location(id, -(halfView - 48), halfView - 48, halfView, floorDepth, null, null);
            Backdrop(LoadGridSprite(backdropPath), floorDepth, -50);
        }

        /// <summary>An exit or other clickable thing drawn in the backdrop: an invisible click area over it.</summary>
        private static void Exit(string id, Sprite sprite, int centerX, int bottom, int width, int height, int approachX)
        {
            var area = Block("Area (" + id + ")", sprite, Color.clear, centerX, bottom, width, height, 1);
            View<ObjectView>(area, id, approachX);
        }

        /// <summary>The way on along the street: a click area at the left or right edge of the view; the hero walks to the floor's end.</summary>
        private static void EdgeExit(string id, Sprite sprite, int halfView, bool right)
        {
            var sign = right ? 1 : -1;
            Exit(id, sprite, sign * (halfView - 24), 0, 48, 300, sign * (halfView - 48));
        }

        private static GameObject Layer(string name, float follow)
        {
            var layer = new GameObject(name);
            Set(layer.AddComponent<ParallaxLayer>(), "follow", follow);
            return layer;
        }

        private static void Child(GameObject parent, GameObject child) => child.transform.SetParent(parent.transform, true);

        /// <summary>
        /// A standing character: its figure at its own size (drawn looking right, like all figure art: a source drawn
        /// looking left is mirrored when the sprite is prepared), where it looks at the start, the view and the approach point
        /// (<see cref="ApproachOffset"/> to its left: where a scene walks someone to it).
        /// </summary>
        private static void Character(string id, string name, string spritePath, int centerX, Facing start)
        {
            var figure = Figure(name + " (" + id + ")", LoadGridSprite(spritePath), centerX, 8);
            figure.GetComponent<SpriteRenderer>().flipX = start == Facing.Left; // as the walker will show it: art looks right
            var walker = figure.AddComponent<Walker>();
            Set(walker, "startFacing", (int)start);
            View<CharacterView>(figure, id, centerX - ApproachOffset);
            Animations(figure);
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

        /// <summary>Conversation and monologue bubbles (Р10): the view builds its avatar and bubbles itself when bound.</summary>
        private static DialogueView BuildDialogueUi(RectTransform canvas, Camera camera)
        {
            var root = new GameObject("Dialogue", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            Stretch(root);
            var view = root.gameObject.AddComponent<DialogueView>();
            Set(view, "worldCamera", camera);
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
            hover.anchoredPosition = new Vector2(-24f, -170f); // below the selection hint
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

            var hint = Text("Selection hint", root, 26f, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.TopRight);
            Place(hint.rectTransform, new Vector2(0.4f, 1f), new Vector2(1f, 1f), new Vector2(0f, -150f), new Vector2(-24f, -110f));
            Set(view, "selectionHint", hint);
            hint.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Full-screen black cover on top of the game UI, shown only during location transitions.</summary>
        private static TransitionCurtain BuildCurtain(RectTransform canvas, GameObject systems)
        {
            var cover = Panel("Transition curtain", canvas, Color.black);
            Stretch(cover);
            cover.SetAsLastSibling();
            var curtain = systems.AddComponent<TransitionCurtain>();
            Set(curtain, "curtain", cover.gameObject);
            cover.gameObject.SetActive(false);
            return curtain;
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

        /// <summary>
        /// The world camera on the pixel grid (В-22): the world is rendered into a 960×540 texture; on screen it is
        /// enlarged by the largest whole factor, then smoothly by the remainder (RetroAA), keeping 16:9 with bars.
        /// The floor line is world y = 0, with <see cref="FloorDepth"/> grid pixels of floor below it.
        /// </summary>
        private static Camera BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = PixelGrid.OrthographicSize; // the pixel perfect camera sets the same while rendering
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; // also the colour of the bars
            cameraObject.transform.position = new Vector3(0f, PixelGrid.Units(PixelGrid.Height / 2f - FloorDepth), -10f);
            cameraObject.tag = "MainCamera";

            var pixelPerfect = cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = PixelGrid.PixelsPerUnit;
            pixelPerfect.refResolutionX = PixelGrid.Width;
            pixelPerfect.refResolutionY = PixelGrid.Height;
            pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.StretchFill;
            Set(pixelPerfect, "m_FilterMode", (int)PixelPerfectCamera.PixelPerfectFilterMode.RetroAA);
            return camera;
        }

        /// <summary>A door with its frame, grid pixels (visual standard §6.7, В-30).</summary>
        private const int StandardDoorWidth = 104;
        private const int StandardDoorHeight = 208;

        /// <summary>Floor below the floor line in the placeholder locations, grid pixels.</summary>
        private const int FloorDepth = 72;

        /// <summary>
        /// Conversation line of every location, grid pixels above the bottom of the screen (Р10): one height everywhere,
        /// so conversations stand at the same place on every screen; a location changes it only by hand, when its picture
        /// needs it. Over the tallest standing figure (171) on the highest floor (100).
        /// </summary>
        public const int DialogueLine = 282;

        /// <summary>Where a scene walks someone to a character: this far to its left, grid pixels.</summary>
        private const int ApproachOffset = 48;

        /// <summary>Half width of a fixed-screen location's view, grid pixels.</summary>
        private const int OneScreen = PixelGrid.Width / 2;

        /// <summary>
        /// A location whose walkable floor spans <paramref name="minX"/>..<paramref name="maxX"/> grid pixels and whose
        /// drawn view spans ±<paramref name="halfView"/> (one screen: fixed; wider: the camera scrolls); the floor line is
        /// <paramref name="floorDepth"/> grid pixels above the bottom of the screen. With a floor colour, a placeholder floor is drawn.
        /// </summary>
        private static void Location(string id, int minX, int maxX, int halfView, int floorDepth, Color? floorColor, Sprite sprite)
        {
            var root = new GameObject("Location (" + id + ")");
            var location = root.AddComponent<LocationRoot>();
            Set(location, "locationId", id);
            Set(location, "floorMinX", PixelGrid.Units(minX));
            Set(location, "floorMaxX", PixelGrid.Units(maxX));
            Set(location, "viewMinX", PixelGrid.Units(-halfView));
            Set(location, "viewMaxX", PixelGrid.Units(halfView));
            Set(location, "floorDepth", floorDepth);
            Set(location, "dialogueLine", DialogueLine);

            if (floorColor.HasValue)
            {
                var floor = Block("Floor", sprite, floorColor.Value, 0, -floorDepth, 2 * halfView, floorDepth, -10);
                floor.transform.SetParent(root.transform);
            }
        }

        /// <summary>An entry point; <paramref name="facing"/> is where the hero looks on arrival: away from the door he used.</summary>
        private static void Entry(string name, int x, bool isDefault, Facing facing)
        {
            var point = new GameObject("Entry " + name);
            point.transform.position = new Vector3(PixelGrid.Units(x), 0f, 0f);
            var entry = point.AddComponent<EntryPoint>();
            Set(entry, "entryName", name);
            Set(entry, "isDefault", isDefault);
            Set(entry, "facing", (int)facing);
        }

        /// <summary>A placeholder rectangle, in grid pixels: centred on <paramref name="centerX"/>, standing on <paramref name="bottom"/>.</summary>
        private static GameObject Block(string name, Sprite sprite, Color color, int centerX, int bottom, int width, int height, int order)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.position = new Vector3(PixelGrid.Units(centerX), PixelGrid.Units(bottom), 0f); // pivot: bottom middle
            // The placeholder sprite is one grid pixel per texture pixel, so its scale is its size in its own pixels.
            var spritePixels = sprite.rect.width;
            gameObject.transform.localScale = new Vector3(width / spritePixels, height / spritePixels, 1f);
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return gameObject;
        }

        private static T View<T>(GameObject gameObject, string id, int approachX)
            where T : EntityView
        {
            var view = gameObject.AddComponent<T>();
            Set(view, "entityId", id);
            var approach = new GameObject("Approach");
            approach.transform.SetParent(gameObject.transform, false);
            approach.transform.position = new Vector3(PixelGrid.Units(approachX), 0f, 0f);
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

            return LoadGridSprite(SpritePath);
        }

        /// <summary>A sprite from the Art folder; import settings come from PixelArtImport and must have come out right.</summary>
        private static Sprite LoadGridSprite(string path)
        {
            if (!File.Exists(path))
            {
                throw new System.InvalidOperationException($"{path} is missing.");
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var problem = PixelArtImport.Problem(AssetImporter.GetAtPath(path) as TextureImporter);
            if (sprite == null || problem != null)
            {
                throw new System.InvalidOperationException($"{path} is not imported as a pixel-grid sprite: {problem ?? "no sprite"}.");
            }

            return sprite;
        }

        /// <summary>
        /// A drawn figure at its own size (art is never scaled: one texture pixel is one grid pixel, В-06),
        /// centred on <paramref name="centerX"/> grid pixels and standing on the floor line.
        /// </summary>
        private static GameObject Figure(string name, Sprite sprite, int centerX, int order)
        {
            var gameObject = new GameObject(name);
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            // The pivot is under the feet (PixelArtImport): the figure stands on the floor line whatever its size.
            gameObject.transform.position = new Vector3(PixelGrid.Units(centerX), 0f, 0f);
            return gameObject;
        }

        private static void Set(Object target, string field, object value) => SerializedFields.Set(target, field, value);
    }
}
