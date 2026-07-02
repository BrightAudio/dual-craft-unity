using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DualCraft.Story
{
    using AI;
    using Audio;
    using Cards;
    using Core;
    using Data;
    using UI.Visual;

    public class StorySceneController : MonoBehaviour
    {
        private const int MapWidth = 24;
        private const int MapHeight = 18;
        private const float TileSize = 1f;
        private const float MoveDuration = 0.14f;
        private const float WalkBobAmplitude = 0.075f;  // vertical arc per tile step
        private const float NpcWanderInterval = 2.6f;   // seconds between NPC move decisions
        private const float WildEncounterChance = 0.18f; // per grass tile step
        private const int MainCharacterFrameWidth = 16;
        private const int MainCharacterFrameHeight = 32;
        private const int MainCharacterColumns = 4;
        private const int MainCharacterRows = 4;
        private const float MainCharacterPixelsPerUnit = 24f;

        private static readonly Color GrassA = new(0.22f, 0.50f, 0.34f);
        private static readonly Color GrassB = new(0.17f, 0.40f, 0.29f);
        private static readonly Color PathA = new(0.63f, 0.57f, 0.45f);
        private static readonly Color PathB = new(0.53f, 0.47f, 0.37f);
        private static readonly Color WaterA = new(0.19f, 0.48f, 0.72f);
        private static readonly Color WaterB = new(0.12f, 0.31f, 0.54f);
        private static readonly Color RoofA = new(0.54f, 0.28f, 0.24f);
        private static readonly Color RoofB = new(0.35f, 0.14f, 0.12f);
        private static readonly Color WallA = new(0.86f, 0.82f, 0.71f);
        private static readonly Color WallB = new(0.70f, 0.64f, 0.52f);
        private static readonly Color Gold = new(0.90f, 0.76f, 0.42f);
        private static readonly Color Ink = new(0.08f, 0.08f, 0.12f);
        private static readonly Color Sky = new(0.47f, 0.81f, 0.95f);

        private sealed class DialogueBeat
        {
            public string Speaker;
            public string Body;
        }

        private sealed class StoryActor
        {
            public string Id;
            public string DisplayName;
            public Vector2Int Tile;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Color BaseTint = Color.white;
            public string SpriteAssetName;
            public string[] Dialogue;
            public bool IsHeadmistress;
            // NPC wander (Pokémon-style: shuffle around home tile every few seconds)
            public bool CanWander;
            public Vector2Int HomeTile;
            public Vector2Int Facing = Vector2Int.down;
            // Invoker trainer battle
            public bool IsInvoker;
            public string InvokerId;
        }

        private sealed class StarterChoice
        {
            public string CardId;
            public string DeckName;
            public string HouseName;
            public string Motto;
            public string Summary;
            public Color Accent;
            public DaemonCardData Card;
            public DeckData Deck;
        }

        private sealed class WaterShimmerProp
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector3 BasePosition;
            public float VerticalAmplitude;
            public float VerticalSpeed;
            public float HorizontalAmplitude;
            public float HorizontalSpeed;
            public float Phase;
            public float AlphaMin;
            public float AlphaMax;
            public float AlphaSpeed;
            public float BaseRotation;
            public float RotationAmplitude;
            public Color BaseColor;
        }

        private enum StoryArea
        {
            Courtyard,
            AcademyHall,
        }

        private enum SpecialPrompt
        {
            None,
            EnterHall,
            LeaveHall,
            BeginTrial,
        }

        private enum StoryQuestStep
        {
            MeetSerastra,
            ChooseStarter,
            EnterHall,
            WinTrial,
            ReturnToCourtyard,
            DefeatThray,
            ReportToSerastra,
            FreeRoam,
        }

        private readonly Dictionary<string, Sprite> _spriteCache = new();
        private readonly Dictionary<string, Texture2D> _streamingTextureCache = new();
        private readonly Dictionary<string, Sprite[]> _storyCharacterSheetCache = new();
        private readonly Dictionary<Vector2Int, StoryActor> _actorsByTile = new();
        private readonly HashSet<Vector2Int> _blockedTiles = new();
        private readonly List<(Transform transform, Vector3 basePosition, float amplitude, float speed, float phase)> _hoveringProps = new();
        private readonly List<WaterShimmerProp> _waterShimmers = new();
        private readonly List<DialogueBeat> _dialogueQueue = new();

        private PlayerProfile _profile;
        private CardDatabase _database;
        private Camera _camera;
        private Transform _worldRoot;
        private Transform _playerTransform;
        private SpriteRenderer _playerRenderer;
        private Sprite[] _mainCharacterFrames;
        private Vector2Int _playerTile = new(12, 3);
        private bool _isMoving;
        private Vector3 _moveStart;
        private Vector3 _moveEnd;
        private float _moveProgress;
        private StoryActor _headmistress;
        private StoryActor _proctor;
        private StoryActor _nearestActor;
        private StoryArea _currentArea = StoryArea.Courtyard;
        private SpecialPrompt _activeSpecialPrompt;

        private Canvas _canvas;
        private TextMeshProUGUI _objectiveText;
        private TextMeshProUGUI _hintText;
        private TextMeshProUGUI _promptText;
        private TextMeshProUGUI _locationText;
        private TextMeshProUGUI _chapterText;
        private GameObject _dialoguePanel;
        private TextMeshProUGUI _speakerText;
        private TextMeshProUGUI _dialogueText;
        private TextMeshProUGUI _dialogueAdvanceText;
        private GameObject _starterPanel;
        private RectTransform _starterOptionContainer;
        private GameObject _starterStatusPanel;
        private Image _starterStatusArt;
        private TextMeshProUGUI _starterStatusTitle;
        private TextMeshProUGUI _starterStatusBody;
        private Button _trialButton;
        private Button _menuButton;
        private Button _hudMenuButton;
        private Button _storyContinueButton;
        private GameObject _storyMenuPanel;
        private TextMeshProUGUI _storyMenuTitle;
        private TextMeshProUGUI _storyMenuBody;
        private GameObject _storyStartChoicePanel;
        private TextMeshProUGUI _storyStartChoiceBody;
        private GameObject _sceneBannerPanel;
        private TextMeshProUGUI _sceneBannerTitle;
        private TextMeshProUGUI _sceneBannerSubtitle;
        private Coroutine _sceneBannerRoutine;

        private StarterChoice[] _starterChoices;
        private StarterChoice _selectedChoice;
        private Action _dialogueCompleteAction;
        private int _dialogueIndex = -1;
        private Coroutine _revealCoroutine;
        private string _fullDialogueText = string.Empty;
        private bool _isRevealing;
        private bool _introSequenceStarted;
        private Vector2Int _playerFacing = Vector2Int.down; // last movement direction for flipX
        private float _npcWanderTimer;
        // Wild encounter state
        private DaemonCardData _encounterDaemon;
        private GameObject _encounterPanel;
        private TextMeshProUGUI _encounterTitle;
        private TextMeshProUGUI _encounterBody;
        private Image _encounterDaemonArt;
        private Image _encounterDaemonFx;
        private Coroutine _encounterFxRoutine;

        private void Awake()
        {
            EnsureCamera();
            EnsureEventSystem();
        }

        private void Start()
        {
            Application.targetFrameRate = 60;

            _profile = ProfileManager.Load();
            _database = RuntimeAssetLocator.LoadCardDatabase(null, this);
            _database?.Initialize();

            MusicManager.EnsureInstance().PlayMode(MusicManager.MusicMode.Collection);

            ResolveStarterChoices();
            ApplySavedStoryLocation();
            BuildWorld();
            TickCamera(); // snap camera to player position before first frame
            BuildHud();
            SyncStoryStateFromProfile();
            ShowStoryStartChoice();
        }

        private void Update()
        {
            TickCamera();
            AnimateWorldProps();
            UpdateActorHighlight();
            UpdateNearestActorPrompt();
            TickNpcWander();

            if (_encounterPanel != null && _encounterPanel.activeSelf)
                return;

            if (_storyStartChoicePanel != null && _storyStartChoicePanel.activeSelf)
                return;

            if (_storyMenuPanel != null && _storyMenuPanel.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.M))
                    CloseStoryMenu();
                return;
            }

            if (_dialoguePanel.activeSelf)
            {
                if (PressedConfirm())
                    AdvanceDialogue();
                return;
            }

            if (_starterPanel.activeSelf)
                return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.M))
            {
                ToggleStoryMenu();
                return;
            }

            if (_isMoving)
            {
                TickMovement();
                return;
            }

            if (PressedConfirm())
            {
                TryInteract();
                return;
            }

            Vector2Int direction = ReadMoveIntent();
            if (direction != Vector2Int.zero)
                TryStartMove(direction);
        }

        private void EnsureCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var cameraGo = new GameObject("Main Camera");
                cameraGo.tag = "MainCamera";
                _camera = cameraGo.AddComponent<Camera>();
                cameraGo.AddComponent<AudioListener>();
            }

            _camera.orthographic = true;
            _camera.orthographicSize = 4.2f;
            _camera.transform.position = new Vector3(0f, -3.0f, -10f); // snapped on first TickCamera
            _camera.backgroundColor = Sky;
            _camera.clearFlags = CameraClearFlags.SolidColor;
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private void ResolveStarterChoices()
        {
            var decks = Resources.LoadAll<DeckData>("CardData/Decks");

            _starterChoices = new[]
            {
                new StarterChoice
                {
                    CardId = "d-flame-6",
                    DeckName = "Apprentice's Flame Kit",
                    HouseName = "House Ember",
                    Motto = "Fast feet. Fierce heart.",
                    Summary = "Aggressive summons with quick pressure and explosive finishers.",
                    Accent = new Color(0.89f, 0.42f, 0.27f),
                },
                new StarterChoice
                {
                    CardId = "d-water-2",
                    DeckName = "Apprentice's Water Kit",
                    HouseName = "House Tide",
                    Motto = "Adapt. Endure. Return.",
                    Summary = "Measured tempo with resilient bodies and smooth mid-game control.",
                    Accent = new Color(0.29f, 0.58f, 0.88f),
                },
                new StarterChoice
                {
                    CardId = "d-earth-6",
                    DeckName = "Apprentice's Earth Kit",
                    HouseName = "House Root",
                    Motto = "Stand firm. Strike true.",
                    Summary = "Durable daemons and stable pressure for players who like solid boards.",
                    Accent = new Color(0.53f, 0.68f, 0.29f),
                },
                new StarterChoice
                {
                    CardId = "d-dark-4",
                    DeckName = "Shadow Grimoire",
                    HouseName = "House Veil",
                    Motto = "In shadow, all strength is hidden.",
                    Summary = "Patient and deceptive — drain your opponent's resources and strike when their resolve breaks.",
                    Accent = new Color(0.52f, 0.18f, 0.72f),
                },
            };

            foreach (StarterChoice choice in _starterChoices)
            {
                choice.Card = _database?.GetCard(choice.CardId) as DaemonCardData;
                choice.Deck = decks.FirstOrDefault(deck => deck != null && deck.deckName == choice.DeckName);
            }

            if (!string.IsNullOrWhiteSpace(_profile.storyStarterCardId))
                _selectedChoice = _starterChoices.FirstOrDefault(choice => choice.CardId == _profile.storyStarterCardId);
        }

        private void BuildWorld()
        {
            if (_worldRoot != null)
                Destroy(_worldRoot.gameObject);

            _actorsByTile.Clear();
            _blockedTiles.Clear();
            _hoveringProps.Clear();
            _waterShimmers.Clear();

            _worldRoot = new GameObject("StoryWorld").transform;
            _worldRoot.SetParent(transform, false);

            if (_currentArea == StoryArea.Courtyard)
            {
                BuildTerrain();
                BuildAcademyFacade();
                BuildPedestals();
                BuildActors();
                BuildAmbientRunes();
            }
            else
            {
                BuildHallTerrain();
                BuildHallArchitecture();
                BuildHallActors();
                BuildHallAmbientRunes();
            }

            BuildPlayer();
            UpdateLocationLabel();
        }

        private void BuildTerrain()
        {
            for (int y = 0; y < MapHeight; y++)
            {
                for (int x = 0; x < MapWidth; x++)
                {
                    bool onPath = IsPathTile(x, y);
                    bool onWater = IsWaterTile(x, y);
                    Sprite sprite = onWater
                        ? LoadAutotileCenterCell("Still water.png") ?? GetTileSprite("water", WaterA, WaterB, new Color(0.55f, 0.88f, 0.98f))
                        : onPath
                            ? LoadAutotileCenterCell("Brick path.png") ?? GetTileSprite("path", PathA, PathB, new Color(0.76f, 0.70f, 0.56f))
                            : LoadAutotileCenterCell("Light grass.png") ?? GetTileSprite("grass", GrassA, GrassB, new Color(0.29f, 0.63f, 0.41f));

                    CreateWorldSprite($"Ground_{x}_{y}", sprite, GridToWorld(new Vector2Int(x, y)), 0, _worldRoot, 1f, Color.white);

                    if (onWater)
                        _blockedTiles.Add(new Vector2Int(x, y));
                }
            }

            for (int x = 0; x < MapWidth; x++)
            {
                AddBorderShrub(new Vector2Int(x, MapHeight - 1));
                AddBorderShrub(new Vector2Int(x, 0));
            }

            for (int y = 1; y < MapHeight - 1; y++)
            {
                AddBorderShrub(new Vector2Int(0, y));
                AddBorderShrub(new Vector2Int(MapWidth - 1, y));
            }

            for (int x = 5; x <= 8; x++)
            {
                AddGardenBed(x, 7);
                AddGardenBed(x, 8);
                AddGardenBed(MapWidth - 1 - x, 7);
                AddGardenBed(MapWidth - 1 - x, 8);
            }

            for (int x = 9; x <= 14; x++)
            {
                var tile = new Vector2Int(x, 9);
                CreateWorldSprite($"Seal_{x}", GetSealSprite("seal-glyph"), GridToWorld(tile) + new Vector3(0f, 0f, -0.01f), 5, _worldRoot, 1f,
                    new Color(0.91f, 0.82f, 0.50f, x % 2 == 0 ? 0.22f : 0.14f));
            }

            SpawnWaterShimmerProps();
        }

        private void SpawnWaterShimmerProps()
        {
            for (int y = 0; y < MapHeight; y++)
            {
                for (int x = 0; x < MapWidth; x++)
                {
                    if (!IsWaterTile(x, y))
                        continue;

                    var random = new System.Random((x + 17) * 73856093 ^ (y + 31) * 19349663);
                    int sparkleCount = random.Next(1, 3);
                    for (int i = 0; i < sparkleCount; i++)
                        CreateWaterShimmer(new Vector2Int(x, y), random, false, i);

                    if (random.NextDouble() > 0.35d)
                        CreateWaterShimmer(new Vector2Int(x, y), random, true, sparkleCount);
                }
            }
        }

        private void CreateWaterShimmer(Vector2Int tile, System.Random random, bool line, int variantIndex)
        {
            Sprite sprite = line ? GetShimmerLineSprite() : GetSparkleSprite();
            float offsetX = Mathf.Lerp(-0.34f, 0.34f, (float)random.NextDouble());
            float offsetY = Mathf.Lerp(-0.20f, 0.26f, (float)random.NextDouble());
            float scale = line
                ? Mathf.Lerp(0.22f, 0.38f, (float)random.NextDouble())
                : Mathf.Lerp(0.15f, 0.45f, (float)random.NextDouble());
            float phase = Mathf.Lerp(0f, Mathf.PI * 2f, (float)random.NextDouble());
            float verticalSpeed = Mathf.Lerp(1.5f, 3.5f, (float)random.NextDouble());
            float alphaCycleDuration = Mathf.Lerp(1.5f, 3f, (float)random.NextDouble());
            float alphaSpeed = (Mathf.PI * 2f) / Mathf.Max(0.01f, alphaCycleDuration);
            float horizontalAmplitude = line
                ? Mathf.Lerp(0.06f, 0.14f, (float)random.NextDouble())
                : Mathf.Lerp(0.01f, 0.05f, (float)random.NextDouble());
            float rotationAmplitude = line
                ? Mathf.Lerp(5f, 15f, (float)random.NextDouble())
                : Mathf.Lerp(0f, 4f, (float)random.NextDouble());
            float baseRotation = line ? Mathf.Lerp(-8f, 8f, (float)random.NextDouble()) : 0f;
            Color baseColor = line
                ? new Color(0.62f, 0.88f, 1f, 0.55f)
                : new Color(0.74f, 0.96f, 1f, 0.78f);

            Transform shimmer = CreateWorldSprite(
                $"{(line ? "WaterLine" : "WaterSparkle")}_{tile.x}_{tile.y}_{variantIndex}",
                sprite,
                GridToWorld(tile) + new Vector3(offsetX, offsetY, 0f),
                12,
                _worldRoot,
                scale,
                baseColor);

            shimmer.localRotation = Quaternion.Euler(0f, 0f, baseRotation);
            var renderer = shimmer.GetComponent<SpriteRenderer>();
            _waterShimmers.Add(new WaterShimmerProp
            {
                Transform = shimmer,
                Renderer = renderer,
                BasePosition = shimmer.position,
                VerticalAmplitude = line ? 0.04f : Mathf.Lerp(0.05f, 0.12f, (float)random.NextDouble()),
                VerticalSpeed = verticalSpeed,
                HorizontalAmplitude = horizontalAmplitude,
                HorizontalSpeed = Mathf.Lerp(0.7f, 1.8f, (float)random.NextDouble()),
                Phase = phase,
                AlphaMin = line ? 0.14f : 0.22f,
                AlphaMax = line ? 0.48f : 0.86f,
                AlphaSpeed = alphaSpeed,
                BaseRotation = baseRotation,
                RotationAmplitude = rotationAmplitude,
                BaseColor = baseColor,
            });
        }

        private void BuildAcademyFacade()
        {
            for (int x = 6; x <= 17; x++)
            {
                for (int y = 14; y <= 17; y++)
                {
                    bool roof = y >= 16;
                    Vector2Int tile = new(x, y);
                    if (roof)
                    {
                        Sprite roofSprite = LoadEnvironmentCharacterCell("e4wall.png", Mathf.Abs(x - 6) % 8, y == 17 ? 0 : 1, 32f, new Vector2(0.5f, 0.08f))
                            ?? GetTileSprite("roof", RoofA, RoofB, new Color(0.72f, 0.42f, 0.37f));
                        CreateWorldSprite($"Roof_{x}_{y}",
                            roofSprite,
                            GridToWorld(tile),
                            GetSortOrder(tile, 30),
                            _worldRoot);
                    }
                    else
                    {
                        Sprite wallSprite = LoadEnvironmentCharacterCell("elevatorwall.png", Mathf.Abs(x - 6) % 4, y == 15 ? 0 : 1, 32f, new Vector2(0.5f, 0.08f))
                            ?? GetTileSprite("wall", WallA, WallB, new Color(0.93f, 0.89f, 0.79f));
                        CreateWorldSprite($"Wall_{x}_{y}",
                            wallSprite,
                            GridToWorld(tile),
                            GetSortOrder(tile, 25),
                            _worldRoot);
                    }

                    _blockedTiles.Add(tile);
                }
            }

            for (int x = 9; x <= 14; x++)
            {
                Vector2Int stairTile = new(x, 13);
                CreateWorldSprite($"Stair_{x}",
                    GetTileSprite("stair", new Color(0.72f, 0.68f, 0.62f), new Color(0.56f, 0.52f, 0.46f), new Color(0.82f, 0.79f, 0.73f)),
                    GridToWorld(stairTile),
                    GetSortOrder(stairTile, 12),
                    _worldRoot);
            }

            for (int x = 10; x <= 13; x++)
            {
                var doorTile = new Vector2Int(x, 14);
                Sprite doorSprite = LoadEnvironmentCharacterCell("doors1.png", x - 10, 0, 32f, new Vector2(0.5f, 0.08f))
                    ?? GetTileSprite("door", new Color(0.26f, 0.16f, 0.13f), new Color(0.17f, 0.10f, 0.08f), Gold);
                Transform door = CreateWorldSprite($"Door_{x}",
                    doorSprite,
                    GridToWorld(doorTile),
                    GetSortOrder(doorTile, 35),
                    _worldRoot);
                if (doorSprite != null)
                    FitWorldSpriteToBounds(door, 1f, 1.4f);
            }

            CreateWorldText("AcademyBanner", "AETHER ACADEMY", new Vector3(0f, GridToWorld(new Vector2Int(12, 15)).y + 0.5f, 0f),
                3.4f, new Color(0.21f, 0.12f, 0.10f), 180);

            AddColumn(8, 13);
            AddColumn(15, 13);
        }

        private void BuildPedestals()
        {
            Vector2Int[] tiles = { new(7, 11), new(10, 11), new(14, 11), new(17, 11) };
            for (int i = 0; i < tiles.Length && i < _starterChoices.Length; i++)
            {
                StarterChoice choice = _starterChoices[i];
                Vector2Int baseTile = tiles[i];

                Transform table = CreateWorldSprite($"StarterTable_{i}",
                    GetTileSprite($"starter-table-{i}", new Color(0.45f, 0.30f, 0.18f), new Color(0.30f, 0.18f, 0.10f), new Color(0.76f, 0.62f, 0.34f)),
                    GridToWorld(baseTile) + new Vector3(0f, 0.08f, 0f),
                    GetSortOrder(baseTile, 12),
                    _worldRoot);
                FitWorldSpriteToBounds(table, 1.95f, 0.85f);

                CreateWorldSprite($"GrimwareShadow_{i}",
                    GetDropShadowSprite(),
                    GridToWorld(baseTile) + new Vector3(0f, 0.58f, 0f),
                    GetSortOrder(baseTile, 13),
                    _worldRoot,
                    1.25f,
                    new Color(0f, 0f, 0f, 0.35f));

                Sprite grimwareSprite = ResolveStarterGrimwareSprite(choice) ?? choice.Card?.artwork;
                if (grimwareSprite != null)
                {
                    Transform book = CreateWorldSprite($"StarterGrimware_{i}",
                        grimwareSprite,
                        GridToWorld(baseTile) + new Vector3(0f, 0.98f, 0f),
                        GetSortOrder(baseTile, 31),
                        _worldRoot,
                        1f,
                        Color.white);
                    FitWorldSpriteToBounds(book, 0.78f, 1.08f);
                    _hoveringProps.Add((book, book.position, 0.10f, 1.55f, i * 0.9f));
                }

                CreateWorldText($"PedestalName_{i}",
                    choice.HouseName,
                    GridToWorld(baseTile) + new Vector3(0f, -0.78f, 0f),
                    1.2f,
                    choice.Accent,
                    GetSortOrder(baseTile, 50));

                _blockedTiles.Add(baseTile);
            }
        }

        private void BuildActors()
        {
            _actorsByTile.Clear();

            StoryQuestStep step = GetCurrentQuestStep();
            Vector2Int headmistressTile = _selectedChoice == null ? new Vector2Int(12, 13) : new Vector2Int(7, 12);
            string[] headmistressDialogue = BuildSerastraAmbientDialogue(step);

            _headmistress = CreateActor(
                "headmistress",
                "Grand Covener Serastra",
                headmistressTile,
                CreateCharacterSprite("headmistress", new Color(0.33f, 0.12f, 0.42f), new Color(0.78f, 0.65f, 0.92f), Gold),
                headmistressDialogue,
                true,
                "trainer_PSYCHIC_F.png");

            var nova = CreateActor(
                "student-left",
                "Nova",
                new Vector2Int(6, 10),
                CreateCharacterSprite("student-left", new Color(0.18f, 0.28f, 0.53f), new Color(0.58f, 0.84f, 0.97f), new Color(0.92f, 0.70f, 0.43f)),
                new[]
                {
                    "Four grimwares, four paths. Choose the one that already knows your name.",
                },
                false,
                "NPC 01.png");
            nova.CanWander = true;
            nova.HomeTile = new Vector2Int(6, 10);
            nova.Facing = Vector2Int.down;

            var rook = CreateActor(
                "student-right",
                "Rook",
                new Vector2Int(18, 9),
                CreateCharacterSprite("student-right", new Color(0.22f, 0.46f, 0.26f), new Color(0.66f, 0.89f, 0.60f), new Color(0.92f, 0.70f, 0.43f)),
                new[]
                {
                    "Choose the one that feels right. The trial is easier when the daemon already trusts your call.",
                },
                false,
                "NPC 06.png");
            rook.CanWander = true;
            rook.HomeTile = new Vector2Int(18, 9);
            rook.Facing = Vector2Int.down;

            var thray = CreateActor(
                "invoker-thray",
                "Invoker Thray",
                new Vector2Int(3, 4),
                CreateCharacterSprite("invoker-thray", new Color(0.28f, 0.14f, 0.10f), new Color(0.76f, 0.52f, 0.28f), new Color(0.94f, 0.82f, 0.56f)),
                BuildThrayAmbientDialogue(step),
                false,
                "trainer_PSYCHIC_M.png");
            thray.IsInvoker = true;
            thray.InvokerId = "invoker-thray";
            thray.CanWander = true;
            thray.HomeTile = new Vector2Int(3, 4);
            thray.Facing = Vector2Int.right;
        }

        private string[] BuildSerastraAmbientDialogue(StoryQuestStep step)
        {
            return step switch
            {
                StoryQuestStep.MeetSerastra or StoryQuestStep.ChooseStarter => new[]
                {
                    "Your name reached the coven before you did. That is not luck — that is lineage.",
                    "Four grimwares stand ready. Choose your bond and prove the coven's mark was earned, not given.",
                },
                StoryQuestStep.EnterHall => new[]
                {
                    "The hall doors are open to you now.",
                    "Carry your bound grimoire into Sanction Hall and listen for the novice sigil.",
                },
                StoryQuestStep.ReturnToCourtyard or StoryQuestStep.DefeatThray => new[]
                {
                    "Good. The hall has marked your first victory.",
                    "There is a lower grove invoker named Thray. Challenge him before the lesson cools in your hands.",
                },
                StoryQuestStep.ReportToSerastra => new[]
                {
                    "You carry the grove's answer on your sleeve. Tell me how Thray fell.",
                },
                _ => new[]
                {
                    "The academy will not run out of tests, but today you have earned a breath.",
                    "Bind wild daemons, tune your grimware, and return when the next sanction bell rings.",
                },
            };
        }

        private string[] BuildThrayAmbientDialogue(StoryQuestStep step)
        {
            return step switch
            {
                StoryQuestStep.MeetSerastra or StoryQuestStep.ChooseStarter => new[]
                {
                    "No grimoire bond yet? Then no duel yet. I don't spar empty hands.",
                },
                StoryQuestStep.EnterHall or StoryQuestStep.WinTrial => new[]
                {
                    "Beat the academy trial first. I only challenge invokers with a sanctioned mark.",
                },
                StoryQuestStep.ReportToSerastra or StoryQuestStep.FreeRoam => new[]
                {
                    "You've already bested me. My coven mark goes back to the forge.",
                },
                _ => new[]
                {
                    "Show me what your grimoire can summon.",
                },
            };
        }

        private void BuildPlayer()
        {
            Sprite playerSprite = GetMainCharacterFrame(Vector2Int.down, false)
                ?? CreateCharacterSprite("player-fallback", new Color(0.23f, 0.21f, 0.58f), new Color(0.72f, 0.76f, 0.98f), Gold);

            _playerTransform = CreateWorldSprite(
                "Player",
                playerSprite,
                GridToWorld(_playerTile),
                GetSortOrder(_playerTile, 40),
                _worldRoot,
                1f,
                Color.white);

            _playerRenderer = _playerTransform.GetComponent<SpriteRenderer>();
            UpdatePlayerSpriteForFacing(_playerFacing, false);
        }

        private void UpdatePlayerSpriteForFacing(Vector2Int facing, bool moving)
        {
            if (_playerRenderer == null)
                return;

            Sprite frame = GetMainCharacterFrame(facing, moving);
            if (frame != null)
                _playerRenderer.sprite = frame;

            _playerRenderer.flipX = false;
        }

        private Sprite GetMainCharacterFrame(Vector2Int facing, bool moving)
        {
            _mainCharacterFrames ??= LoadMainCharacterFrames();
            if (_mainCharacterFrames == null || _mainCharacterFrames.Length == 0)
                return null;

            int rowFromTop = ResolveMainCharacterRow(facing);
            int column = moving ? 1 + (Mathf.FloorToInt(Time.time * 10f) % (MainCharacterColumns - 1)) : 0;
            int index = rowFromTop * MainCharacterColumns + column;
            if (index < 0 || index >= _mainCharacterFrames.Length)
                return _mainCharacterFrames[0];

            return _mainCharacterFrames[index];
        }

        private static int ResolveMainCharacterRow(Vector2Int facing)
        {
            if (facing.y > 0)
                return 3;
            if (facing.x < 0)
                return 1;
            if (facing.x > 0)
                return 2;
            return 0;
        }

        private Sprite[] LoadMainCharacterFrames()
        {
            return LoadStoryCharacterFrames("main-character");
        }

        private Sprite GetStoryCharacterFrame(string assetName, Vector2Int facing, bool moving)
        {
            Sprite[] frames = LoadStoryCharacterFrames(assetName);
            if (frames == null || frames.Length == 0)
                return null;

            int rowFromTop = ResolveMainCharacterRow(facing);
            int column = moving ? 1 + (Mathf.FloorToInt(Time.time * 10f) % (MainCharacterColumns - 1)) : 0;
            int index = rowFromTop * MainCharacterColumns + column;
            if (index < 0 || index >= frames.Length)
                return frames[0];

            return frames[index];
        }

        private Sprite[] LoadStoryCharacterFrames(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                return null;

            if (_storyCharacterSheetCache.TryGetValue(assetName, out Sprite[] cached))
                return cached;

            string path = ResolveStoryCharacterSheetPath(assetName);
            if (!File.Exists(path))
                return null;

            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = assetName
            };

            if (!texture.LoadImage(bytes, false))
            {
                Destroy(texture);
                return null;
            }

            if (texture.width % MainCharacterColumns != 0
                || texture.height % MainCharacterRows != 0)
            {
                Destroy(texture);
                return null;
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            int frameWidth = texture.width / MainCharacterColumns;
            int frameHeight = texture.height / MainCharacterRows;
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                Destroy(texture);
                return null;
            }

            float pixelsPerUnit = Mathf.Max(1f, frameHeight * 0.75f);
            Vector2 pivot = new(0.5f, Mathf.Clamp01(2f / frameHeight));
            var frames = new Sprite[MainCharacterColumns * MainCharacterRows];
            for (int rowFromTop = 0; rowFromTop < MainCharacterRows; rowFromTop++)
            {
                int y = texture.height - ((rowFromTop + 1) * frameHeight);
                for (int column = 0; column < MainCharacterColumns; column++)
                {
                    int x = column * frameWidth;
                    int index = rowFromTop * MainCharacterColumns + column;
                    frames[index] = Sprite.Create(
                        texture,
                        new Rect(x, y, frameWidth, frameHeight),
                        pivot,
                        pixelsPerUnit);
                }
            }

            _storyCharacterSheetCache[assetName] = frames;
            return frames;
        }

        private string ResolveStoryCharacterSheetPath(string assetName)
        {
            string trimmed = assetName.Trim();
            bool hasExtension = Path.HasExtension(trimmed);
            string fileName = hasExtension ? trimmed : $"{trimmed}.png";
            string[] directories =
            {
                Path.Combine(Application.streamingAssetsPath, "Story"),
                Path.Combine(Application.streamingAssetsPath, "Story", "Environment", "Characters")
            };

            foreach (string directory in directories)
            {
                string exact = Path.Combine(directory, fileName);
                if (File.Exists(exact))
                    return exact;

                string withoutExtension = Path.GetFileNameWithoutExtension(trimmed);
                string lower = Path.Combine(directory, $"{withoutExtension}.png");
                if (File.Exists(lower))
                    return lower;

                string upper = Path.Combine(directory, $"{withoutExtension}.PNG");
                if (File.Exists(upper))
                    return upper;
            }

            return Path.Combine(Application.streamingAssetsPath, "Story", fileName);
        }

        private Texture2D LoadStreamingTexture(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            if (_streamingTextureCache.TryGetValue(relativePath, out Texture2D cached))
                return cached;

            string path = Path.Combine(Application.streamingAssetsPath, "Story", relativePath);
            if (!File.Exists(path))
                return null;

            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = Path.GetFileNameWithoutExtension(relativePath)
            };

            if (!texture.LoadImage(bytes, false))
            {
                Destroy(texture);
                return null;
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            _streamingTextureCache[relativePath] = texture;
            return texture;
        }

        private Sprite LoadStreamingSprite(string relativePath, Rect? rect = null, float ppu = 32f, Vector2? pivot = null)
        {
            Texture2D texture = LoadStreamingTexture(relativePath);
            if (texture == null)
                return null;

            Rect sourceRect = rect ?? new Rect(0f, 0f, texture.width, texture.height);
            sourceRect.x = Mathf.Clamp(sourceRect.x, 0f, texture.width - 1f);
            sourceRect.y = Mathf.Clamp(sourceRect.y, 0f, texture.height - 1f);
            sourceRect.width = Mathf.Clamp(sourceRect.width, 1f, texture.width - sourceRect.x);
            sourceRect.height = Mathf.Clamp(sourceRect.height, 1f, texture.height - sourceRect.y);
            return Sprite.Create(texture, sourceRect, pivot ?? new Vector2(0.5f, 0.5f), ppu);
        }

        private Sprite LoadEnvironmentCharacterCell(string fileName, int cellX, int cellY, float ppu = 32f, Vector2? pivot = null)
        {
            Texture2D texture = LoadStreamingTexture(Path.Combine("Environment", "Characters", fileName));
            if (texture == null)
                return null;

            const int cellSize = 32;
            int x = cellX * cellSize;
            int y = texture.height - ((cellY + 1) * cellSize);
            if (x < 0 || y < 0 || x + cellSize > texture.width || y + cellSize > texture.height)
                return null;

            return Sprite.Create(texture, new Rect(x, y, cellSize, cellSize), pivot ?? new Vector2(0.5f, 0.08f), ppu);
        }

        private Sprite LoadAutotileCenterCell(string fileName, float ppu = 32f)
        {
            Texture2D texture = LoadStreamingTexture(Path.Combine("Environment", "Autotiles", fileName));
            if (texture == null)
                return null;

            Rect rect = new(32f, 48f, 32f, 32f);
            if (rect.xMax > texture.width || rect.yMax > texture.height)
                rect = new Rect(0f, 0f, Mathf.Min(32f, texture.width), Mathf.Min(32f, texture.height));

            return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), ppu);
        }

        private Sprite LoadFlowerTile(string fileName, int index, float ppu = 32f)
        {
            Texture2D texture = LoadStreamingTexture(Path.Combine("Environment", "Autotiles", fileName));
            if (texture == null)
                return null;

            const int cellSize = 32;
            int maxIndex = Mathf.Max(0, (texture.width / cellSize) - 1);
            int x = Mathf.Clamp(index, 0, maxIndex) * cellSize;
            int width = Mathf.Min(cellSize, texture.width - x);
            int height = Mathf.Min(cellSize, texture.height);
            return Sprite.Create(texture, new Rect(x, 0f, width, height), new Vector2(0.5f, 0.1f), ppu);
        }

        private void BuildAmbientRunes()
        {
            for (int i = 0; i < 6; i++)
            {
                Vector2Int tile = new(4 + i * 3, 12 + (i % 2));
                Transform rune = CreateWorldSprite(
                    $"AmbientRune_{i}",
                    GetSealSprite($"ambient-rune-{i}"),
                    GridToWorld(tile) + new Vector3(0f, 0.2f, 0f),
                    GetSortOrder(tile, 8),
                    _worldRoot,
                    0.52f,
                    new Color(0.92f, 0.85f, 0.58f, 0.22f));
                _hoveringProps.Add((rune, rune.position, 0.09f, 0.8f + i * 0.13f, i * 0.7f));
            }
        }

        private void BuildHallTerrain()
        {
            for (int y = 0; y < MapHeight; y++)
            {
                for (int x = 0; x < MapWidth; x++)
                {
                    bool carpet = x >= 10 && x <= 13 && y >= 2 && y <= 13;
                    bool circle = Mathf.Abs(x - 12) <= 2 && Mathf.Abs(y - 9) <= 2;
                    Sprite sprite = carpet
                        ? GetTileSprite("hall-carpet", new Color(0.46f, 0.12f, 0.14f), new Color(0.34f, 0.08f, 0.10f), Gold)
                        : GetTileSprite("hall-floor", new Color(0.22f, 0.23f, 0.28f), new Color(0.16f, 0.17f, 0.22f), new Color(0.38f, 0.39f, 0.47f));

                    CreateWorldSprite($"HallFloor_{x}_{y}", sprite, GridToWorld(new Vector2Int(x, y)), 0, _worldRoot);

                    if (circle)
                    {
                        CreateWorldSprite(
                            $"HallSigil_{x}_{y}",
                            GetSealSprite($"hall-sigil-{x}-{y}"),
                            GridToWorld(new Vector2Int(x, y)) + new Vector3(0f, 0f, -0.01f),
                            6,
                            _worldRoot,
                            1f,
                            new Color(0.96f, 0.86f, 0.56f, 0.18f));
                    }
                }
            }

            for (int x = 0; x < MapWidth; x++)
            {
                AddHallWall(new Vector2Int(x, 0));
                AddHallWall(new Vector2Int(x, MapHeight - 1));
            }

            for (int y = 1; y < MapHeight - 1; y++)
            {
                AddHallWall(new Vector2Int(0, y));
                AddHallWall(new Vector2Int(MapWidth - 1, y));
            }
        }

        private void BuildHallArchitecture()
        {
            for (int y = 10; y <= 14; y++)
            {
                AddBookshelf(4, y);
                AddBookshelf(5, y);
                AddBookshelf(18, y);
                AddBookshelf(19, y);
            }

            for (int x = 7; x <= 17; x++)
            {
                if (x >= 10 && x <= 13)
                    continue;

                Vector2Int tile = new(x, 15);
                CreateWorldSprite(
                    $"HallDais_{x}",
                    GetTileSprite("hall-dais", new Color(0.58f, 0.54f, 0.48f), new Color(0.42f, 0.38f, 0.34f), Gold),
                    GridToWorld(tile),
                    GetSortOrder(tile, 18),
                    _worldRoot);
                _blockedTiles.Add(tile);
            }

            for (int i = 0; i < 3; i++)
            {
                int bannerX = 8 + i * 4;
                Vector2Int tile = new(bannerX, 16);
                CreateWorldSprite(
                    $"HallBanner_{i}",
                    GetTileSprite($"hall-banner-{i}", new Color(0.30f + i * 0.06f, 0.16f, 0.42f), new Color(0.20f + i * 0.04f, 0.10f, 0.28f), Gold),
                    GridToWorld(tile),
                    GetSortOrder(tile, 32),
                    _worldRoot,
                    1.2f);
            }

            CreateWorldText("HallName", "SANCTION HALL", new Vector3(0f, GridToWorld(new Vector2Int(12, 16)).y + 0.28f, 0f),
                2.8f, new Color(0.96f, 0.91f, 0.82f), 190);
            CreateWorldText("TrialName", "NOVICE TRIAL", GridToWorld(new Vector2Int(12, 9)) + new Vector3(0f, -1.5f, 0f),
                1.6f, new Color(0.96f, 0.88f, 0.66f), 140);
        }

        private void BuildHallActors()
        {
            _actorsByTile.Clear();
            StoryQuestStep step = GetCurrentQuestStep();

            _headmistress = CreateActor(
                "hall-headmistress",
                "Grand Covener Serastra",
                new Vector2Int(8, 11),
                CreateCharacterSprite("hall-headmistress", new Color(0.33f, 0.12f, 0.42f), new Color(0.78f, 0.65f, 0.92f), Gold),
                step == StoryQuestStep.WinTrial
                    ? new[]
                    {
                        "Stand on the sigil when your grimoire is steady.",
                        "The first duel is guided. We teach technique before we test instinct.",
                    }
                    : new[]
                    {
                        "The hall has marked your first victory. Take that confidence back outside.",
                        "Thray waits near the lower grove.",
                    },
                true,
                "trainer_PSYCHIC_F.png");

            _proctor = CreateActor(
                "hall-proctor",
                "Proctor Caldus",
                new Vector2Int(12, 13),
                CreateCharacterSprite("hall-proctor", new Color(0.43f, 0.18f, 0.12f), new Color(0.83f, 0.56f, 0.36f), Gold),
                step == StoryQuestStep.WinTrial
                    ? new[]
                    {
                        "Your first sanctioned duel is not about perfection. It is about rhythm.",
                        "Summon. Stabilize. Strike when your daemon is truly ready.",
                    }
                    : new[]
                    {
                        "You completed the first loop. Now take the rhythm into a real challenge.",
                    },
                false,
                "trainer_GENTLEMAN.png");

            CreateActor(
                "hall-scribe",
                "Archivist Nera",
                new Vector2Int(16, 11),
                CreateCharacterSprite("hall-scribe", new Color(0.16f, 0.24f, 0.46f), new Color(0.55f, 0.72f, 0.94f), new Color(0.90f, 0.78f, 0.48f)),
                new[]
                {
                    "Every novice duel is recorded. Mostly for the lessons. Sometimes for the embarrassment.",
                },
                false,
                "NPC 11.png");
        }

        private void BuildHallAmbientRunes()
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2Int tile = new(5 + i * 2, 6 + (i % 2));
                Transform rune = CreateWorldSprite(
                    $"HallRune_{i}",
                    GetSealSprite($"hall-rune-{i}"),
                    GridToWorld(tile) + new Vector3(0f, 0.12f, 0f),
                    GetSortOrder(tile, 8),
                    _worldRoot,
                    0.42f,
                    new Color(0.76f, 0.86f, 1f, 0.18f));
                _hoveringProps.Add((rune, rune.position, 0.06f, 0.65f + i * 0.08f, i * 0.4f));
            }
        }

        private void BuildHud()
        {
            _canvas = FindAnyObjectByType<Canvas>();
            if (_canvas == null)
            {
                GameObject canvasGo = new("StoryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                _canvas = canvasGo.GetComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.55f;
            }

            GameObject topHudBar = CreatePanel(
                "TopHudBar",
                _canvas.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -12f),
                new Vector2(0f, 66f),
                new Color(0.05f, 0.06f, 0.10f, 0.86f));

            _locationText = CreateScreenLabel(
                "LocationText",
                topHudBar.transform,
                "AETHER ACADEMY  •  Novice Courtyard",
                25,
                TextAlignmentOptions.Right,
                new Color(0.96f, 0.91f, 0.78f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-30f, 0f),
                new Vector2(740f, 40f));

            _hudMenuButton = CreateAnchoredButton(
                "HudMenuButton",
                topHudBar.transform,
                "Menu",
                new Vector2(150f, 42f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-790f, 0f),
                new Color(0.33f, 0.42f, 0.58f),
                ToggleStoryMenu);

            _chapterText = CreateScreenLabel(
                "ChapterText",
                topHudBar.transform,
                string.Empty,
                23,
                TextAlignmentOptions.Left,
                new Color(0.97f, 0.84f, 0.48f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(30f, 0f),
                new Vector2(620f, 40f));

            GameObject objectivePanel = CreatePanel(
                "ObjectivePanel",
                _canvas.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -96f),
                new Vector2(560f, 104f),
                new Color(0.07f, 0.09f, 0.13f, 0.86f));
            _objectiveText = CreateScreenLabel(
                "ObjectiveText",
                objectivePanel.transform,
                string.Empty,
                24,
                TextAlignmentOptions.TopLeft,
                Color.white,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(18f, -18f),
                new Vector2(-36f, 72f));

            GameObject hintPanel = CreatePanel(
                "HintPanel",
                _canvas.transform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(28f, 22f),
                new Vector2(760f, 54f),
                new Color(0.05f, 0.06f, 0.10f, 0.76f));
            _hintText = CreateScreenLabel(
                "HintText",
                hintPanel.transform,
                "Move with WASD or the arrow keys. Press E or Space to interact.",
                20,
                TextAlignmentOptions.MidlineLeft,
                new Color(0.95f, 0.89f, 0.75f),
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(18f, 0f),
                new Vector2(-36f, -10f));

            GameObject promptPanel = CreatePanel(
                "PromptPanel",
                _canvas.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 82f),
                new Vector2(700f, 52f),
                new Color(0.08f, 0.08f, 0.12f, 0.72f));
            _promptText = CreateScreenLabel(
                "PromptText",
                promptPanel.transform,
                string.Empty,
                22,
                TextAlignmentOptions.Center,
                Gold,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            BuildDialoguePanel();
            BuildStarterPanel();
            BuildStarterStatusPanel();
            BuildStoryMenuPanel();
            BuildStoryStartChoicePanel();
            BuildSceneBanner();
            BuildEncounterPanel();
        }

        private void BuildDialoguePanel()
        {
            _dialoguePanel = CreatePanel(
                "DialoguePanel",
                _canvas.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 26f),
                new Vector2(1180f, 280f),
                new Color(0.05f, 0.05f, 0.08f, 0.95f));
            _dialoguePanel.SetActive(false);

            _speakerText = CreateScreenLabel(
                "SpeakerText",
                _dialoguePanel.transform,
                string.Empty,
                24,
                TextAlignmentOptions.TopLeft,
                Gold,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(22f, -18f),
                new Vector2(-44f, 36f));

            _dialogueText = CreateScreenLabel(
                "DialogueText",
                _dialoguePanel.transform,
                string.Empty,
                30,
                TextAlignmentOptions.TopLeft,
                Color.white,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(22f, 18f),
                new Vector2(-44f, -72f));
            _dialogueText.textWrappingMode = TextWrappingModes.Normal;

            _dialogueAdvanceText = CreateScreenLabel(
                "AdvanceText",
                _dialoguePanel.transform,
                "E / Space to continue",
                20,
                TextAlignmentOptions.BottomRight,
                new Color(0.93f, 0.86f, 0.69f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-22f, 18f),
                new Vector2(340f, 28f));
        }

        private void BuildStarterPanel()
        {
            _starterPanel = CreatePanel(
                "StarterPanel",
                _canvas.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(1700f, 760f),
                new Color(0.04f, 0.06f, 0.10f, 0.965f));
            _starterPanel.SetActive(false);

            CreateScreenLabel(
                "StarterHeader",
                _starterPanel.transform,
                "Choose The Grimware That Answers Your Mark",
                34,
                TextAlignmentOptions.Center,
                new Color(0.97f, 0.92f, 0.77f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -26f),
                new Vector2(1200f, 46f));

            CreateScreenLabel(
                "StarterBody",
                _starterPanel.transform,
                "Each grimware contains a novice bound daemon and a small starting deck. Choose the volume that feels like your first true pact.",
                22,
                TextAlignmentOptions.Center,
                new Color(0.85f, 0.88f, 0.94f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -78f),
                new Vector2(1180f, 34f));

            GameObject container = new("StarterOptions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            container.transform.SetParent(_starterPanel.transform, false);
            _starterOptionContainer = container.GetComponent<RectTransform>();
            _starterOptionContainer.anchorMin = new Vector2(0.5f, 0.5f);
            _starterOptionContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _starterOptionContainer.anchoredPosition = new Vector2(0f, -14f);
            _starterOptionContainer.sizeDelta = new Vector2(1620f, 560f);
            var layout = container.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 22f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            foreach (StarterChoice choice in _starterChoices)
                CreateStarterOptionCard(choice);
        }

        private void BuildStarterStatusPanel()
        {
            _starterStatusPanel = CreatePanel(
                "StarterStatusPanel",
                _canvas.transform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-28f, 30f),
                new Vector2(520f, 300f),
                new Color(0.06f, 0.08f, 0.12f, 0.90f));
            _starterStatusPanel.SetActive(false);

            GameObject artGo = new("StarterStatusArt", typeof(RectTransform), typeof(Image));
            artGo.transform.SetParent(_starterStatusPanel.transform, false);
            var artRect = artGo.GetComponent<RectTransform>();
            artRect.anchorMin = new Vector2(0f, 0.5f);
            artRect.anchorMax = new Vector2(0f, 0.5f);
            artRect.anchoredPosition = new Vector2(88f, 0f);
            artRect.sizeDelta = new Vector2(132f, 176f);
            _starterStatusArt = artGo.GetComponent<Image>();
            _starterStatusArt.preserveAspect = true;

            _starterStatusTitle = CreateScreenLabel(
                "StarterStatusTitle",
                _starterStatusPanel.transform,
                string.Empty,
                26,
                TextAlignmentOptions.TopLeft,
                Gold,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(180f, -18f),
                new Vector2(-198f, 36f));

            _starterStatusBody = CreateScreenLabel(
                "StarterStatusBody",
                _starterStatusPanel.transform,
                string.Empty,
                20,
                TextAlignmentOptions.TopLeft,
                Color.white,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(180f, -62f),
                new Vector2(-198f, 96f));
            _starterStatusBody.textWrappingMode = TextWrappingModes.Normal;

            _trialButton = CreateButton(
                "TrialButton",
                _starterStatusPanel.transform,
                "Start Trial Duel",
                new Vector2(274f, 58f),
                new Vector2(184f, 92f),
                Gold,
                StartTrialDuel);

            _menuButton = CreateButton(
                "MenuButton",
                _starterStatusPanel.transform,
                "Quit To Menu",
                new Vector2(274f, 58f),
                new Vector2(184f, 22f),
                new Color(0.33f, 0.42f, 0.58f),
                () => RuntimeAssetLocator.TryLoadScene("MainMenu", this));
        }

        private void BuildStoryMenuPanel()
        {
            _storyMenuPanel = CreatePanel(
                "StoryMenuPanel",
                _canvas.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-28f, -86f),
                new Vector2(420f, 560f),
                new Color(0.05f, 0.06f, 0.10f, 0.96f));
            _storyMenuPanel.SetActive(false);

            _storyMenuTitle = CreateScreenLabel(
                "StoryMenuTitle",
                _storyMenuPanel.transform,
                "Story Menu",
                30,
                TextAlignmentOptions.TopLeft,
                Gold,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(20f, -18f),
                new Vector2(-40f, 40f));

            _storyMenuBody = CreateScreenLabel(
                "StoryMenuBody",
                _storyMenuPanel.transform,
                string.Empty,
                20,
                TextAlignmentOptions.TopLeft,
                Color.white,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(20f, -70f),
                new Vector2(-40f, -250f));
            _storyMenuBody.textWrappingMode = TextWrappingModes.Normal;

            CreateAnchoredButton(
                "StoryMenuHandButton",
                _storyMenuPanel.transform,
                "View Hand",
                new Vector2(176f, 48f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(20f, -300f),
                new Color(0.46f, 0.64f, 0.46f),
                ShowStoryMenuHandPage);

            CreateAnchoredButton(
                "StoryMenuCraftButton",
                _storyMenuPanel.transform,
                "Craft Deck",
                new Vector2(176f, 48f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -300f),
                new Color(0.37f, 0.55f, 0.70f),
                ShowStoryMenuCraftPage);

            CreateAnchoredButton(
                "StoryMenuGrimwareButton",
                _storyMenuPanel.transform,
                "Grimware",
                new Vector2(176f, 48f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(20f, -360f),
                new Color(0.53f, 0.41f, 0.68f),
                ShowStoryMenuGrimwarePage);

            CreateAnchoredButton(
                "StoryMenuCharacterButton",
                _storyMenuPanel.transform,
                "Character",
                new Vector2(176f, 48f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -360f),
                new Color(0.68f, 0.56f, 0.37f),
                ShowStoryMenuCharacterPage);

            CreateAnchoredButton(
                "StoryMenuSaveButton",
                _storyMenuPanel.transform,
                "Save Game",
                new Vector2(176f, 52f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(20f, 86f),
                new Color(0.88f, 0.76f, 0.42f),
                () => SaveStoryGame(true));

            CreateAnchoredButton(
                "StoryMenuLoadButton",
                _storyMenuPanel.transform,
                "Load Game",
                new Vector2(176f, 52f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-20f, 86f),
                new Color(0.33f, 0.56f, 0.79f),
                LoadStoryGame);

            CreateAnchoredButton(
                "StoryMenuQuitButton",
                _storyMenuPanel.transform,
                "Quit To Menu",
                new Vector2(176f, 52f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(20f, 24f),
                new Color(0.43f, 0.31f, 0.31f),
                () => RuntimeAssetLocator.TryLoadScene("MainMenu", this));

            CreateAnchoredButton(
                "StoryMenuCloseButton",
                _storyMenuPanel.transform,
                "Close",
                new Vector2(176f, 52f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-20f, 24f),
                new Color(0.33f, 0.42f, 0.58f),
                CloseStoryMenu);
        }

        private void BuildStoryStartChoicePanel()
        {
            _storyStartChoicePanel = CreatePanel(
                "StoryStartChoicePanel",
                _canvas.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(720f, 360f),
                new Color(0.04f, 0.05f, 0.09f, 0.97f));
            _storyStartChoicePanel.SetActive(false);

            CreateScreenLabel(
                "StoryStartChoiceTitle",
                _storyStartChoicePanel.transform,
                "Story Mode",
                34,
                TextAlignmentOptions.Center,
                Gold,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -24f),
                new Vector2(500f, 42f));

            _storyStartChoiceBody = CreateScreenLabel(
                "StoryStartChoiceBody",
                _storyStartChoicePanel.transform,
                string.Empty,
                21,
                TextAlignmentOptions.Center,
                Color.white,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -88f),
                new Vector2(580f, 72f));
            _storyStartChoiceBody.textWrappingMode = TextWrappingModes.Normal;

            CreateAnchoredButton(
                "StoryStartNewGameButton",
                _storyStartChoicePanel.transform,
                "New Game",
                new Vector2(250f, 60f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-140f, 42f),
                Gold,
                StartNewStoryGame);

            _storyContinueButton = CreateAnchoredButton(
                "StoryStartContinueButton",
                _storyStartChoicePanel.transform,
                "Continue",
                new Vector2(250f, 60f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(140f, 42f),
                new Color(0.33f, 0.56f, 0.79f),
                ContinueStoryFromSave);
        }

        private void BuildSceneBanner()
        {
            _sceneBannerPanel = CreatePanel(
                "SceneBanner",
                _canvas.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -122f),
                new Vector2(620f, 118f),
                new Color(0.04f, 0.05f, 0.08f, 0f));
            _sceneBannerPanel.SetActive(false);
            _sceneBannerPanel.AddComponent<CanvasGroup>().alpha = 0f;

            _sceneBannerTitle = CreateScreenLabel(
                "SceneBannerTitle",
                _sceneBannerPanel.transform,
                string.Empty,
                34,
                TextAlignmentOptions.Center,
                new Color(0.98f, 0.94f, 0.86f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -18f),
                new Vector2(560f, 42f));

            _sceneBannerSubtitle = CreateScreenLabel(
                "SceneBannerSubtitle",
                _sceneBannerPanel.transform,
                string.Empty,
                20,
                TextAlignmentOptions.Center,
                new Color(0.84f, 0.89f, 0.95f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 20f),
                new Vector2(560f, 34f));
        }

        private void SyncStoryStateFromProfile()
        {
            if (_selectedChoice == null && !string.IsNullOrWhiteSpace(_profile.storyStarterCardId))
                _selectedChoice = _starterChoices.FirstOrDefault(choice => choice.CardId == _profile.storyStarterCardId);

            StoryQuestStep step = GetCurrentQuestStep();
            switch (step)
            {
                case StoryQuestStep.MeetSerastra:
                    SetObjective("Report to Grand Covener Serastra at the academy steps and receive your first grimoire bond.");
                    HideStarterStatus();
                    break;

                case StoryQuestStep.ChooseStarter:
                    SetObjective("Choose the daemon that answers your grimoire.");
                    HideStarterStatus();
                    break;

                case StoryQuestStep.EnterHall:
                    EnsureStarterDeckSaved(_selectedChoice);
                    SetObjective("Enter Sanction Hall for your first academy trial.");
                    ShowStarterStatus(_selectedChoice);
                    break;

                case StoryQuestStep.WinTrial:
                    EnsureStarterDeckSaved(_selectedChoice);
                    SetObjective("Step onto the Novice Trial sigil and win your first guided duel.");
                    ShowStarterStatus(_selectedChoice);
                    break;

                case StoryQuestStep.ReturnToCourtyard:
                    EnsureStarterDeckSaved(_selectedChoice);
                    SetObjective("Trial cleared. Return to the courtyard and challenge Invoker Thray near the lower grove.");
                    ShowStarterStatus(_selectedChoice);
                    break;

                case StoryQuestStep.DefeatThray:
                    EnsureStarterDeckSaved(_selectedChoice);
                    SetObjective("Challenge Invoker Thray near the lower grove and win the proving duel.");
                    ShowStarterStatus(_selectedChoice);
                    break;

                case StoryQuestStep.ReportToSerastra:
                    EnsureStarterDeckSaved(_selectedChoice);
                    SetObjective("Report your grove victory to Grand Covener Serastra at the academy steps.");
                    ShowStarterStatus(_selectedChoice);
                    break;

                default:
                    EnsureStarterDeckSaved(_selectedChoice);
                    SetObjective("Chapter complete. Explore the courtyard, bind wild daemons, or return to battle from the menu.");
                    ShowStarterStatus(_selectedChoice);
                    break;
            }

            RefreshChapterText();
        }

        private void SetObjective(string message)
        {
            if (_objectiveText != null)
                _objectiveText.text = $"OBJECTIVE\n{message}";

            RefreshChapterText();
        }

        private StoryQuestStep GetCurrentQuestStep()
        {
            if (_selectedChoice == null)
                return _introSequenceStarted ? StoryQuestStep.ChooseStarter : StoryQuestStep.MeetSerastra;

            bool trialComplete = _profile.storyTrialComplete || _profile.storyChapter >= 2;
            bool thrayDefeated = _profile.defeatedInvokerIds.Contains("invoker-thray");

            if (!trialComplete)
                return _currentArea == StoryArea.AcademyHall ? StoryQuestStep.WinTrial : StoryQuestStep.EnterHall;

            if (!thrayDefeated)
                return _currentArea == StoryArea.AcademyHall ? StoryQuestStep.ReturnToCourtyard : StoryQuestStep.DefeatThray;

            return !_profile.storyFirstInvokerReported ? StoryQuestStep.ReportToSerastra : StoryQuestStep.FreeRoam;
        }

        private void RefreshChapterText()
        {
            if (_chapterText == null)
                return;

            _chapterText.text = GetChapterLabel(GetCurrentQuestStep());
        }

        private string GetChapterLabel(StoryQuestStep step)
        {
            return step switch
            {
                StoryQuestStep.MeetSerastra or StoryQuestStep.ChooseStarter => "PROLOGUE  •  The Grimoire Answers",
                StoryQuestStep.EnterHall or StoryQuestStep.WinTrial => "CHAPTER 1  •  First Sanction",
                StoryQuestStep.ReturnToCourtyard or StoryQuestStep.DefeatThray => "CHAPTER 2  •  The Grove Proving",
                StoryQuestStep.ReportToSerastra => "CHAPTER 2  •  Report To Serastra",
                _ => "FREE ROAM  •  Aether Academy",
            };
        }

        private string GetAmbientPrompt(StoryQuestStep step)
        {
            return step switch
            {
                StoryQuestStep.MeetSerastra => "Walk to the academy steps.",
                StoryQuestStep.ChooseStarter => string.Empty,
                StoryQuestStep.EnterHall => "Walk up the steps and enter Sanction Hall.",
                StoryQuestStep.WinTrial => "Step onto the novice sigil when you're ready.",
                StoryQuestStep.ReturnToCourtyard => "Leave Sanction Hall and return to the courtyard.",
                StoryQuestStep.DefeatThray => "Find Invoker Thray near the lower-left grove.",
                StoryQuestStep.ReportToSerastra => "Return to Serastra at the academy steps.",
                _ => "Explore, bind wild daemons, or return to the menu.",
            };
        }

        private void TickMovement()
        {
            _moveProgress += Time.deltaTime / MoveDuration;
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_moveProgress));
            // Walk bob: single sine arc over the tile step (mimics GBA 2-frame walk feel)
            float walkBob = Mathf.Sin(Mathf.Clamp01(_moveProgress) * Mathf.PI) * WalkBobAmplitude;
            _playerTransform.position = Vector3.Lerp(_moveStart, _moveEnd, eased) + new Vector3(0f, walkBob, 0f);
            if (_playerRenderer != null)
                _playerRenderer.sortingOrder = GetSortOrder(_playerTile, 40);
            UpdatePlayerSpriteForFacing(_playerFacing, true);

            if (_moveProgress < 1f)
                return;

            _isMoving = false;
            _playerTransform.position = _moveEnd;
            UpdatePlayerSpriteForFacing(_playerFacing, false);
            TryTriggerWildEncounter();
        }

        private Vector2Int ReadMoveIntent()
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
                return Vector2Int.up;
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
                return Vector2Int.down;
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
                return Vector2Int.left;
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
                return Vector2Int.right;
            return Vector2Int.zero;
        }

        private void TryStartMove(Vector2Int direction)
        {
            Vector2Int targetTile = _playerTile + direction;
            if (TryAutoTransition(direction))
                return;

            if (!IsWalkable(targetTile))
                return;

            _moveStart = _playerTransform.position;
            _moveEnd = GridToWorld(targetTile);
            _moveProgress = 0f;
            _playerTile = targetTile;
            _isMoving = true;
            // Facing direction — flip sprite horizontally for left/right (GBA flipX pattern)
            _playerFacing = direction;
            if (_playerRenderer != null && direction.x != 0)
                _playerRenderer.flipX = direction.x < 0;
            UpdatePlayerSpriteForFacing(_playerFacing, true);
        }

        private bool TryAutoTransition(Vector2Int direction)
        {
            if (_selectedChoice == null)
                return false;

            if (_currentArea == StoryArea.Courtyard &&
                direction.y > 0 &&
                _playerTile.y >= 12 &&
                _playerTile.x >= 10 &&
                _playerTile.x <= 13)
            {
                TransitionToArea(StoryArea.AcademyHall, new Vector2Int(12, 4));
                return true;
            }

            if (_currentArea == StoryArea.AcademyHall &&
                direction.y < 0 &&
                _playerTile.y <= 4 &&
                _playerTile.x >= 10 &&
                _playerTile.x <= 13)
            {
                TransitionToArea(StoryArea.Courtyard, new Vector2Int(12, 12));
                return true;
            }

            return false;
        }

        private bool IsWalkable(Vector2Int tile)
        {
            if (tile.x <= 0 || tile.y <= 0 || tile.x >= MapWidth - 1 || tile.y >= MapHeight - 1)
                return false;

            if (_blockedTiles.Contains(tile))
                return false;

            return !_actorsByTile.ContainsKey(tile);
        }

        private void TryInteract()
        {
            _activeSpecialPrompt = ResolveSpecialPrompt();
            if (_activeSpecialPrompt != SpecialPrompt.None)
            {
                switch (_activeSpecialPrompt)
                {
                    case SpecialPrompt.EnterHall:
                        TransitionToArea(StoryArea.AcademyHall, new Vector2Int(12, 4));
                        break;

                    case SpecialPrompt.LeaveHall:
                        TransitionToArea(StoryArea.Courtyard, new Vector2Int(12, 12));
                        break;

                    case SpecialPrompt.BeginTrial:
                        BeginTrialBriefing();
                        break;
                }
                return;
            }

            if (_nearestActor == null)
                return;

            if (_nearestActor.IsHeadmistress)
            {
                StoryQuestStep step = GetCurrentQuestStep();
                if (_selectedChoice == null)
                {
                    BeginStarterCeremony();
                    return;
                }

                if (step == StoryQuestStep.ReportToSerastra)
                {
                    CompleteFirstInvokerReport();
                    return;
                }

                StartDialogue(new[]
                {
                    new DialogueBeat
                    {
                        Speaker = _nearestActor.DisplayName,
                        Body = GetSerastraInteractLine(step)
                    }
                },
                () =>
                {
                    SyncStoryStateFromProfile();
                    ShowStarterStatus(_selectedChoice);
                });
                return;
            }

            if (_nearestActor.IsInvoker && !_profile.defeatedInvokerIds.Contains(_nearestActor.InvokerId))
            {
                if (!_profile.storyTrialComplete && _profile.storyChapter < 2)
                {
                    StartDialogue(new[]
                    {
                        new DialogueBeat
                        {
                            Speaker = _nearestActor.DisplayName,
                            Body = "Beat the novice trial first. I only challenge invokers with a sanctioned mark."
                        }
                    });
                    return;
                }

                StartInvokerBattle(_nearestActor);
                return;
            }

            if (_nearestActor.IsInvoker)
            {
                StartDialogue(new[]
                {
                    new DialogueBeat
                    {
                        Speaker = _nearestActor.DisplayName,
                        Body = "You've already bested me. My coven mark goes back to the forge."
                    }
                });
                return;
            }

            StartDialogue(_nearestActor.Dialogue.Select(line => new DialogueBeat
            {
                Speaker = _nearestActor.DisplayName,
                Body = line
            }));
        }

        private string GetSerastraInteractLine(StoryQuestStep step)
        {
            return step switch
            {
                StoryQuestStep.EnterHall => $"{_selectedChoice.Card.cardName} has accepted your mark, {_profile.playerName}. Carry that bond into Sanction Hall.",
                StoryQuestStep.WinTrial => "Stand on the novice sigil when your grimoire is steady. Caldus will guide the first duel.",
                StoryQuestStep.ReturnToCourtyard or StoryQuestStep.DefeatThray => "Thray waits near the lower grove. Do not let the first trial be the only proof your daemon sees today.",
                StoryQuestStep.FreeRoam => "The first page of your academy record is written. Now make the grimoire stronger.",
                _ => $"{_selectedChoice.Card.cardName} has already accepted your mark, {_profile.playerName}. Keep listening to it.",
            };
        }

        private void CompleteFirstInvokerReport()
        {
            _profile.storyFirstInvokerReported = true;
            _profile.storyChapter = Mathf.Max(_profile.storyChapter, 4);
            _profile.glint += 75;
            ProfileManager.Save(_profile);

            StartDialogue(new[]
            {
                new DialogueBeat
                {
                    Speaker = "Grand Covener Serastra",
                    Body = "Thray does not yield easily. If he lowered his mark, your grimoire spoke clearly."
                },
                new DialogueBeat
                {
                    Speaker = "Grand Covener Serastra",
                    Body = "Take this academy stipend. Bind wild daemons, tune your grimware, and prepare for the next sanction bell."
                },
                new DialogueBeat
                {
                    Speaker = "Grimoire",
                    Body = "Received 75 glint. Chapter complete."
                }
            },
            SyncStoryStateFromProfile);
        }

        private void BeginStarterCeremony()
        {
            _introSequenceStarted = true;
            StartDialogue(new[]
            {
                new DialogueBeat
                {
                    Speaker = "Grand Covener Serastra",
                    Body = $"Welcome, {_profile.playerName}. Your name was written in the coven's ledger before you ever crossed these gates. That is not coincidence. That is a calling."
                },
                new DialogueBeat
                {
                    Speaker = "Grand Covener Serastra",
                    Body = "I lead this coven. And I tell every new invoker the same truth: your grimoire does not belong to you yet. It belongs to the daemon that accepts it."
                },
                new DialogueBeat
                {
                    Speaker = "Grand Covener Serastra",
                    Body = "Four grimwares stand before you. Each one is a different kind of strength. Approach them. One will answer back."
                }
            },
            OpenStarterSelection);
        }

        private void OpenStarterSelection()
        {
            _starterPanel.SetActive(true);
            SetObjective("Choose the daemon that answers your grimoire.");
        }

        private void CreateStarterOptionCard(StarterChoice choice)
        {
            GameObject card = new("StarterOption", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            card.transform.SetParent(_starterOptionContainer, false);
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(375f, 560f);

            var bg = card.GetComponent<Image>();
            bg.color = new Color(choice.Accent.r * 0.22f, choice.Accent.g * 0.22f, choice.Accent.b * 0.22f, 0.96f);

            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 12f;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var le = card.GetComponent<LayoutElement>();
            le.preferredWidth = 375f;
            le.preferredHeight = 560f;

            CreateBlockText(card.transform, choice.HouseName, 28, choice.Accent, 42f, FontStyles.Bold);
            CreateBlockText(card.transform, choice.Motto, 18, new Color(0.94f, 0.92f, 0.84f), 34f, FontStyles.Italic);

            GameObject artFrame = new("ArtworkFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            artFrame.transform.SetParent(card.transform, false);
            artFrame.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.07f, 0.95f);
            artFrame.GetComponent<LayoutElement>().preferredHeight = 250f;

            GameObject art = new("Artwork", typeof(RectTransform), typeof(Image));
            art.transform.SetParent(artFrame.transform, false);
            var artRect = art.GetComponent<RectTransform>();
            artRect.anchorMin = new Vector2(0.5f, 0.5f);
            artRect.anchorMax = new Vector2(0.5f, 0.5f);
            artRect.sizeDelta = new Vector2(190f, 220f);
            var artImage = art.GetComponent<Image>();
            artImage.preserveAspect = true;
            artImage.sprite = ResolveStarterGrimwareSprite(choice);

            string statLine = choice.Card != null
                ? $"Bound daemon: {choice.Card.cardName}\nATK {choice.Card.attack}   LIFE {choice.Card.ashe}   SE {choice.Card.asheCost}"
                : "Starter data missing";
            CreateBlockText(card.transform, choice.DeckName, 24, Color.white, 36f, FontStyles.Bold);
            CreateBlockText(card.transform, statLine, 18, new Color(0.90f, 0.86f, 0.75f), 52f, FontStyles.Bold);
            CreateBlockText(card.transform, choice.Summary, 18, new Color(0.86f, 0.89f, 0.94f), 72f, FontStyles.Normal);

            if (choice.Deck != null)
            {
                string deckSummary = $"{choice.Deck.deckName}\n{choice.Deck.description}";
                CreateBlockText(card.transform, deckSummary, 16, new Color(0.74f, 0.79f, 0.88f), 68f, FontStyles.Normal);
            }

            GameObject spacer = new("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(card.transform, false);
            spacer.GetComponent<LayoutElement>().preferredHeight = 8f;

            CreateStretchButton(card.transform, "Bind This Daemon", choice.Accent, () => ChooseStarter(choice));
        }

        private void ChooseStarter(StarterChoice choice)
        {
            if (choice == null || choice.Card == null || choice.Deck == null)
                return;

            _selectedChoice = choice;
            _profile.storyIntroComplete = true;
            _profile.storyChapter = Mathf.Max(1, _profile.storyChapter);
            _profile.storyStarterCardId = choice.Card.cardId;
            _profile.storyStarterDeckName = choice.Deck.deckName;
            _profile.storyStarterSavedDeckId = EnsureStarterDeckSaved(choice);
            StoreCurrentLocation();
            ProfileManager.Save(_profile);
            RefreshStoryMenu();

            _starterPanel.SetActive(false);
            PlayBindingBurst(choice.Accent);

            StartDialogue(new[]
            {
                new DialogueBeat
                {
                    Speaker = "Grand Covener Serastra",
                    Body = $"{choice.Card.cardName} has accepted your mark. The {choice.HouseName} grimware is yours. Guard the bond — a daemon that loses faith in its invoker is far more dangerous than any enemy."
                }
            },
            () =>
            {
                SetObjective($"Starter bonded: {choice.Card.cardName}. Enter the academy hall for your first sanctioned duel.");
                ShowStarterStatus(choice);
            });
        }

        private string EnsureStarterDeckSaved(StarterChoice choice)
        {
            if (choice?.Deck == null || _profile == null)
                return string.Empty;

            string savedDeckId = $"story-starter-{choice.CardId}";
            List<string> cardIds = ExpandDeckEntries(choice.Deck.cards, includeNonPillars: true);
            List<string> pillarIds = ExpandDeckEntries(choice.Deck.pillars, includeNonPillars: false);
            EnsureStoryStarterCraftDeck(cardIds, choice.Deck.element, choice.CardId);

            SavedDeck saved = new SavedDeck
            {
                id = savedDeckId,
                name = $"{choice.Card.cardName} Grimoire",
                element = choice.Deck.element,
                cardIds = cardIds,
                pillarIds = pillarIds,
            };

            int existingIndex = _profile.customDecks.FindIndex(deck => deck.id == savedDeckId);
            if (existingIndex >= 0)
                _profile.customDecks[existingIndex] = saved;
            else
                _profile.customDecks.Add(saved);

            HashSet<string> owned = new(_profile.ownedCardIds);
            foreach (string id in cardIds)
                owned.Add(id);
            foreach (string id in pillarIds)
                owned.Add(id);
            owned.Add(choice.Card.cardId);
            _profile.ownedCardIds = owned.ToList();

            return savedDeckId;
        }

        private void EnsureStoryStarterCraftDeck(List<string> cardIds, Element preferredElement, string starterCardId)
        {
            if (cardIds == null || _database == null)
                return;

            _database.Initialize();
            var protectedUtilityIds = new HashSet<string>();

            AddStarterUtilityCopies<SealCardData>(cardIds, protectedUtilityIds, preferredElement, 3);
            AddStarterUtilityCopies<DomainCardData>(cardIds, protectedUtilityIds, preferredElement, 1);
            AddStarterUtilityCopies<MaskCardData>(cardIds, protectedUtilityIds, preferredElement, 1);
            AddStarterUtilityCopies<DispelCardData>(cardIds, protectedUtilityIds, preferredElement, 1);

            TrimStarterDeckToSize(cardIds, protectedUtilityIds, starterCardId);
        }

        private void AddStarterUtilityCopies<T>(List<string> cardIds, HashSet<string> protectedUtilityIds, Element preferredElement, int targetCount) where T : CardData
        {
            if (targetCount <= 0)
                return;

            int existingCount = cardIds.Count(id => _database.GetCard(id) is T);
            while (existingCount < targetCount)
            {
                CardData utilityCard = PickStarterUtilityCard<T>(preferredElement, cardIds);
                if (utilityCard == null)
                    break;

                cardIds.Add(utilityCard.cardId);
                protectedUtilityIds.Add(utilityCard.cardId);
                existingCount++;
            }
        }

        private CardData PickStarterUtilityCard<T>(Element preferredElement, List<string> cardIds) where T : CardData
        {
            return _database.GetCardsByType<T>()
                .Where(card => card != null && !string.IsNullOrWhiteSpace(card.cardId))
                .OrderBy(card => ScoreStarterUtilityCard(card, preferredElement, cardIds))
                .ThenBy(card => card.cardName)
                .FirstOrDefault();
        }

        private int ScoreStarterUtilityCard(CardData card, Element preferredElement, List<string> cardIds)
        {
            int score = 0;
            if (card is DomainCardData domain && domain.effectElement != preferredElement)
                score += 20;

            score += card.GetWillCost() * 5;
            score += card.rarity switch
            {
                Rarity.Common => 0,
                Rarity.Rare => 3,
                Rarity.Epic => 6,
                Rarity.Legendary => 9,
                _ => 4
            };
            score += cardIds.Count(id => id == card.cardId) * 2;
            return score;
        }

        private void TrimStarterDeckToSize(List<string> cardIds, HashSet<string> protectedUtilityIds, string starterCardId)
        {
            while (cardIds.Count > GameConstants.DeckSize)
            {
                int removeIndex = cardIds.FindLastIndex(id =>
                    id != starterCardId &&
                    !protectedUtilityIds.Contains(id) &&
                    _database.GetCard(id) is DaemonCardData);

                if (removeIndex < 0)
                {
                    removeIndex = cardIds.FindLastIndex(id =>
                        id != starterCardId &&
                        !protectedUtilityIds.Contains(id));
                }

                if (removeIndex < 0)
                    break;

                cardIds.RemoveAt(removeIndex);
            }
        }

        private static List<string> ExpandDeckEntries(DeckEntry[] entries, bool includeNonPillars)
        {
            var ids = new List<string>();
            if (entries == null)
                return ids;

            foreach (DeckEntry entry in entries)
            {
                if (entry?.card == null)
                    continue;

                bool isPillar = entry.card is PillarCardData;
                if (!includeNonPillars && !isPillar)
                    continue;

                for (int i = 0; i < Mathf.Max(0, entry.count); i++)
                    ids.Add(entry.card.cardId);
            }

            return ids;
        }

        private void ShowStarterStatus(StarterChoice choice)
        {
            if (choice == null)
                return;

            _starterStatusPanel.SetActive(true);
            _starterStatusArt.sprite = ResolveStarterGrimwareSprite(choice);
            _starterStatusTitle.text = $"Starter Grimware: {choice.DeckName}";
            _starterStatusBody.text =
                $"{choice.HouseName}\n{choice.Motto}\n\nBound daemon: {choice.Card?.cardName}\n\n{choice.Summary}";
        }

        private void HideStarterStatus()
        {
            if (_starterStatusPanel != null)
                _starterStatusPanel.SetActive(false);
        }

        private void StartTrialDuel()
        {
            if (_selectedChoice == null)
                return;

            if (string.IsNullOrWhiteSpace(_profile.storyStarterSavedDeckId))
                _profile.storyStarterSavedDeckId = EnsureStarterDeckSaved(_selectedChoice);

            ProfileManager.Save(_profile);

            DeckConverter.SelectedDeckId = _profile.storyStarterSavedDeckId;
            DeckConverter.SelectedAIDifficulty = AIPlayer.Difficulty.Easy;
            DeckConverter.RematchAIDifficulty = AIPlayer.Difficulty.Easy;
            DeckConverter.SelectedStoryBattle = new DeckConverter.StoryBattleConfig
            {
                enableNoviceTutorial = true,
                useAcademyTrialDeck = true,
                opponentDeckName = "Academy Trial Grimoire",
                opponentName = "Proctor Caldus",
                battleTitle = "Academy Novice Trial",
                battleSubtitle = "Summon, weather the response, and land your first clean strike.",
                opponentElement = ResolveTrialOpponentElement(),
                opponentCreatureType = CreatureType.Elemental,
                storyBattleId = "academy-trial",
                storyWinChapter = 2,
                marksTrialComplete = true,
                battlebackThemeId = "city",
            };
            RuntimeAssetLocator.TryLoadScene("Battle", this);
        }

        private Element ResolveTrialOpponentElement()
        {
            Element playerElement = _selectedChoice?.Deck != null ? _selectedChoice.Deck.element : Element.Flame;
            return playerElement switch
            {
                Element.Flame => Element.Nature,
                Element.Water => Element.Flame,
                Element.Earth => Element.Water,
                Element.Dark => Element.Light,
                _ => Element.Nature,
            };
        }

        private void BeginTrialBriefing()
        {
            if (_profile.storyTrialBriefingComplete)
            {
                StartDialogue(new[]
                {
                    new DialogueBeat
                    {
                        Speaker = "Proctor Caldus",
                        Body = "Take the sigil's guidance, novice. Summon cleanly, keep your footing, and answer when your daemon is ready."
                    }
                }, StartTrialDuel);
                return;
            }

            _profile.storyTrialBriefingComplete = true;
            ProfileManager.Save(_profile);

            StartDialogue(new[]
            {
                new DialogueBeat
                {
                    Speaker = "Grand Covener Serastra",
                    Body = "This first sanctioned duel is guided. We are not measuring brilliance yet. We are measuring control."
                },
                new DialogueBeat
                {
                    Speaker = "Proctor Caldus",
                    Body = "You will summon, survive my answer, and learn the timing of a proper counterstrike. Do not rush the rhythm."
                },
                new DialogueBeat
                {
                    Speaker = "Proctor Caldus",
                    Body = "The hall will mark each step for you. Follow it, and your grimoire will remember the lesson."
                }
            }, StartTrialDuel);
        }

        private void StartDialogue(IEnumerable<DialogueBeat> beats, Action onComplete = null)
        {
            _dialogueQueue.Clear();
            _dialogueQueue.AddRange(beats.Where(beat => beat != null && !string.IsNullOrWhiteSpace(beat.Body)));
            if (_dialogueQueue.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            _dialogueCompleteAction = onComplete;
            _dialogueIndex = -1;
            _dialoguePanel.SetActive(true);
            AdvanceDialogue();
        }

        private void AdvanceDialogue()
        {
            if (_isRevealing)
            {
                FinishRevealImmediately();
                return;
            }

            _dialogueIndex++;
            if (_dialogueIndex >= _dialogueQueue.Count)
            {
                _dialoguePanel.SetActive(false);
                _speakerText.text = string.Empty;
                _dialogueText.text = string.Empty;
                Action onComplete = _dialogueCompleteAction;
                _dialogueCompleteAction = null;
                onComplete?.Invoke();
                return;
            }

            DialogueBeat beat = _dialogueQueue[_dialogueIndex];
            _speakerText.text = beat.Speaker;
            BeginReveal(beat.Body);
        }

        private void BeginReveal(string text)
        {
            if (_revealCoroutine != null)
                StopCoroutine(_revealCoroutine);

            _fullDialogueText = text ?? string.Empty;
            _revealCoroutine = StartCoroutine(RevealDialogueText());
        }

        private IEnumerator RevealDialogueText()
        {
            _isRevealing = true;
            _dialogueText.text = string.Empty;
            _dialogueAdvanceText.text = "typing...";

            for (int i = 0; i < _fullDialogueText.Length; i++)
            {
                _dialogueText.text = _fullDialogueText.Substring(0, i + 1);
                yield return new WaitForSecondsRealtime(0.015f);
            }

            _isRevealing = false;
            _dialogueAdvanceText.text = "E / Space to continue";
        }

        private void FinishRevealImmediately()
        {
            if (_revealCoroutine != null)
                StopCoroutine(_revealCoroutine);

            _revealCoroutine = null;
            _isRevealing = false;
            _dialogueText.text = _fullDialogueText;
            _dialogueAdvanceText.text = "E / Space to continue";
        }

        private void UpdateNearestActorPrompt()
        {
            _nearestActor = FindNearestActor();
            _activeSpecialPrompt = ResolveSpecialPrompt();

            if (_dialoguePanel.activeSelf || _starterPanel.activeSelf || (_storyMenuPanel != null && _storyMenuPanel.activeSelf) || (_storyStartChoicePanel != null && _storyStartChoicePanel.activeSelf))
            {
                _promptText.text = string.Empty;
                return;
            }

            if (_activeSpecialPrompt != SpecialPrompt.None)
            {
                _promptText.text = _activeSpecialPrompt switch
                {
                    SpecialPrompt.EnterHall => "Press E to enter the academy hall",
                    SpecialPrompt.LeaveHall => "Press E to return to the courtyard",
                    SpecialPrompt.BeginTrial => "Press E to begin the novice trial duel",
                    _ => string.Empty,
                };
                return;
            }

            if (_nearestActor == null)
            {
                _promptText.text = GetAmbientPrompt(GetCurrentQuestStep());
                return;
            }

            _promptText.text = $"Press E to speak with {_nearestActor.DisplayName}";
        }

        private SpecialPrompt ResolveSpecialPrompt()
        {
            if (_selectedChoice == null)
                return SpecialPrompt.None;

            if (_currentArea == StoryArea.Courtyard)
            {
                bool atHallThreshold = _playerTile.y >= 12 && _playerTile.x >= 10 && _playerTile.x <= 13;
                return atHallThreshold ? SpecialPrompt.EnterHall : SpecialPrompt.None;
            }

            bool atExit = _playerTile.y <= 4 && _playerTile.x >= 10 && _playerTile.x <= 13;
            if (atExit)
                return SpecialPrompt.LeaveHall;

            int sigilDistance = Mathf.Abs(_playerTile.x - 12) + Mathf.Abs(_playerTile.y - 9);
            bool trialComplete = _profile.storyTrialComplete || _profile.storyChapter >= 2;
            return sigilDistance <= 1 && !trialComplete ? SpecialPrompt.BeginTrial : SpecialPrompt.None;
        }

        private void TransitionToArea(StoryArea area, Vector2Int spawnTile)
        {
            _currentArea = area;
            _playerTile = spawnTile;
            StoreCurrentLocation();
            ProfileManager.Save(_profile);
            BuildWorld();
            SyncStoryStateFromProfile();
            HandleAreaArrival(area);
        }

        private void UpdateLocationLabel()
        {
            if (_locationText == null)
                return;

            _locationText.text = _currentArea == StoryArea.Courtyard
                ? "AETHER ACADEMY  •  Novice Courtyard"
                : "AETHER ACADEMY  •  Sanction Hall";

            RefreshChapterText();
        }

        private void HandleAreaArrival(StoryArea area)
        {
            StoreCurrentLocation();
            ProfileManager.Save(_profile);

            if (area == StoryArea.AcademyHall)
            {
                ShowSceneBanner("SANCTION HALL", "Novice Trial Wing", Gold, 2.1f);

                if (_profile.storyHallIntroComplete)
                    return;

                _profile.storyHallIntroComplete = true;
                ProfileManager.Save(_profile);

                StartDialogue(new[]
                {
                    new DialogueBeat
                    {
                        Speaker = "Grand Covener Serastra",
                        Body = "This is Sanction Hall. Every invoker remembers the sound of their first duel in this chamber."
                    },
                    new DialogueBeat
                    {
                        Speaker = "Proctor Caldus",
                        Body = "I will take the opposing sigil. Step onto the novice mark when you are ready, and the hall will guide your first sanctioned match."
                    }
                });
                return;
            }

            ShowSceneBanner("AETHER ACADEMY", "Novice Courtyard", Gold, 1.5f);
        }

        private bool HasStorySave()
        {
            if (_profile == null)
                return false;

            return !string.IsNullOrWhiteSpace(_profile.storyStarterCardId) ||
                   !string.IsNullOrWhiteSpace(_profile.storyAreaId) ||
                   _profile.storyChapter > 0;
        }

        private StoryArea ResolveSavedArea()
        {
            if (_profile != null && Enum.TryParse(_profile.storyAreaId, out StoryArea savedArea))
                return savedArea;

            return StoryArea.Courtyard;
        }

        private void ApplySavedStoryLocation()
        {
            _currentArea = ResolveSavedArea();
            if (_profile != null && (_profile.storyPlayerX != 0 || _profile.storyPlayerY != 0))
                _playerTile = new Vector2Int(_profile.storyPlayerX, _profile.storyPlayerY);
            else
                _playerTile = _currentArea == StoryArea.AcademyHall ? new Vector2Int(12, 4) : new Vector2Int(12, 12);
        }

        private void StoreCurrentLocation()
        {
            if (_profile == null)
                return;

            _profile.storyAreaId = _currentArea.ToString();
            _profile.storyPlayerX = _playerTile.x;
            _profile.storyPlayerY = _playerTile.y;
        }

        private void SaveStoryGame(bool showFeedback)
        {
            if (_profile == null)
                return;

            StoreCurrentLocation();
            ProfileManager.Save(_profile);
            RefreshStoryMenu();
            if (showFeedback)
                _hintText.text = "Story saved. Press Esc, Tab, or M anytime to open the story menu.";
        }

        private void LoadStoryGame()
        {
            _profile = ProfileManager.Load();
            ResolveStarterChoices();
            ApplySavedStoryLocation();
            BuildWorld();
            TickCamera();
            SyncStoryStateFromProfile();
            RefreshStoryMenu();
            CloseStoryMenu();
            if (_storyStartChoicePanel != null)
                _storyStartChoicePanel.SetActive(false);
            ShowSceneBanner(
                _currentArea == StoryArea.Courtyard ? "AETHER ACADEMY" : "SANCTION HALL",
                _currentArea == StoryArea.Courtyard ? "Novice Courtyard" : "Novice Trial Wing",
                Gold,
                1.5f);
        }

        private void ResetStoryProgressForNewGame()
        {
            _profile.storyChapter = 0;
            _profile.storyIntroComplete = false;
            _profile.storyStarterCardId = string.Empty;
            _profile.storyStarterDeckName = string.Empty;
            _profile.storyStarterSavedDeckId = string.Empty;
            _profile.storyTrialComplete = false;
            _profile.storyTrialBriefingComplete = false;
            _profile.storyHallIntroComplete = false;
            _profile.storyAreaId = StoryArea.Courtyard.ToString();
            _profile.storyPlayerX = 12;
            _profile.storyPlayerY = 12;
            _introSequenceStarted = false;
            _selectedChoice = null;
            _currentArea = StoryArea.Courtyard;
            _playerTile = new Vector2Int(12, 12);
        }

        private void ContinueStoryFromSave()
        {
            if (!HasStorySave())
                return;

            LoadStoryGame();
        }

        private void StartNewStoryGame()
        {
            ResetStoryProgressForNewGame();
            ProfileManager.Save(_profile);
            BuildWorld();
            TickCamera();
            SyncStoryStateFromProfile();
            RefreshStoryMenu();
            if (_storyStartChoicePanel != null)
                _storyStartChoicePanel.SetActive(false);
            CloseStoryMenu();
            ShowSceneBanner("AETHER ACADEMY", "Novice Courtyard", Gold, 1.8f);
        }

        private void ShowStoryStartChoice()
        {
            if (_storyStartChoicePanel == null)
                return;

            _storyStartChoicePanel.SetActive(true);
            bool hasSave = HasStorySave();
            _storyStartChoiceBody.text = hasSave
                ? $"Continue from {ResolveSavedArea()}.\n\n{GetSaveSummary()}"
                : "Begin a new story and bind your first grimware.";
            if (_storyContinueButton != null)
                _storyContinueButton.interactable = hasSave;
        }

        private void OpenStoryMenu()
        {
            if (_storyMenuPanel == null)
                return;

            RefreshStoryMenu();
            _storyMenuPanel.SetActive(true);
        }

        private void CloseStoryMenu()
        {
            if (_storyMenuPanel != null)
                _storyMenuPanel.SetActive(false);
        }

        private void ToggleStoryMenu()
        {
            if (_storyMenuPanel == null)
                return;

            if (_storyMenuPanel.activeSelf)
                CloseStoryMenu();
            else
                OpenStoryMenu();
        }

        private void RefreshStoryMenu()
        {
            if (_storyMenuBody == null)
                return;

            string starterName = _selectedChoice?.Card?.cardName ?? "None bound";
            string grimwareName = !string.IsNullOrWhiteSpace(_profile?.storyStarterDeckName) ? _profile.storyStarterDeckName : "No grimware";
            _storyMenuTitle.text = _currentArea == StoryArea.Courtyard ? "Story Menu  •  Courtyard" : "Story Menu  •  Sanction Hall";
            _storyMenuBody.text =
                $"Chapter: {_profile?.storyChapter ?? 0}\n" +
                $"Area: {_currentArea}\n" +
                $"Bound daemon: {starterName}\n" +
                $"Grimware: {grimwareName}\n" +
                $"Trial cleared: {((_profile?.storyTrialComplete ?? false) ? "Yes" : "No")}\n\n" +
                GetSaveSummary();
        }

        private string GetSaveSummary()
        {
            if (_profile == null)
                return "No story profile loaded.";

            string area = string.IsNullOrWhiteSpace(_profile.storyAreaId) ? "Courtyard" : _profile.storyAreaId;
            string starter = string.IsNullOrWhiteSpace(_profile.storyStarterCardId) ? "Unbound" : _profile.storyStarterCardId;
            return $"Save point: {area}  •  Tile {_profile.storyPlayerX},{_profile.storyPlayerY}\nStarter: {starter}";
        }

        private void SetStoryMenuPage(string title, string body)
        {
            if (_storyMenuTitle != null)
                _storyMenuTitle.text = title;
            if (_storyMenuBody != null)
                _storyMenuBody.text = body;
        }

        private SavedDeck GetStoryStarterDeck()
        {
            if (_profile?.customDecks == null)
                return null;

            if (!string.IsNullOrWhiteSpace(_profile.storyStarterSavedDeckId))
            {
                SavedDeck byId = _profile.customDecks.FirstOrDefault(deck => deck.id == _profile.storyStarterSavedDeckId);
                if (byId != null)
                    return byId;
            }

            return !string.IsNullOrWhiteSpace(_profile.storyStarterDeckName)
                ? _profile.customDecks.FirstOrDefault(deck => deck.name == _profile.storyStarterDeckName)
                : null;
        }

        private List<CardData> GetStoryDeckCards(SavedDeck deck)
        {
            if (deck?.cardIds == null || _database == null)
                return new List<CardData>();

            return deck.cardIds
                .Select(id => _database.GetCard(id))
                .Where(card => card != null)
                .ToList();
        }

        private void ShowStoryMenuHandPage()
        {
            SavedDeck deck = GetStoryStarterDeck();
            List<DaemonCardData> hand = GetStoryDeckCards(deck).OfType<DaemonCardData>().Take(6).ToList();
            var body = new StringBuilder();
            body.AppendLine("HAND");
            body.AppendLine("Your active party of bound daemons. Switch these during wild encounters.");
            body.AppendLine();

            if (hand.Count == 0)
            {
                body.AppendLine("No daemons are bound yet. Choose a starter grimware or bind a wild daemon.");
            }
            else
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    DaemonCardData daemon = hand[i];
                    int hp = Mathf.Max(6, daemon.ashe * 2 + 4);
                    body.AppendLine($"{i + 1}. {daemon.cardName}");
                    body.AppendLine($"   HP {hp}  •  ATK {Mathf.Max(1, daemon.attack)}  •  {daemon.element} / {daemon.creatureType}");
                }
            }

            SetStoryMenuPage("Hand  •  Bound Daemons", body.ToString());
        }

        private void ShowStoryMenuCraftPage()
        {
            SavedDeck deck = GetStoryStarterDeck();
            List<CardData> craft = GetStoryDeckCards(deck)
                .Where(card => !(card is DaemonCardData) && card.category != CardCategory.Pillar && card.category != CardCategory.Invoker)
                .ToList();
            var body = new StringBuilder();
            body.AppendLine("CRAFT DECK");
            body.AppendLine("Utility cards for story battles: Seals bind, Domains build SE, Masks heal, Dispels weaken.");
            body.AppendLine();

            if (craft.Count == 0)
            {
                body.AppendLine("No craft cards are saved in this grimware yet.");
            }
            else
            {
                foreach (IGrouping<CardCategory, CardData> group in craft.GroupBy(card => card.category).OrderBy(group => group.Key.ToString()))
                {
                    body.AppendLine($"{group.Key}");
                    foreach (CardData card in group.Take(6))
                        body.AppendLine($"   • {card.cardName}");
                }
            }

            SetStoryMenuPage("Craft  •  Items & Cards", body.ToString());
        }

        private void ShowStoryMenuGrimwarePage()
        {
            SavedDeck deck = GetStoryStarterDeck();
            var ids = new HashSet<string>();
            if (_profile?.ownedCardIds != null)
                foreach (string id in _profile.ownedCardIds)
                    ids.Add(id);
            if (_profile?.caughtWildCardIds != null)
                foreach (string id in _profile.caughtWildCardIds)
                    ids.Add(id);
            if (deck?.cardIds != null)
                foreach (string id in deck.cardIds)
                    ids.Add(id);

            List<DaemonCardData> daemons = ids
                .Select(id => _database?.GetCard(id))
                .OfType<DaemonCardData>()
                .OrderBy(card => card.element.ToString())
                .ThenBy(card => card.cardName)
                .ToList();

            var body = new StringBuilder();
            body.AppendLine("GRIMWARE");
            body.AppendLine("Your storage box for bound daemons. Wild binds are added here.");
            body.AppendLine();
            body.AppendLine($"Active grimware: {(deck?.name ?? "None")}");
            body.AppendLine($"Daemons stored: {daemons.Count}");
            body.AppendLine();

            foreach (DaemonCardData daemon in daemons.Take(10))
                body.AppendLine($"• {daemon.cardName} — {daemon.element} / {daemon.creatureType}");
            if (daemons.Count > 10)
                body.AppendLine($"...and {daemons.Count - 10} more.");

            SetStoryMenuPage("Grimware  •  Storage Box", body.ToString());
        }

        private void ShowStoryMenuCharacterPage()
        {
            var body = new StringBuilder();
            body.AppendLine(_profile?.playerName ?? "Invoker");
            body.AppendLine($"Rank {_profile?.rank ?? 1}  •  XP {_profile?.xp ?? 0}");
            body.AppendLine($"Glint {_profile?.glint ?? 0}  •  Embers {_profile?.embers ?? 0}");
            body.AppendLine();
            body.AppendLine($"Chapter: {_profile?.storyChapter ?? 0}");
            body.AppendLine($"Trial cleared: {((_profile?.storyTrialComplete ?? false) ? "Yes" : "No")}");
            body.AppendLine($"Defeated invokers: {_profile?.defeatedInvokerIds?.Count ?? 0}");
            body.AppendLine();
            body.AppendLine("Controls");
            body.AppendLine("WASD / Arrow Keys: move");
            body.AppendLine("E / Space: interact");
            body.AppendLine("M / Tab / Esc: open or close this menu");

            SetStoryMenuPage("Character  •  Invoker", body.ToString());
        }

        private void ShowSceneBanner(string title, string subtitle, Color accent, float duration)
        {
            if (_sceneBannerPanel == null)
                return;

            if (_sceneBannerRoutine != null)
                StopCoroutine(_sceneBannerRoutine);

            _sceneBannerRoutine = StartCoroutine(PlaySceneBanner(title, subtitle, accent, duration));
        }

        private IEnumerator PlaySceneBanner(string title, string subtitle, Color accent, float duration)
        {
            if (_sceneBannerPanel == null)
                yield break;

            CanvasGroup group = _sceneBannerPanel.GetComponent<CanvasGroup>();
            Image image = _sceneBannerPanel.GetComponent<Image>();
            if (group == null || image == null)
                yield break;

            _sceneBannerPanel.SetActive(true);
            _sceneBannerTitle.text = title;
            _sceneBannerSubtitle.text = subtitle;
            image.color = new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, 0f);
            group.alpha = 0f;

            float fadeIn = 0.24f;
            float hold = Mathf.Max(0.65f, duration);
            float fadeOut = 0.28f;

            for (float t = 0f; t < fadeIn; t += Time.deltaTime)
            {
                float alpha = Mathf.Clamp01(t / fadeIn);
                group.alpha = alpha;
                image.color = new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, alpha * 0.95f);
                yield return null;
            }

            group.alpha = 1f;
            image.color = new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, 0.95f);
            yield return new WaitForSecondsRealtime(hold);

            for (float t = 0f; t < fadeOut; t += Time.deltaTime)
            {
                float alpha = 1f - Mathf.Clamp01(t / fadeOut);
                group.alpha = alpha;
                image.color = new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, alpha * 0.95f);
                yield return null;
            }

            group.alpha = 0f;
            _sceneBannerPanel.SetActive(false);
            _sceneBannerRoutine = null;
        }

        private StoryActor FindNearestActor()
        {
            StoryActor nearest = null;
            int bestDistance = int.MaxValue;
            foreach (StoryActor actor in _actorsByTile.Values)
            {
                int distance = Mathf.Abs(actor.Tile.x - _playerTile.x) + Mathf.Abs(actor.Tile.y - _playerTile.y);
                if (distance <= 1 && distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = actor;
                }
            }

            return nearest;
        }

        private void UpdateActorHighlight()
        {
            foreach (StoryActor actor in _actorsByTile.Values)
            {
                Color baseColor = actor.BaseTint;
                if (actor == _nearestActor)
                {
                    baseColor = Color.Lerp(actor.BaseTint, new Color(1f, 0.96f, 0.80f), 0.45f);
                    // NPC faces toward player when adjacent (GBA FacingHandler pattern)
                    if (actor.Renderer != null)
                    {
                        int dx = _playerTile.x - actor.Tile.x;
                        int dy = _playerTile.y - actor.Tile.y;
                        if (Mathf.Abs(dx) >= Mathf.Abs(dy) && dx != 0)
                            actor.Facing = dx > 0 ? Vector2Int.right : Vector2Int.left;
                        else if (dy != 0)
                            actor.Facing = dy > 0 ? Vector2Int.up : Vector2Int.down;

                        Sprite idleFrame = GetStoryCharacterFrame(actor.SpriteAssetName, actor.Facing, false);
                        if (idleFrame != null)
                            actor.Renderer.sprite = idleFrame;

                        if (dx != 0)
                            actor.Renderer.flipX = dx < 0;
                    }
                }
                else if (actor.Renderer != null)
                {
                    // Restore facing from last wander direction
                    Sprite idleFrame = GetStoryCharacterFrame(actor.SpriteAssetName, actor.Facing, false);
                    if (idleFrame != null)
                        actor.Renderer.sprite = idleFrame;
                    actor.Renderer.flipX = actor.Facing.x < 0;
                }
                actor.Renderer.color = baseColor;
            }
        }

        private void AnimateWorldProps()
        {
            float t = Time.time;
            foreach ((Transform transform, Vector3 basePosition, float amplitude, float speed, float phase) entry in _hoveringProps)
            {
                if (entry.transform == null)
                    continue;

                Vector3 pos = entry.basePosition;
                pos.y += Mathf.Sin(t * entry.speed + entry.phase) * entry.amplitude;
                pos.y = Mathf.Round(pos.y * 100f) / 100f;
                entry.transform.position = pos;
            }

            foreach (WaterShimmerProp shimmer in _waterShimmers)
            {
                if (shimmer?.Transform == null)
                    continue;

                Vector3 pos = shimmer.BasePosition;
                pos.x += Mathf.Sin(t * shimmer.HorizontalSpeed + shimmer.Phase * 0.7f) * shimmer.HorizontalAmplitude;
                pos.y += Mathf.Sin(t * shimmer.VerticalSpeed + shimmer.Phase) * shimmer.VerticalAmplitude;
                pos.x = Mathf.Round(pos.x * 100f) / 100f;
                pos.y = Mathf.Round(pos.y * 100f) / 100f;
                shimmer.Transform.position = pos;
                shimmer.Transform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    shimmer.BaseRotation + Mathf.Sin(t * shimmer.HorizontalSpeed + shimmer.Phase) * shimmer.RotationAmplitude);

                if (shimmer.Renderer != null)
                {
                    Color color = shimmer.BaseColor;
                    color.a = Mathf.Lerp(
                        shimmer.AlphaMin,
                        shimmer.AlphaMax,
                        0.5f + Mathf.Sin(t * shimmer.AlphaSpeed + shimmer.Phase) * 0.5f);
                    shimmer.Renderer.color = color;
                }
            }

            if (_playerRenderer != null && !_isMoving)
                _playerRenderer.color = Color.Lerp(Color.white, new Color(0.88f, 0.93f, 1f), 0.15f + Mathf.Sin(t * 3f) * 0.05f);
        }

        private void PlayBindingBurst(Color accent)
        {
            for (int i = 0; i < 10; i++)
            {
                var sparkle = CreateWorldSprite(
                    $"BindingBurst_{i}",
                    GetSealSprite($"binding-{i}"),
                    _playerTransform.position + new Vector3(UnityEngine.Random.Range(-0.18f, 0.18f), UnityEngine.Random.Range(0.2f, 0.8f), 0f),
                    300,
                    _worldRoot,
                    0.34f,
                    new Color(accent.r, accent.g, accent.b, 0.92f));

                StartCoroutine(FadeAndRise(sparkle, UnityEngine.Random.Range(0.55f, 0.95f)));
            }
        }

        private IEnumerator FadeAndRise(Transform target, float duration)
        {
            if (target == null)
                yield break;

            var renderer = target.GetComponent<SpriteRenderer>();
            Vector3 start = target.position;
            Vector3 end = start + new Vector3(UnityEngine.Random.Range(-0.25f, 0.25f), UnityEngine.Random.Range(0.65f, 1.05f), 0f);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                target.position = Vector3.Lerp(start, end, t);
                if (renderer != null)
                {
                    Color c = renderer.color;
                    c.a = 1f - t;
                    renderer.color = c;
                }
                yield return null;
            }

            if (target != null)
                Destroy(target.gameObject);
        }

        // ── NPC Wander (GBA-style: each actor shuffles randomly within a small radius) ─────────

        private void TickNpcWander()
        {
            if (_dialoguePanel != null && _dialoguePanel.activeSelf) return;
            if (_starterPanel != null && _starterPanel.activeSelf) return;

            _npcWanderTimer -= Time.deltaTime;
            if (_npcWanderTimer > 0f) return;
            _npcWanderTimer = NpcWanderInterval + UnityEngine.Random.Range(-0.4f, 0.9f);

            foreach (StoryActor actor in _actorsByTile.Values.ToList())
            {
                if (!actor.CanWander) continue;
                if (actor == _nearestActor) continue; // freeze when player is adjacent
                TryWanderActor(actor);
            }
        }

        private void TryWanderActor(StoryActor actor)
        {
            // Shuffle direction order so wandering is unpredictable
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }

            foreach (Vector2Int dir in dirs)
            {
                Vector2Int target = actor.Tile + dir;

                // Stay within Manhattan radius 2 of home tile (GBA wander radius)
                if (Mathf.Abs(target.x - actor.HomeTile.x) + Mathf.Abs(target.y - actor.HomeTile.y) > 2)
                    continue;
                if (target.x <= 0 || target.y <= 0 || target.x >= MapWidth - 1 || target.y >= MapHeight - 1)
                    continue;
                if (_blockedTiles.Contains(target)) continue;
                if (_actorsByTile.ContainsKey(target)) continue;
                if (target == _playerTile) continue;

                // Commit movement: update tile registry, facing, flipX
                _actorsByTile.Remove(actor.Tile);
                actor.Tile = target;
                actor.Facing = dir;
                if (actor.Renderer != null && dir.x != 0)
                    actor.Renderer.flipX = dir.x < 0;
                _actorsByTile[target] = actor;

                StartCoroutine(SmoothMoveActor(actor, GridToWorld(target)));
                return; // one NPC moves per wander tick
            }
        }

        private IEnumerator SmoothMoveActor(StoryActor actor, Vector3 destination)
        {
            const float duration = 0.22f;
            const float bobAmplitude = 0.06f;
            Vector3 origin = actor.Transform.position;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                float bob = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * bobAmplitude;
                actor.Transform.position = Vector3.Lerp(origin, destination, eased) + new Vector3(0f, bob, 0f);
                if (actor.Renderer != null)
                {
                    actor.Renderer.sortingOrder = GetSortOrder(actor.Tile, 40);
                    Sprite walkFrame = GetStoryCharacterFrame(actor.SpriteAssetName, actor.Facing, true);
                    if (walkFrame != null)
                        actor.Renderer.sprite = walkFrame;
                }
                yield return null;
            }
            actor.Transform.position = destination;
            if (actor.Renderer != null)
            {
                actor.Renderer.sortingOrder = GetSortOrder(actor.Tile, 40);
                Sprite idleFrame = GetStoryCharacterFrame(actor.SpriteAssetName, actor.Facing, false);
                if (idleFrame != null)
                    actor.Renderer.sprite = idleFrame;
            }
        }

        private StoryActor CreateActor(string id, string displayName, Vector2Int tile, Sprite sprite, string[] dialogue, bool isHeadmistress, string spriteAssetNameOverride = null)
        {
            string resolvedSpriteAssetName = string.IsNullOrWhiteSpace(spriteAssetNameOverride) ? id : spriteAssetNameOverride;
            Sprite assetSprite = GetStoryCharacterFrame(resolvedSpriteAssetName, Vector2Int.down, false);
            string spriteAssetName = null;
            if (assetSprite != null)
            {
                sprite = assetSprite;
                spriteAssetName = resolvedSpriteAssetName;
            }

            Transform actorTransform = CreateWorldSprite(
                id,
                sprite,
                GridToWorld(tile),
                GetSortOrder(tile, 40),
                _worldRoot);
            var renderer = actorTransform.GetComponent<SpriteRenderer>();

            StoryActor actor = new()
            {
                Id = id,
                DisplayName = displayName,
                Tile = tile,
                Transform = actorTransform,
                Renderer = renderer,
                BaseTint = ResolveActorTint(id, isHeadmistress),
                SpriteAssetName = spriteAssetName,
                Dialogue = dialogue ?? Array.Empty<string>(),
                IsHeadmistress = isHeadmistress
            };

            if (renderer != null)
                renderer.color = actor.BaseTint;

            AddActorCovenMark(actor);
            _actorsByTile[tile] = actor;
            return actor;
        }

        private Color ResolveActorTint(string actorId, bool isHeadmistress)
        {
            if (isHeadmistress)
                return new Color(0.94f, 0.90f, 1f);

            int hash = Mathf.Abs((actorId ?? string.Empty).GetHashCode());
            float hue = (hash % 1000) / 1000f;
            Color tint = Color.HSVToRGB(hue, 0.18f, 0.95f);
            return new Color(
                Mathf.Lerp(0.82f, tint.r, 0.42f),
                Mathf.Lerp(0.82f, tint.g, 0.42f),
                Mathf.Lerp(0.82f, tint.b, 0.42f),
                1f);
        }

        private Color ResolveActorMarkColor(string actorId)
        {
            int hash = Mathf.Abs((actorId ?? string.Empty).GetHashCode());
            float hue = (hash % 1000) / 1000f;
            Color color = Color.HSVToRGB(hue, 0.58f, 0.95f);
            return new Color(color.r, color.g, color.b, 0.82f);
        }

        private void AddActorCovenMark(StoryActor actor)
        {
            if (actor?.Transform == null)
                return;

            Sprite seal = GetSealSprite("seal-glyph");
            if (seal == null)
                return;

            Transform mark = CreateWorldSprite(
                $"{actor.Id}_Mark",
                seal,
                actor.Transform.position + new Vector3(0f, 0.68f, 0f),
                GetSortOrder(actor.Tile, 65),
                _worldRoot,
                0.22f,
                ResolveActorMarkColor(actor.Id));
            _hoveringProps.Add((mark, mark.position, 0.03f, 1.8f, actor.Tile.x * 0.17f + actor.Tile.y * 0.11f));
        }

        private void AddBorderShrub(Vector2Int tile)
        {
            int variant = Mathf.Abs(tile.x * 31 + tile.y * 17);
            string fileName = (variant % 3 == 0) ? "Object tree 2.PNG" : "Object tree 1.png";
            Sprite shrubSprite = LoadEnvironmentCharacterCell(fileName, variant % 4, 0, 32f, new Vector2(0.5f, 0.08f))
                ?? GetTileSprite("border-shrub", new Color(0.11f, 0.30f, 0.18f), new Color(0.08f, 0.24f, 0.13f), new Color(0.32f, 0.64f, 0.35f));
            Transform shrub = CreateWorldSprite($"BorderShrub_{tile.x}_{tile.y}",
                shrubSprite,
                GridToWorld(tile),
                GetSortOrder(tile, 10),
                _worldRoot);
            if (shrubSprite != null)
                FitWorldSpriteToBounds(shrub, 1.35f, 1.35f);
            _blockedTiles.Add(tile);
        }

        private void AddGardenBed(int x, int y)
        {
            Vector2Int tile = new(x, y);
            int variant = Mathf.Abs(x * 13 + y * 7);
            string fileName = (variant % 2 == 0) ? "Flowers1.png" : "Flowers2.png";
            Sprite flowerSprite = LoadFlowerTile(fileName, variant % 5)
                ?? GetTileSprite("garden-bed", new Color(0.24f, 0.32f, 0.19f), new Color(0.16f, 0.24f, 0.13f), new Color(0.71f, 0.31f, 0.57f));
            Transform bed = CreateWorldSprite($"Garden_{x}_{y}",
                flowerSprite,
                GridToWorld(tile),
                GetSortOrder(tile, 6),
                _worldRoot);
            if (flowerSprite != null)
                FitWorldSpriteToBounds(bed, 1.05f, 1.05f);
            _blockedTiles.Add(tile);
        }

        private void AddColumn(int x, int y)
        {
            for (int dy = 0; dy < 2; dy++)
            {
                Vector2Int tile = new(x, y + dy);
                CreateWorldSprite($"Column_{x}_{y}_{dy}",
                    GetTileSprite("column", new Color(0.78f, 0.73f, 0.64f), new Color(0.62f, 0.56f, 0.49f), Gold),
                    GridToWorld(tile),
                    GetSortOrder(tile, 28),
                    _worldRoot);
            }
        }

        private void AddHallWall(Vector2Int tile)
        {
            CreateWorldSprite($"HallWall_{tile.x}_{tile.y}",
                GetTileSprite("hall-wall", new Color(0.42f, 0.40f, 0.46f), new Color(0.30f, 0.28f, 0.34f), new Color(0.60f, 0.58f, 0.66f)),
                GridToWorld(tile),
                GetSortOrder(tile, 12),
                _worldRoot);
            _blockedTiles.Add(tile);
        }

        private void AddBookshelf(int x, int y)
        {
            Vector2Int tile = new(x, y);
            CreateWorldSprite($"Bookshelf_{x}_{y}",
                GetTileSprite("bookshelf", new Color(0.34f, 0.22f, 0.12f), new Color(0.24f, 0.14f, 0.08f), new Color(0.86f, 0.72f, 0.34f)),
                GridToWorld(tile),
                GetSortOrder(tile, 20),
                _worldRoot);
            _blockedTiles.Add(tile);
        }

        private bool IsPathTile(int x, int y)
        {
            if (x >= 10 && x <= 13 && y <= 13)
                return true;
            return y >= 10 && y <= 12 && x >= 7 && x <= 16;
        }

        private bool IsWaterTile(int x, int y)
        {
            bool leftPond = x >= 3 && x <= 4 && y >= 6 && y <= 8;
            bool rightPond = x >= 19 && x <= 20 && y >= 6 && y <= 8;
            return leftPond || rightPond;
        }

        private static bool PressedConfirm()
        {
            return Input.GetKeyDown(KeyCode.E)
                || Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter);
        }

        private static int GetSortOrder(Vector2Int tile, int offset = 0)
        {
            return 400 - tile.y * 10 + offset;
        }

        private static Vector3 GridToWorld(Vector2Int tile)
        {
            float x = (tile.x - (MapWidth - 1) * 0.5f) * TileSize;
            float y = (tile.y - (MapHeight - 1) * 0.5f) * TileSize;
            return new Vector3(x, y, 0f);
        }

        private Transform CreateWorldSprite(string name, Sprite sprite, Vector3 position, int sortingOrder, Transform parent, float scale = 1f, Color? tint = null)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            renderer.color = tint ?? Color.white;
            return go.transform;
        }

        private static void FitWorldSpriteToBounds(Transform target, float maxWidth, float maxHeight)
        {
            if (target == null)
                return;

            var renderer = target.GetComponent<SpriteRenderer>();
            if (renderer?.sprite == null)
                return;

            Vector2 spriteSize = renderer.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
                return;

            float uniformScale = Mathf.Min(maxWidth / spriteSize.x, maxHeight / spriteSize.y);
            target.localScale = Vector3.one * uniformScale;
        }

        private void CreateWorldText(string name, string message, Vector3 position, float scale, Color color, int sortingOrder)
        {
            var go = new GameObject(name, typeof(TextMesh));
            go.transform.SetParent(_worldRoot, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (0.12f * scale);
            var text = go.GetComponent<TextMesh>();
            text.text = message;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.25f;
            text.fontSize = 64;
            text.color = color;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = sortingOrder;
        }

        private Sprite GetTileSprite(string key, Color baseColor, Color shadeColor, Color accentColor)
        {
            if (_spriteCache.TryGetValue(key, out Sprite cached))
                return cached;

            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = key
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color pixel = ((x + y) % 2 == 0) ? baseColor : shadeColor;
                    if (x == 0 || y == 0 || x == size - 1 || y == size - 1)
                        pixel = Color.Lerp(shadeColor, Color.black, 0.15f);
                    if ((x == 4 && y == 4) || (x == 11 && y == 8) || (x == 7 && y == 12))
                        pixel = accentColor;
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _spriteCache[key] = sprite;
            return sprite;
        }

        private Sprite GetSealSprite(string key)
        {
            if (_spriteCache.TryGetValue(key, out Sprite cached))
                return cached;

            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = key
            };

            Color clear = new(0f, 0f, 0f, 0f);
            Color ring = new(1f, 1f, 1f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - (size - 1) * 0.5f;
                    float dy = y - (size - 1) * 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    Color pixel = clear;
                    if (Mathf.Abs(dist - 5f) < 0.6f || Mathf.Abs(dist - 2.2f) < 0.6f || Mathf.Abs(dx) < 0.35f || Mathf.Abs(dy) < 0.35f)
                        pixel = ring;
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _spriteCache[key] = sprite;
            return sprite;
        }

        private Sprite CreateCharacterSprite(string key, Color robePrimary, Color robeSecondary, Color trim)
        {
            if (_spriteCache.TryGetValue(key, out Sprite cached))
                return cached;

            const int width = 16;
            const int height = 24;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = key
            };

            Color clear = new(0f, 0f, 0f, 0f);
            Color skin = new(0.96f, 0.79f, 0.62f, 1f);
            Color skinShade = Color.Lerp(skin, Ink, 0.16f);
            Color hair = new(0.19f, 0.12f, 0.08f, 1f);
            Color hairHighlight = Color.Lerp(hair, new Color(0.40f, 0.28f, 0.18f), 0.35f);
            Color boot = Color.Lerp(robePrimary, new Color(0.08f, 0.06f, 0.04f), 0.50f);
            Color robeHighlight = Color.Lerp(robePrimary, Color.white, 0.14f);
            Color cloakShade = Color.Lerp(robeSecondary, Ink, 0.24f);
            Color trimHighlight = Color.Lerp(trim, Color.white, 0.18f);

            void SetPixel(int x, int y, Color color)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    return;

                texture.SetPixel(x, y, color);
            }

            void FillRow(int y, int xMin, int xMax, Color color)
            {
                for (int x = xMin; x <= xMax; x++)
                    SetPixel(x, y, color);
            }

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    texture.SetPixel(x, y, clear);

            for (int x = 2; x <= 13; x++)
            {
                float falloff = 1f - Mathf.Clamp01(Mathf.Abs(x - 7.5f) / 5.5f);
                SetPixel(x, 0, new Color(0.04f, 0.03f, 0.05f, 0.44f * falloff * falloff));
            }

            FillRow(1, 5, 6, boot);
            FillRow(1, 9, 10, boot);
            FillRow(2, 4, 6, boot);
            FillRow(2, 9, 11, boot);

            FillRow(3, 4, 11, trim);
            FillRow(4, 4, 11, robeSecondary);
            FillRow(5, 4, 11, robeSecondary);
            FillRow(6, 5, 10, robeSecondary);
            FillRow(7, 5, 10, cloakShade);
            FillRow(8, 5, 10, trim);
            FillRow(9, 5, 10, robePrimary);
            FillRow(10, 4, 11, robePrimary);
            FillRow(11, 4, 11, robePrimary);
            FillRow(12, 4, 11, robePrimary);
            FillRow(13, 4, 11, robeHighlight);
            FillRow(14, 5, 10, cloakShade);
            FillRow(15, 6, 9, trim);

            FillRow(16, 5, 10, robeSecondary);
            SetPixel(4, 15, robeSecondary);
            SetPixel(11, 15, robeSecondary);
            SetPixel(4, 16, robeSecondary);
            SetPixel(11, 16, robeSecondary);
            SetPixel(4, 17, cloakShade);
            SetPixel(11, 17, cloakShade);

            FillRow(9, 3, 4, cloakShade);
            FillRow(9, 11, 12, cloakShade);
            FillRow(10, 3, 4, robeSecondary);
            FillRow(10, 11, 12, robeSecondary);
            FillRow(11, 3, 4, robePrimary);
            FillRow(11, 11, 12, robePrimary);
            FillRow(12, 4, 4, trim);
            FillRow(12, 11, 11, trim);

            FillRow(9, 7, 8, trimHighlight);
            FillRow(10, 7, 8, trim);
            FillRow(11, 7, 8, trim);
            FillRow(12, 7, 8, trimHighlight);
            SetPixel(6, 13, trim);
            SetPixel(9, 13, trim);

            SetPixel(7, 15, skin);
            SetPixel(8, 15, skin);
            FillRow(16, 6, 9, skin);
            FillRow(17, 5, 10, skin);
            FillRow(18, 5, 10, skin);
            FillRow(19, 5, 10, skin);
            FillRow(20, 6, 9, skin);
            FillRow(21, 6, 9, skinShade);

            FillRow(20, 4, 11, hair);
            FillRow(21, 4, 11, hair);
            FillRow(22, 4, 11, hair);
            FillRow(23, 5, 10, hair);
            FillRow(19, 4, 11, hair);
            SetPixel(5, 18, hair);
            SetPixel(10, 18, hair);
            SetPixel(4, 18, hair);
            SetPixel(11, 18, hair);
            SetPixel(4, 17, hair);
            SetPixel(11, 17, hair);
            SetPixel(5, 22, hairHighlight);
            SetPixel(6, 22, hairHighlight);
            SetPixel(7, 22, hairHighlight);

            SetPixel(6, 18, new Color(0.11f, 0.08f, 0.07f, 1f));
            SetPixel(9, 18, new Color(0.11f, 0.08f, 0.07f, 1f));
            SetPixel(7, 17, new Color(1f, 0.92f, 0.86f, 0.7f));
            SetPixel(8, 17, new Color(1f, 0.92f, 0.86f, 0.55f));
            SetPixel(7, 16, skinShade);
            SetPixel(8, 16, skinShade);
            SetPixel(7, 20, new Color(0.66f, 0.44f, 0.36f, 1f));
            SetPixel(8, 20, new Color(0.66f, 0.44f, 0.36f, 1f));

            SetPixel(4, 4, trimHighlight);
            SetPixel(11, 4, trimHighlight);
            SetPixel(5, 6, Color.Lerp(robeSecondary, Color.white, 0.08f));
            SetPixel(10, 6, Color.Lerp(robeSecondary, Color.white, 0.08f));
            SetPixel(5, 11, robeHighlight);
            SetPixel(10, 11, robeHighlight);
            SetPixel(6, 5, cloakShade);
            SetPixel(9, 5, cloakShade);
            SetPixel(7, 4, Color.Lerp(robeSecondary, Color.white, 0.12f));
            SetPixel(8, 4, Color.Lerp(robeSecondary, Color.white, 0.12f));

            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.08f), 16f);
            _spriteCache[key] = sprite;
            return sprite;
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = ResolvePivot(anchorMin, anchorMax);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            go.GetComponent<Image>().color = color;
            return go;
        }

        private TextMeshProUGUI CreateScreenLabel(string name, Transform parent, string text, int fontSize,
            TextAlignmentOptions alignment, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = ResolvePivot(anchorMin, anchorMax);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        private void CreateBlockText(Transform parent, string text, int fontSize, Color color, float preferredHeight, FontStyles style)
        {
            var go = new GameObject("BlockText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = preferredHeight;
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
        }

        private Button CreateButton(string name, Transform parent, string label, Vector2 size, Vector2 anchoredPosition, Color color, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            CreateScreenLabel(
                "Label",
                go.transform,
                label,
                21,
                TextAlignmentOptions.Center,
                Ink,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            return button;
        }

        private Button CreateAnchoredButton(string name, Transform parent, string label, Vector2 size, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Color color, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = ResolvePivot(anchorMin, anchorMax);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            CreateScreenLabel(
                "Label",
                go.transform,
                label,
                20,
                TextAlignmentOptions.Center,
                Ink,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            return button;
        }

        private void CreateStretchButton(Transform parent, string label, Color color, Action onClick)
        {
            var go = new GameObject("ChoiceButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 54f;
            var image = go.GetComponent<Image>();
            image.color = color;
            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            CreateScreenLabel(
                "Label",
                go.transform,
                label,
                21,
                TextAlignmentOptions.Center,
                Ink,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
        }

        private static Vector2 ResolvePivot(Vector2 anchorMin, Vector2 anchorMax)
        {
            return Mathf.Approximately(anchorMin.x, anchorMax.x) && Mathf.Approximately(anchorMin.y, anchorMax.y)
                ? anchorMin
                : new Vector2(0.5f, 0.5f);
        }

        private Sprite ResolveStarterGrimwareSprite(StarterChoice choice)
        {
            string fileName = choice?.CardId switch
            {
                "d-flame-6" => "SpellBookDemo1.png",
                "d-water-2" => "SpellDemo2.png",
                "d-earth-6" => "SpellDemo3.png",
                "d-dark-4" => "BookwithArrow.png",
                _ => "BookExample.png"
            };

            return LoadStreamingSprite(Path.Combine("Grimware", fileName), null, 96f);
        }

        // ─── Camera ───────────────────────────────────────────────────────

        private void TickCamera()
        {
            if (_camera == null || _playerTransform == null) return;
            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;
            float mapHalfW = (MapWidth  - 1) * 0.5f * TileSize;
            float mapHalfH = (MapHeight - 1) * 0.5f * TileSize;
            float tx = _playerTransform.position.x;
            float ty = _playerTransform.position.y + 1.2f; // look slightly ahead of player
            float minX = halfW < mapHalfW ? -mapHalfW + halfW : 0f;
            float maxX = halfW < mapHalfW ? mapHalfW - halfW : 0f;
            float minY = halfH < mapHalfH ? -mapHalfH + halfH : 0f;
            float maxY = halfH < mapHalfH ? mapHalfH - halfH : 0f;
            float cx = Mathf.Clamp(tx, minX, maxX);
            float cy = Mathf.Clamp(ty, minY, maxY);
            Vector3 desired = new(cx, cy, _camera.transform.position.z);
            float follow = 1f - Mathf.Exp(-14f * Time.deltaTime);
            _camera.transform.position = Vector3.Lerp(_camera.transform.position, desired, follow);
        }

        // ─── Sprite helpers ───────────────────────────────────────────────

        private Sprite GetSparkleSprite()
        {
            const string key = "__sparkle__";
            if (_spriteCache.TryGetValue(key, out Sprite cached)) return cached;
            const int size = 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = key };
            float cx = (size - 1) * 0.5f, cy2 = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy2;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    bool cross = Mathf.Abs(dx) < 0.7f || Mathf.Abs(dy) < 0.7f;
                    float a = cross
                        ? Mathf.Clamp01(1.3f - dist / 3.2f)
                        : Mathf.Clamp01(1f  - dist / 2.2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, true);
            Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _spriteCache[key] = s;
            return s;
        }

        private Sprite GetShimmerLineSprite()
        {
            const string key = "__shimmer-line__";
            if (_spriteCache.TryGetValue(key, out Sprite cached))
                return cached;

            const int width = 12;
            const int height = 6;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = key
            };

            float cx = (width - 1) * 0.5f;
            float cy = (height - 1) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Abs(x - cx) / (width * 0.5f);
                    float dy = Mathf.Abs(y - cy) / (height * 0.5f);
                    float core = Mathf.Clamp01(1f - dx * 1.05f - dy * 1.9f);
                    float glow = Mathf.Clamp01(1f - dx * 0.72f - dy * 0.95f);
                    float alpha = Mathf.Max(core, glow * 0.58f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), width);
            _spriteCache[key] = sprite;
            return sprite;
        }

        private Sprite GetDropShadowSprite()
        {
            const string key = "__drop-shadow__";
            if (_spriteCache.TryGetValue(key, out Sprite cached)) return cached;
            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = key };
            float cx = (size - 1) * 0.5f, cy2 = (size - 1) * 0.5f;
            const float rx = 5.5f, ry = 2.6f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float ddx = (x - cx) / rx;
                    float ddy = (y - cy2) / ry;
                    float d = ddx * ddx + ddy * ddy;
                    float a = Mathf.Clamp01(1f - (d - 0.55f) * 3f);
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                }
            tex.Apply(false, true);
            Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _spriteCache[key] = s;
            return s;
        }

        // ─── Wild Daemon Encounters ────────────────────────────────────────

        private void BuildEncounterPanel()
        {
            _encounterPanel = CreatePanel(
                "EncounterPanel",
                _canvas.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Color(0.04f, 0.04f, 0.06f, 0.92f));
            _encounterPanel.SetActive(false);

            // Inner card
            var card = CreatePanel(
                "EncounterCard",
                _encounterPanel.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(640f, 560f),
                new Color(0.09f, 0.09f, 0.13f, 1f));

            CreateScreenLabel(
                "EyeLabel",
                card.transform,
                "— A daemon stirs in the grove —",
                18,
                TextAlignmentOptions.Center,
                new Color(0.75f, 0.68f, 0.52f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -28f),
                new Vector2(520f, 32f));

            _encounterTitle = CreateScreenLabel(
                "EncounterTitle",
                card.transform,
                string.Empty,
                36,
                TextAlignmentOptions.Center,
                Gold,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -72f),
                new Vector2(520f, 52f));

            // Daemon card artwork frame
            var artFrame = new GameObject("ArtFrame", typeof(RectTransform), typeof(Image));
            artFrame.transform.SetParent(card.transform, false);
            var artRect = artFrame.GetComponent<RectTransform>();
            artRect.anchorMin = new Vector2(0.5f, 1f);
            artRect.anchorMax = new Vector2(0.5f, 1f);
            artRect.pivot = new Vector2(0.5f, 1f);
            artRect.anchoredPosition = new Vector2(0f, -130f);
            artRect.sizeDelta = new Vector2(148f, 148f);
            _encounterDaemonArt = artFrame.GetComponent<Image>();
            _encounterDaemonArt.color = new Color(0.6f, 0.6f, 0.6f, 1f); // fallback tint

            var fxGo = new GameObject("ArtFx", typeof(RectTransform), typeof(Image));
            fxGo.transform.SetParent(artFrame.transform, false);
            var fxRect = fxGo.GetComponent<RectTransform>();
            fxRect.anchorMin = new Vector2(0.5f, 0.5f);
            fxRect.anchorMax = new Vector2(0.5f, 0.5f);
            fxRect.pivot = new Vector2(0.5f, 0.5f);
            fxRect.anchoredPosition = Vector2.zero;
            fxRect.sizeDelta = new Vector2(194f, 194f);
            _encounterDaemonFx = fxGo.GetComponent<Image>();
            _encounterDaemonFx.preserveAspect = true;
            _encounterDaemonFx.raycastTarget = false;
            _encounterDaemonFx.color = new Color(1f, 1f, 1f, 0f);

            _encounterBody = CreateScreenLabel(
                "EncounterBody",
                card.transform,
                string.Empty,
                20,
                TextAlignmentOptions.Center,
                new Color(0.88f, 0.85f, 0.78f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -290f),
                new Vector2(560f, 104f));

            // Buttons row
            var buttonRow = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            buttonRow.transform.SetParent(card.transform, false);
            var rowRect = buttonRow.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.5f, 0f);
            rowRect.anchorMax = new Vector2(0.5f, 0f);
            rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, 34f);
            rowRect.sizeDelta = new Vector2(560f, 64f);
            var hlg = buttonRow.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(0, 0, 0, 0);

            CreateStretchButton(buttonRow.transform, "BATTLE DAEMON", Gold, StartWildDaemonBattle);
            CreateStretchButton(buttonRow.transform, "FLEE", new Color(0.28f, 0.28f, 0.34f), OnFlee);
        }

        private bool IsWildEncounterTile(Vector2Int tile)
        {
            if (_currentArea != StoryArea.Courtyard) return false;
            if (IsPathTile(tile.x, tile.y)) return false;
            if (IsWaterTile(tile.x, tile.y)) return false;
            if (_blockedTiles.Contains(tile)) return false;
            return tile.y <= 9;
        }

        private void TryTriggerWildEncounter()
        {
            if (_encounterPanel != null && _encounterPanel.activeSelf) return;
            if (!IsWildEncounterTile(_playerTile)) return;
            if (UnityEngine.Random.value > WildEncounterChance) return;

            _encounterDaemon = PickWildDaemon();
            if (_encounterDaemon == null) return;

            ShowEncounterPanel(_encounterDaemon);
        }

        private DaemonCardData PickWildDaemon()
        {
            if (_database?.daemons == null || _database.daemons.Length == 0)
                return null;

            var pool = _database.daemons.Where(d =>
                d != null && !string.IsNullOrEmpty(d.cardId) &&
                (d.element == Element.Flame || d.element == Element.Nature ||
                 d.element == Element.Water || d.element == Element.Earth)
            ).ToArray();

            if (pool.Length == 0)
                pool = _database.daemons.Where(d => d != null && !string.IsNullOrEmpty(d.cardId)).ToArray();

            if (pool.Length == 0) return null;
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        private void ShowEncounterPanel(DaemonCardData daemon)
        {
            if (_encounterPanel == null) return;

            string elementTag = daemon.element.ToString().ToUpper();
            if (_encounterTitle != null)
                _encounterTitle.text = $"Wild {daemon.cardName}";
            if (_encounterBody != null)
                _encounterBody.text = $"[{elementTag} DAEMON]\nEnter the battle GUI, summon from your Hand, spend SE on moves, then cast a Seal when its HP is low to bind it into your Grimware.\n{(string.IsNullOrEmpty(daemon.flavorText) ? "It regards you with ancient, hungry eyes." : daemon.flavorText)}";
            if (_encounterDaemonArt != null)
            {
                _encounterDaemonArt.sprite = daemon.artwork;
                _encounterDaemonArt.color = daemon.artwork != null ? Color.white : new Color(0.35f, 0.35f, 0.42f, 1f);
                _encounterDaemonArt.preserveAspect = true;
            }

            StartEncounterFxLoop(daemon);

            _encounterPanel.SetActive(true);
        }

        private void StartWildDaemonBattle()
        {
            if (_encounterDaemon == null)
            {
                HideEncounterPanel();
                return;
            }

            if (string.IsNullOrWhiteSpace(_profile?.storyStarterSavedDeckId))
            {
                HideEncounterPanel();
                StartDialogue(new[]
                {
                    new DialogueBeat
                    {
                        Speaker = "Grimware",
                        Body = "You need a bonded grimware before you can fight and bind wild daemons."
                    }
                });
                return;
            }

            DaemonCardData daemon = _encounterDaemon;
            HideEncounterPanel();

            DeckConverter.SelectedDeckId = _profile.storyStarterSavedDeckId;
            DeckConverter.SelectedAIDifficulty = AIPlayer.Difficulty.Easy;
            DeckConverter.RematchAIDifficulty = AIPlayer.Difficulty.Easy;
            DeckConverter.SelectedStoryBattle = new DeckConverter.StoryBattleConfig
            {
                enableNoviceTutorial = false,
                useAcademyTrialDeck = false,
                useDaemonEncounterRules = true,
                opponentName = $"Wild {daemon.cardName}",
                battleTitle = "Wild Daemon Encounter",
                battleSubtitle = "Defeat the wild daemon and bind it to your Grimware when you win.",
                opponentElement = daemon.element,
                opponentCreatureType = daemon.creatureType,
                storyBattleId = $"wild-{daemon.cardId}",
                isWildDaemonEncounter = true,
                wildDaemonCardId = daemon.cardId,
                bindWildDaemonOnWin = true,
                battlebackThemeId = ResolveWildEncounterBattlebackTheme(),
            };
            RuntimeAssetLocator.TryLoadScene("Battle", this);
        }

        private string ResolveWildEncounterBattlebackTheme()
        {
            if (_currentArea == StoryArea.AcademyHall)
                return "indoor1";

            if (IsWaterTile(_playerTile.x, _playerTile.y))
                return "water";

            if (IsPathTile(_playerTile.x, _playerTile.y))
                return "city";

            return "grass";
        }

        private void HideEncounterPanel()
        {
            if (_encounterFxRoutine != null)
            {
                StopCoroutine(_encounterFxRoutine);
                _encounterFxRoutine = null;
            }

            if (_encounterDaemonFx != null)
            {
                _encounterDaemonFx.sprite = null;
                _encounterDaemonFx.color = new Color(1f, 1f, 1f, 0f);
                _encounterDaemonFx.rectTransform.localScale = Vector3.one;
                _encounterDaemonFx.rectTransform.localRotation = Quaternion.identity;
            }

            _encounterPanel?.SetActive(false);
            _encounterDaemon = null;
        }

        private void OnFlee()
        {
            HideEncounterPanel();
        }

        private void StartEncounterFxLoop(DaemonCardData daemon)
        {
            if (_encounterDaemonFx == null)
                return;

            if (_encounterFxRoutine != null)
            {
                StopCoroutine(_encounterFxRoutine);
                _encounterFxRoutine = null;
            }

            BattleEffectAssetKind kind = ResolveEncounterEffectKind(daemon);
            if (!BattleEffectSpriteLibrary.TryGetFrames(kind, out Sprite[] frames) || frames.Length == 0)
            {
                _encounterDaemonFx.sprite = null;
                _encounterDaemonFx.color = new Color(1f, 1f, 1f, 0f);
                return;
            }

            _encounterFxRoutine = StartCoroutine(PlayEncounterFx(frames, ResolveEncounterFxTint(daemon)));
        }

        private IEnumerator PlayEncounterFx(Sprite[] frames, Color tint)
        {
            if (_encounterDaemonFx == null || frames == null || frames.Length == 0)
                yield break;

            RectTransform rect = _encounterDaemonFx.rectTransform;
            float elapsed = 0f;
            float frameDuration = 0.055f;

            while (_encounterPanel != null && _encounterPanel.activeSelf)
            {
                elapsed += Time.unscaledDeltaTime;
                int frameIndex = Mathf.FloorToInt(elapsed / frameDuration) % frames.Length;
                float pulse = 0.5f + Mathf.Sin(elapsed * 2.1f) * 0.5f;
                float angle = Mathf.Sin(elapsed * 1.1f) * 6f;

                _encounterDaemonFx.sprite = frames[frameIndex];
                _encounterDaemonFx.color = new Color(tint.r, tint.g, tint.b, Mathf.Lerp(0.24f, 0.54f, pulse));
                rect.localScale = Vector3.one * Mathf.Lerp(0.92f, 1.08f, pulse);
                rect.localRotation = Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }
        }

        private static BattleEffectAssetKind ResolveEncounterEffectKind(DaemonCardData daemon)
        {
            if (daemon == null)
                return BattleEffectAssetKind.Bind;

            switch (daemon.element)
            {
                case Element.Flame:
                    return BattleEffectAssetKind.Flame;
                case Element.Ice:
                    return BattleEffectAssetKind.Frost;
                case Element.Water:
                    return BattleEffectAssetKind.Water;
                case Element.Earth:
                    return BattleEffectAssetKind.Stone;
                case Element.Air:
                    return BattleEffectAssetKind.Gale;
                case Element.Nature:
                    return BattleEffectAssetKind.Verdant;
                case Element.Light:
                    return BattleEffectAssetKind.Radiant;
                case Element.Dark:
                    return BattleEffectAssetKind.Shadow;
                default:
                    return BattleEffectAssetKind.Bind;
            }
        }

        private static Color ResolveEncounterFxTint(DaemonCardData daemon)
        {
            if (daemon == null)
                return new Color(0.72f, 0.66f, 0.92f);

            switch (daemon.element)
            {
                case Element.Flame:
                    return new Color(1f, 0.66f, 0.42f);
                case Element.Ice:
                    return new Color(0.74f, 0.92f, 1f);
                case Element.Water:
                    return new Color(0.52f, 0.78f, 1f);
                case Element.Earth:
                    return new Color(0.84f, 0.70f, 0.42f);
                case Element.Air:
                    return new Color(0.86f, 0.96f, 1f);
                case Element.Nature:
                    return new Color(0.60f, 0.92f, 0.54f);
                case Element.Light:
                    return new Color(1f, 0.96f, 0.68f);
                case Element.Dark:
                    return new Color(0.72f, 0.56f, 0.94f);
                default:
                    return new Color(0.72f, 0.66f, 0.92f);
            }
        }

        private void StartInvokerBattle(StoryActor invoker)
        {
            if (string.IsNullOrWhiteSpace(_profile?.storyStarterSavedDeckId))
            {
                StartDialogue(new[]
                {
                    new DialogueBeat
                    {
                        Speaker = invoker.DisplayName,
                        Body = "Bond with a grimware first — then we settle this."
                    }
                });
                return;
            }

            DeckConverter.SelectedDeckId = _profile.storyStarterSavedDeckId;
            DeckConverter.SelectedAIDifficulty = AIPlayer.Difficulty.Normal;
            DeckConverter.RematchAIDifficulty = AIPlayer.Difficulty.Normal;
            DeckConverter.SelectedStoryBattle = new DeckConverter.StoryBattleConfig
            {
                enableNoviceTutorial = false,
                useAcademyTrialDeck = false,
                opponentDeckName = "Wanderer's Grimoire",
                opponentName = invoker.DisplayName,
                battleTitle = "Invoker Duel",
                battleSubtitle = "Prove your daemon bonds. One clean strike wins the grove.",
                opponentElement = _selectedChoice != null
                    ? ResolveInvokerCounterElement(_selectedChoice.Deck?.element ?? Element.Flame)
                    : Element.Earth,
                opponentCreatureType = CreatureType.Elemental,
                storyBattleId = invoker.InvokerId,
                storyWinChapter = 3,
                defeatedInvokerId = invoker.InvokerId,
                battlebackThemeId = "field",
            };
            RuntimeAssetLocator.TryLoadScene("Battle", this);
        }

        private static Element ResolveInvokerCounterElement(Element playerElement)
        {
            return playerElement switch
            {
                Element.Flame  => Element.Water,
                Element.Water  => Element.Nature,
                Element.Earth  => Element.Flame,
                Element.Dark   => Element.Light,
                _              => Element.Earth,
            };
        }
    }
}
