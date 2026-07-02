// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Battle Scene Controller
//  MonoBehaviour that drives the battle UI, player
//  interaction, AI opponent, and full game loop.
// ═══════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DualCraft.UI
{
    using AI;
    using Battle;
    using CardComponents;
    using Cards;
    using Core;
    using Data;
    using Story;
    using Visual;
    using Net = DualCraft.Networking;

    public partial class BattleSceneController : MonoBehaviour
    {
        [Header("Card Database")]
        [SerializeField] private CardDatabase cardDatabase;

        [Header("Deck Selections")]
        [SerializeField] private DeckData player1Deck;
        [SerializeField] private DeckData player2Deck;

        [Header("Player 1 UI")]
        [SerializeField] private Transform p1HandContainer;
        [SerializeField] private Transform p1FieldContainer;
        [SerializeField] private Transform p1PillarContainer;
        [SerializeField] private TextMeshProUGUI p1NameText;
        [SerializeField] private TextMeshProUGUI p1HpText;
        [SerializeField] private Slider p1HpBar;
        [SerializeField] private TextMeshProUGUI p1WillText;
        [SerializeField] private TextMeshProUGUI p1DeckCountText;

        [Header("Player 2 UI")]
        [SerializeField] private Transform p2HandContainer;
        [SerializeField] private Transform p2FieldContainer;
        [SerializeField] private Transform p2PillarContainer;
        [SerializeField] private TextMeshProUGUI p2NameText;
        [SerializeField] private TextMeshProUGUI p2HpText;
        [SerializeField] private Slider p2HpBar;
        [SerializeField] private TextMeshProUGUI p2WillText;
        [SerializeField] private TextMeshProUGUI p2DeckCountText;

        [Header("Controls")]
        [SerializeField] private TextMeshProUGUI phaseText;
        [SerializeField] private TextMeshProUGUI turnText;
        [SerializeField] private Button nextPhaseButton;
        [SerializeField] private Button endTurnButton;

        [Header("Prefabs")]
        [SerializeField] private GameObject cardPrefab;
        [SerializeField] private GameObject daemonFieldPrefab;
        [SerializeField] private GameObject pillarPrefab;

        [Header("Game Log")]
        [SerializeField] private Transform logContainer;
        [SerializeField] private GameObject logEntryPrefab;
        [SerializeField] private ScrollRect logScrollRect;

        [Header("Game Over")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private TextMeshProUGUI gameOverText;

        [Header("Dice")]
        [SerializeField] private DiceRoller diceRoller;

        // ─── Runtime State ───────────────────────────────
        private BattleManager _battle;
        private AIPlayer _ai;
        private Net.RelayGameHost _relayHost;
        private Net.RelayGameClient _relayClient;
        private bool _onlineBattle;
        private int _onlineActionSequence;
        private const int LocalPlayer = 0;
        private const int AIPlayerIndex = 1;
        private const float AIActionDelay = 0.95f;
        private const int MaxVisibleLogEntries = 8;
        private const bool ShowPersistentGameLog = false;
        private const bool UseDragDropHandPlay = false;
        private const bool UseSeatSelectionForDaemonPlay = false;
        private Button _forfeitButton;
        private static readonly Color HandTint = new(0.05f, 0.04f, 0.065f, 0.42f);
        private static readonly Color FieldTint = new(0.06f, 0.05f, 0.08f, 0.36f);
        private static readonly Color PillarTint = new(0.07f, 0.055f, 0.04f, 0.30f);
        private static readonly Color InvokerTint = new(0.045f, 0.04f, 0.06f, 0.36f);
        private static readonly Color DeckTint = new(0.08f, 0.06f, 0.04f, 0.26f);
        private static readonly Color VoidTint = new(0.18f, 0.08f, 0.14f, 0.46f);
        private static readonly Color ControlsTint = new(0.12f, 0.10f, 0.16f, 0.94f);
        private static readonly Color CombatCritTint = new(1f, 0.84f, 0.20f, 0.95f);
        private static readonly Color CombatWeakTint = new(0.74f, 0.78f, 0.86f, 0.9f);
        private static readonly Color CombatHitTint = new(1f, 0.36f, 0.30f, 0.92f);
        private static readonly Color CombatPillarTint = new(1f, 0.76f, 0.28f, 0.92f);
        private static readonly Vector2 LanePlateInset = new(10f, 6f);
        private const float BoardCardSlotW = 145f;
        private const float BoardCardSlotH = 180f;
        private const float EnemyBoardCardSlotH = 222f;
        private const float BoardCardInnerW = 111f;
        private const float BoardCardInnerH = 158f;
        private const float DeckPileCardW = 145f;
        private const float DeckPileCardH = 207f;
        private const float BoardCardSlotGap = 42f;
        private const float PillarPileSlotW = 108f;
        private const float PillarPileSlotH = 153f;
        private const float PillarPileInnerW = 92f;
        private const float PillarPileInnerH = 130f;
        private const float ZoneLabelFontSize = 12f;
        private const float ZoneLabelOpacity = 0.85f;
        private static Sprite _generatedBoardMat;
        private static Sprite _generatedBoardOverlay;
        private static Sprite _generatedGlowSprite;

        // Attack targeting state
        private int _selectedAttackerIndex = -1;
        private int _selectedFusionPrimaryIndex = -1;
        private bool _waitingForTarget;
        private bool _aiTurnRunning;
        private int _prevP1Hp = -1;
        private int _prevP2Hp = -1;
        private int _prevP1Will = -1;
        private int _prevP2Will = -1;

        // Card preview / field-placement state
        private int _selectedCardForPlay = -1;
        private GameObject _cardPreviewOverlay;
        private int _pendingLocalSummonSlot = -1;
        private bool _summonAnimInFlight;
        private int _pendingMaskHandIndex = -1;
        private int _pendingAsheHandIndex = -1;
        private int _lastTurnNumber = -1;
        private GamePhase _lastPhase = (GamePhase)(-1);
        private bool _refreshScheduled;
        private bool _isRefreshingUi;
        private bool _autoDrawRunning;
        private bool _suppressSERolledVisual;
        private bool _layoutRefreshQueued;
        private int _p1HandHash, _p2HandHash, _p1FieldHash, _p2FieldHash;
        private int _p1PillarHash, _p2PillarHash;
        private int _p1PrevHandCount = -1;
        private int _p2PrevHandCount = -1;
        private RectTransform _combatFxLayer;
        private RectTransform _combatTextLayer;
        private Image _combatFlashOverlay;
        private BattleBoardStage3D _boardStage3D;
        private readonly Queue<LogEntry> _effectAnnouncementQueue = new();
        private readonly Queue<LogEntry> _battleDialogueQueue = new();
        private Coroutine _effectAnnouncementRoutine;
        private GameObject _battleDialoguePanel;
        private TextMeshProUGUI _battleDialogueText;
        private TextMeshProUGUI _battleDialoguePromptText;
        private bool _battleDialogueAwaitingAdvance;
        private Transform _diceAreaContainer; // named "DiceArea" child of canvas — cached to prevent blind parent deactivation
        private readonly Dictionary<string, int> _p1FieldSlotMap = new();
        private readonly Dictionary<string, int> _p2FieldSlotMap = new();
        private readonly Dictionary<string, RectTransform> _p1DaemonCardRects = new();
        private readonly Dictionary<string, RectTransform> _p2DaemonCardRects = new();
        private readonly Dictionary<string, int> _lastDaemonAsheById = new();
        private bool _daemonAsheSnapshotReady;
        private DeckConverter.StoryBattleConfig _storyBattleConfig;
        private GameObject _storyTutorialPanel;
        private TextMeshProUGUI _storyTutorialTitleText;
        private TextMeshProUGUI _storyTutorialBodyText;
        private TextMeshProUGUI _storyTutorialFooterText;
        private Image _storyTutorialAccentImage;
        private bool _storyTutorialEnemyTurnSeen;
        private bool _storyTutorialAttackSeen;
        private GameObject _storyBattleBannerPanel;
        private TextMeshProUGUI _storyBattleBannerTitleText;
        private TextMeshProUGUI _storyBattleBannerSubtitleText;
        private Image _storyBattleBannerAccentImage;

        // Highlight colors
        private static readonly Color HighlightPlay = new(0.96f, 0.82f, 0.34f, 0.24f);
        private static readonly Color HighlightAttacker = new(1f, 0.85f, 0.2f, 0.4f);
        private static readonly Color HighlightTarget = new(1f, 0.3f, 0.3f, 0.35f);

        // ─── Deck Resolution ────────────────────────────
        private DeckData ResolvePlayerDeck(DeckData fallback)
        {
            // Try rematch first (persists across scene reloads); then fall through to a fresh selection.
            string sel = DeckConverter.SelectedDeckId;
            if (string.IsNullOrEmpty(sel))
                sel = DeckConverter.RematchDeckId; // re-use last battle deck for Rematch
            else
                DeckConverter.RematchDeckId = sel; // remember for future rematch
            DeckConverter.SelectedDeckId = null; // consume once

            if (!string.IsNullOrEmpty(sel))
            {
                if (sel.StartsWith("prebuilt:"))
                {
                    string deckName = sel.Substring("prebuilt:".Length);
                    var decks = Resources.LoadAll<DeckData>("CardData/Decks");
                    foreach (var d in decks)
                        if (d != null && d.deckName == deckName) return d;
                }
                else
                {
                    var profile = ProfileManager.Load();
                    var saved = profile.customDecks.Find(d => d.id == sel);
                    if (saved != null && cardDatabase != null)
                    {
                        var converted = DeckConverter.ToRuntimeDeck(saved, cardDatabase);
                        if (converted != null && converted.IsValid) return converted;
                    }
                }
            }

            return RuntimeAssetLocator.LoadDefaultDeck(fallback, this);
        }

        private DeckData ResolveOpponentDeck(DeckData fallback, DeckData resolvedPlayerDeck)
        {
            DeckData storyDeck = StoryBattleDeckFactory.CreateOpponentDeck(cardDatabase, _storyBattleConfig, resolvedPlayerDeck);
            if (storyDeck != null)
                return storyDeck;

            if (_storyBattleConfig != null && !string.IsNullOrWhiteSpace(_storyBattleConfig.opponentDeckName))
            {
                DeckData configured = Resources.LoadAll<DeckData>("CardData/Decks")
                    .FirstOrDefault(deck => deck != null && deck.deckName == _storyBattleConfig.opponentDeckName);
                if (configured != null)
                    return configured;
            }

            return RuntimeAssetLocator.LoadAlternateDeck(fallback, resolvedPlayerDeck, this);
        }

        private AIPlayer.Difficulty ResolveAIDifficulty()
        {
            AIPlayer.Difficulty? selected = DeckConverter.SelectedAIDifficulty;
            if (selected.HasValue)
            {
                DeckConverter.RematchAIDifficulty = selected;
                DeckConverter.SelectedAIDifficulty = null;
                return selected.Value;
            }

            if (DeckConverter.RematchAIDifficulty.HasValue)
                return DeckConverter.RematchAIDifficulty.Value;

            var profile = ProfileManager.Load();
            return profile != null && Enum.TryParse(profile.preferredAIDifficulty, true, out AIPlayer.Difficulty saved)
                ? saved
                : AIPlayer.Difficulty.Normal;
        }

        private static string GetAIDifficultyLabel(AIPlayer.Difficulty difficulty)
        {
            return difficulty switch
            {
                AIPlayer.Difficulty.Easy => "Easy",
                AIPlayer.Difficulty.Hard => "Hard",
                _ => "Normal",
            };
        }

        private DeckData CreateBattleDeckVariant(DeckData source)
        {
            if (source == null)
                return null;

            List<CardData> flatMain = ExpandDeckEntries(source.cards);
            if (flatMain.Count != GameConstants.DeckSize)
                return source;

            int existingAsheCount = flatMain.Count(card => card is AsheCardData);
            int templateAsheCount = GetTemplateAsheIds(source).Count;
            int baselineAsheCount = Mathf.Clamp(Mathf.RoundToInt(GameConstants.DeckSize * 0.225f), 8, 10);
            int targetAsheCount = Mathf.Clamp(templateAsheCount > 0 ? templateAsheCount : baselineAsheCount, 8, 10);
            if (existingAsheCount >= targetAsheCount)
                return source;

            List<AsheCardData> injectedAshe = BuildRuntimeAshePackage(source, targetAsheCount - existingAsheCount);
            if (injectedAshe.Count == 0)
                return source;

            List<int> cutIndices = ChooseDeckCuts(flatMain, injectedAshe.Count);
            if (cutIndices.Count < injectedAshe.Count)
                return source;

            foreach (int index in cutIndices.OrderByDescending(i => i))
                flatMain.RemoveAt(index);

            flatMain.AddRange(injectedAshe);
            DeckData clone = ScriptableObject.CreateInstance<DeckData>();
            clone.name = $"{source.name}_BattleVariant";
            clone.deckName = source.deckName;
            clone.element = source.element;
            clone.primaryCreatureType = source.primaryCreatureType;
            clone.description = source.description;
            clone.isStarter = source.isStarter;
            clone.battleBoardMat = source.battleBoardMat;
            clone.battleBoardOverlay = source.battleBoardOverlay;
            clone.daemonSeatArt = source.daemonSeatArt;
            clone.pillarSeatArt = source.pillarSeatArt;
            clone.cardBackOverride = source.cardBackOverride;
            clone.battleMusicOverride = source.battleMusicOverride;
            clone.cards = GroupDeckEntries(flatMain);
            clone.pillars = source.pillars != null
                ? source.pillars.Select(entry => new DeckEntry { card = entry.card, count = entry.count }).ToArray()
                : Array.Empty<DeckEntry>();

            Debug.Log($"[Battle] Injected {injectedAshe.Count} Ashe support cards into {source.deckName} for stronger SE pacing and resource consistency.");
            return clone;
        }

        private List<string> GetTemplateAsheIds(DeckData source)
        {
            if (source == null)
                return new List<string>();

            try
            {
                if (!CardLoader.Instance.IsLoaded)
                    CardLoader.Instance.LoadFromResources();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Battle] Could not load JSON card data for Ashe support lookup: {ex.Message}");
                return new List<string>();
            }

            DeckTemplate template = CardLoader.Instance.GetDeck(source.deckName);
            if (template?.CardIds == null || template.CardIds.Length == 0)
                return new List<string>();

            return template.CardIds
                .Where(id => CardLoader.Instance.GetCard(id)?.Category == CardCategory.AsheCard)
                .ToList();
        }

        private List<AsheCardData> BuildRuntimeAshePackage(DeckData source, int requestedCount)
        {
            var package = new List<AsheCardData>();
            if (source == null || requestedCount <= 0)
                return package;

            List<AsheCardData> preferredTemplates = LoadPreferredAsheTemplates(source)
                .Select(template => CloneBattleAsheTemplate(template, source))
                .Where(template => template != null)
                .ToList();

            if (preferredTemplates.Count > 0)
            {
                for (int i = 0; i < requestedCount; i++)
                    package.Add(preferredTemplates[i % preferredTemplates.Count]);
                return package;
            }

            var cache = new Dictionary<string, AsheCardData>();
            List<string> desiredIds = GetTemplateAsheIds(source);
            if (desiredIds.Count == 0)
            {
                desiredIds = CardLoader.Instance.GetAllCards()
                    .Where(card => card.Category == CardCategory.AsheCard)
                    .OrderBy(card =>
                    {
                        bool elementMatch = card.RequiredElement == source.element;
                        bool creatureMatch = card.RequiredCreatureType == source.primaryCreatureType;
                        return elementMatch && creatureMatch ? 0 : elementMatch || creatureMatch ? 1 : 2;
                    })
                    .ThenBy(card => card.WillCost)
                    .Select(card => card.Id)
                    .ToList();
            }

            if (desiredIds.Count == 0)
                return package;

            int safety = Mathf.Max(requestedCount * 3, desiredIds.Count);
            for (int i = 0; i < safety && package.Count < requestedCount; i++)
            {
                string id = desiredIds[i % desiredIds.Count];
                var runtime = CardLoader.Instance.GetCard(id);
                if (runtime == null || runtime.Category != CardCategory.AsheCard)
                    continue;

                if (!cache.TryGetValue(id, out AsheCardData asheCard))
                {
                    asheCard = CreateRuntimeAsheCard(runtime);
                    if (asheCard == null)
                        continue;
                    cache[id] = asheCard;
                }

                package.Add(asheCard);
            }

            return package;
        }

        private List<AsheCardData> LoadPreferredAsheTemplates(DeckData source)
        {
            if (source == null)
                return new List<AsheCardData>();

            AsheCardData[] templates = Resources.LoadAll<AsheCardData>("CardData/Cards");
            if (templates == null || templates.Length == 0)
                return new List<AsheCardData>();

            if (source.primaryCreatureType == CreatureType.Elemental)
            {
                AsheCardData matched = templates.FirstOrDefault(card =>
                    card != null
                    && !string.IsNullOrEmpty(card.cardId)
                    && card.cardId.StartsWith("ae-", StringComparison.OrdinalIgnoreCase)
                    && card.targetElement == source.element);

                if (matched != null)
                    return new List<AsheCardData> { matched };

                AsheCardData fallbackElemental = templates.FirstOrDefault(card =>
                    card != null
                    && !string.IsNullOrEmpty(card.cardId)
                    && card.cardId.StartsWith("ae-", StringComparison.OrdinalIgnoreCase));
                return fallbackElemental != null ? new List<AsheCardData> { fallbackElemental } : new List<AsheCardData>();
            }

            string preferredId = source.primaryCreatureType switch
            {
                CreatureType.Machine => "ac-machine",
                CreatureType.Artificial => "ac-artificial",
                CreatureType.Spirit => "ac-spirit",
                CreatureType.Undead => "ac-undead",
                _ => string.Empty,
            };

            AsheCardData directMatch = templates.FirstOrDefault(card =>
                card != null
                && string.Equals(card.cardId, preferredId, StringComparison.OrdinalIgnoreCase));

            return directMatch != null ? new List<AsheCardData> { directMatch } : new List<AsheCardData>();
        }

        private AsheCardData CloneBattleAsheTemplate(AsheCardData template, DeckData source)
        {
            if (template == null || source == null)
                return null;

            AsheCardData clone = Instantiate(template);
            clone.name = $"{template.name}_{source.deckName}_Battle";
            clone.hideFlags = HideFlags.DontSave;
            clone.matchType = source.primaryCreatureType == CreatureType.Elemental
                ? AsheMatchType.Element
                : AsheMatchType.CreatureType;
            clone.targetCreatureType = source.primaryCreatureType;

            if (source.primaryCreatureType == CreatureType.Elemental)
            {
                clone.targetElement = source.element;
                clone.description = $"Bound Elemental attacks cost 0 SE while {clone.cardName} is attached.";
            }
            else if (source.primaryCreatureType == CreatureType.Machine)
            {
                clone.description = $"Flux Siphon steals {clone.sePerTurn} SE for each enemy daemon in play and adds it to your reserve each turn.";
            }
            else if (source.primaryCreatureType == CreatureType.Artificial)
            {
                clone.description = $"Force Field absorbs {Mathf.Max(1, clone.shieldAmount)} damage for the bound Artificial daemon.";
            }
            else if (source.primaryCreatureType == CreatureType.Spirit)
            {
                clone.description = $"Ascendant Bond grants +{clone.buffAttack} ATK and +{clone.buffAshe} Life for {Mathf.Max(1, clone.buffTurns)} turns.";
            }
            else if (source.primaryCreatureType == CreatureType.Undead)
            {
                clone.description = $"Last Rite revives the bound Undead once with {Mathf.Max(1, clone.resurrectHp)} Life, then falls into the Void.";
            }

            if (clone.artwork == null)
            {
                Element artElement = source.primaryCreatureType == CreatureType.Elemental
                    ? source.element
                    : source.primaryCreatureType switch
                    {
                        CreatureType.Machine => Element.Air,
                        CreatureType.Artificial => Element.Light,
                        CreatureType.Spirit => Element.Nature,
                        CreatureType.Undead => Element.Dark,
                        _ => source.element,
                    };
                clone.artwork = CardTextureGenerator.GenerateCardArt(artElement, CardCategory.AsheCard, clone.rarity, clone.cardId);
                clone.fullArt = clone.artwork;
            }

            return clone;
        }

        private AsheCardData CreateRuntimeAsheCard(RuntimeCard runtime)
        {
            if (runtime == null || runtime.Category != CardCategory.AsheCard)
                return null;

            var card = ScriptableObject.CreateInstance<AsheCardData>();
            card.name = runtime.Name.Replace(" ", string.Empty);
            card.cardId = runtime.Id;
            card.cardName = runtime.Name;
            card.category = CardCategory.AsheCard;
            card.rarity = runtime.Rarity;
            card.description = runtime.Description;
            card.flavorText = runtime.FlavorText;
            card.willCost = runtime.WillCost;
            card.artwork = Resources.Load<Sprite>(runtime.ArtPath);
            card.fullArt = card.artwork;
            card.sePerTurn = Mathf.Clamp(runtime.AshePerTurn, 1, 3);
            card.matchType = AsheMatchType.CreatureType;
            card.targetElement = runtime.RequiredElement;
            card.targetCreatureType = runtime.RequiredCreatureType;
            if (card.artwork == null)
            {
                Element artElement = card.targetCreatureType switch
                {
                    CreatureType.Machine => Element.Air,
                    CreatureType.Artificial => Element.Light,
                    CreatureType.Spirit => Element.Nature,
                    CreatureType.Undead => Element.Dark,
                    _ => runtime.RequiredElement,
                };
                card.artwork = CardTextureGenerator.GenerateCardArt(artElement, CardCategory.AsheCard, card.rarity, card.cardId);
                card.fullArt = card.artwork;
            }
            card.hideFlags = HideFlags.DontSave;
            return card;
        }

        private static List<CardData> ExpandDeckEntries(DeckEntry[] entries)
        {
            var cards = new List<CardData>();
            if (entries == null)
                return cards;

            foreach (var entry in entries)
            {
                if (entry?.card == null || entry.count <= 0)
                    continue;

                for (int i = 0; i < entry.count; i++)
                    cards.Add(entry.card);
            }

            return cards;
        }

        private static DeckEntry[] GroupDeckEntries(IEnumerable<CardData> cards)
        {
            if (cards == null)
                return Array.Empty<DeckEntry>();

            return cards
                .Where(card => card != null)
                .GroupBy(card => card)
                .Select(group => new DeckEntry { card = group.Key, count = group.Count() })
                .ToArray();
        }

        private static List<int> ChooseDeckCuts(IReadOnlyList<CardData> cards, int neededCuts)
        {
            if (cards == null || neededCuts <= 0)
                return new List<int>();

            var duplicateCounts = cards
                .Where(card => card != null)
                .GroupBy(card => card.cardId)
                .ToDictionary(group => group.Key, group => group.Count());

            return cards
                .Select((card, index) => new { card, index })
                .Where(x => x.card != null
                    && x.card.category != CardCategory.Domain
                    && x.card.category != CardCategory.Seal
                    && x.card.category != CardCategory.AsheCard)
                .OrderByDescending(x => duplicateCounts.GetValueOrDefault(x.card.cardId, 1))
                .ThenBy(x => x.card.category == CardCategory.Daemon ? 0 : 1)
                .ThenByDescending(x => x.index)
                .Select(x => x.index)
                .Distinct()
                .Take(neededCuts)
                .ToList();
        }

        // ─── Lifecycle ───────────────────────────────────
        private void Start()
        {
            Debug.Log("[Battle] Start begin");
            cardDatabase = RuntimeAssetLocator.LoadCardDatabase(cardDatabase, this);
            if (cardDatabase == null)
                return;

            _storyBattleConfig = DeckConverter.ConsumeStoryBattleConfig();
            if (!string.IsNullOrWhiteSpace(_storyBattleConfig?.battlebackThemeId))
                BattlePresentationRuntime.SetStoryBattleBattlebackTheme(_storyBattleConfig.battlebackThemeId);
            else
                BattlePresentationRuntime.ClearStoryBattleBattlebackTheme();
            DeckData resolvedPlayer1Deck = ResolvePlayerDeck(player1Deck);
            DeckData resolvedPlayer2Deck = ResolveOpponentDeck(player2Deck, resolvedPlayer1Deck);
            player1Deck = CreateBattleDeckVariant(resolvedPlayer1Deck);
            player2Deck = CreateBattleDeckVariant(resolvedPlayer2Deck);
            cardPrefab = RuntimeAssetLocator.LoadCardPrefab(cardPrefab, this);
            daemonFieldPrefab = RuntimeAssetLocator.LoadCardPrefab(daemonFieldPrefab ?? cardPrefab, this);
            pillarPrefab = RuntimeAssetLocator.LoadCardPrefab(pillarPrefab ?? cardPrefab, this);
            if (player1Deck == null || player2Deck == null || cardPrefab == null)
                return;

            BattlePresentationRuntime.SetActiveBattleDecks(player1Deck, player2Deck);
            Debug.Log("[Battle] ConfigureBattlefieldPresentation begin");
            ConfigureBattlefieldPresentation();
            Debug.Log("[Battle] ConfigureBattlefieldPresentation end");
            cardDatabase.Initialize();
            Debug.Log("[Battle] Card database initialized");

            if (TryStartRelayBattle())
            {
                Debug.Log("[Battle] Relay battle initialized");
                return;
            }

            if (_storyBattleConfig?.useDaemonEncounterRules == true)
            {
                _storyEncounterMode = true;
                endTurnButton?.onClick.AddListener(OnEndTurnClicked);
                CreateForfeitButton();
                Debug.Log("[Battle] Story encounter forfeit button created");

                if (gameOverPanel) gameOverPanel.SetActive(false);

                Audio.MusicManager.EnsureInstance().PlayBattleMusic();
                Audio.SfxManager.EnsureInstance();
                StartCoroutine(BeginStoryEncounterSequence());
                Debug.Log("[Battle] Story encounter sequence scheduled");
                return;
            }

            AIPlayer.Difficulty aiDifficulty = ResolveAIDifficulty();
            _battle = new BattleManager(cardDatabase);
            BindBattleEvents();

            _ai = new AIPlayer(AIPlayerIndex, aiDifficulty);

            endTurnButton?.onClick.AddListener(OnEndTurnClicked);
            CreateForfeitButton();
            Debug.Log("[Battle] Forfeit button created");

            if (gameOverPanel) gameOverPanel.SetActive(false);

            string playerName = ProfileManager.Load()?.playerName;
            if (string.IsNullOrWhiteSpace(playerName))
                playerName = "Player 1";
            string aiName = !string.IsNullOrWhiteSpace(_storyBattleConfig?.opponentName)
                ? _storyBattleConfig.opponentName
                : $"AI ({GetAIDifficultyLabel(aiDifficulty)})";
            _battle.InitGame(playerName, player1Deck, aiName, player2Deck);
            Debug.Log("[Battle] InitGame complete");

            // Start battle music and initialize SFX.
            Audio.MusicManager.EnsureInstance().PlayBattleMusic();
            Audio.SfxManager.EnsureInstance();
            Debug.Log("[Battle] Music started");

            // Kick off the opening sequence
            StartCoroutine(OpeningSequence());
            Debug.Log("[Battle] OpeningSequence scheduled");
        }

        private void ConfigureBattlefieldPresentation()
        {
            HideLegacyHudElements();
            RefreshBattlefieldLayout(transform as RectTransform);
            Sync3DBoardStageUsage();
            EnsureHybridDiagnosticsOverlay();
            EnsureCombatFxLayer(transform as RectTransform);
            EnsureBattleDialogueBox(transform as RectTransform);
            EnsureStoryBattleIntroBanner(transform as RectTransform);
            EnsureStoryTutorialOverlay(transform as RectTransform);
            HidePersistentBattleLog();
            StyleActionButton(endTurnButton, "END TURN");

            StyleHudText(p1NameText, 30, FontStyles.Bold);
            StyleHudText(p2NameText, 30, FontStyles.Bold);
            StyleHudText(p1HpText, 24, FontStyles.Bold);
            StyleHudText(p2HpText, 24, FontStyles.Bold);
            StyleHudText(p1WillText, 21, FontStyles.Bold);
            StyleHudText(p2WillText, 21, FontStyles.Bold);
            StyleHudText(p1DeckCountText, 18, FontStyles.Normal);
            StyleHudText(p2DeckCountText, 18, FontStyles.Normal);

            StyleSlider(p1HpBar, new Color(0.24f, 0.85f, 0.42f), new Color(0.14f, 0.10f, 0.12f, 0.95f));
            StyleSlider(p2HpBar, new Color(0.24f, 0.85f, 0.42f), new Color(0.14f, 0.10f, 0.12f, 0.95f));

            if (gameOverPanel != null)
            {
                var panelImage = gameOverPanel.GetComponent<Image>();
                if (panelImage != null)
                    panelImage.color = new Color(0.05f, 0.04f, 0.08f, 0.95f);
            }
            StyleTextBlock(gameOverText, 34, FontStyles.Bold, new Color(0.98f, 0.94f, 0.88f));
        }

        private void Update()
        {
            if (!_battleDialogueAwaitingAdvance)
                return;

            if (Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.JoystickButton0))
            {
                AdvanceBattleDialogue();
            }
        }

        private void EnsureHybridDiagnosticsOverlay()
        {
            var overlay = GetComponent<BattleHybridDiagnosticsOverlay>();
            bool shouldShow = Application.isEditor || HasLaunchArg("-battle-diagnostics", "--battle-diagnostics", "-diag-ui");

            if (!shouldShow)
            {
                if (overlay != null)
                    Destroy(overlay);
                return;
            }

            if (overlay == null)
                gameObject.AddComponent<BattleHybridDiagnosticsOverlay>();
        }

        private static bool HasLaunchArg(params string[] names)
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args == null || names == null)
                return false;

            foreach (string arg in args)
            {
                foreach (string name in names)
                {
                    if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        public void ForceHybridUiRefresh()
        {
            if (_battle?.State == null)
                return;

            RefreshUI(_battle.State);
        }

        private bool TryStartRelayBattle()
        {
            _relayHost = FindFirstObjectByType<Net.RelayGameHost>();
            _relayClient = FindFirstObjectByType<Net.RelayGameClient>();

            if (_relayHost != null && _relayHost.Room?.Started == true && _relayHost.Room.Core?.Engine != null)
            {
                _onlineBattle = true;
                _onlineActionSequence = 0;
                _battle = _relayHost.Room.Core.Engine;
                BindBattleEvents();
                _relayHost.OnLocalMessage += HandleRelayHostMessage;
                endTurnButton?.onClick.AddListener(OnEndTurnClicked);
                CreateForfeitButton();
                if (gameOverPanel) gameOverPanel.SetActive(false);
                Audio.MusicManager.EnsureInstance().PlayBattleMusic();
                Audio.SfxManager.EnsureInstance();
                RefreshUI(_battle.State);
                AddLogEntry(new LogEntry { Message = "Friend match connected as host.", Type = LogEntryType.System });
                return true;
            }

            if (_relayClient != null && _relayClient.LatestSnapshot?.State != null)
            {
                _onlineBattle = true;
                _onlineActionSequence = 0;
                _battle = new BattleManager(cardDatabase);
                BindBattleEvents();
                _relayClient.OnGameStateReceived += HandleRelayClientSnapshot;
                _relayClient.OnActionConfirmed += HandleRelayClientActionConfirmed;
                _relayClient.OnActionRejected += HandleRelayClientActionRejected;
                _relayClient.OnGameOverReceived += HandleRelayClientGameOver;
                _relayClient.OnHostDisconnected += HandleRelayDisconnect;
                endTurnButton?.onClick.AddListener(OnEndTurnClicked);
                CreateForfeitButton();
                if (gameOverPanel) gameOverPanel.SetActive(false);
                Audio.MusicManager.EnsureInstance().PlayBattleMusic();
                Audio.SfxManager.EnsureInstance();
                ApplyRelaySnapshot(_relayClient.LatestSnapshot);
                AddLogEntry(new LogEntry { Message = "Friend match connected as guest.", Type = LogEntryType.System });
                return true;
            }

            return false;
        }

        private void BindBattleEvents()
        {
            _battle.OnStateChanged += OnStateChanged;
            _battle.OnLogEntry += AddLogEntry;
            _battle.OnGameOver += HandleGameOver;
            _battle.OnCombatResolved += HandleCombatResolved;
            _battle.OnSERolled += HandleSERolled;
        }

        private void HandleRelayHostMessage(Net.NetEnvelope envelope)
        {
            if (envelope == null) return;

            switch (envelope.Type)
            {
                case nameof(Net.ActionRejected):
                    var rejected = Net.JsonUtility.FromJson<Net.ActionRejected>(envelope.Payload);
                    AddLogEntry(new LogEntry { Message = $"Action rejected: {rejected.Reason}", Type = LogEntryType.System });
                    break;
                case nameof(Net.GameOver):
                    var over = Net.JsonUtility.FromJson<Net.GameOver>(envelope.Payload);
                    HandleGameOver(over.WinnerIndex, over.WinReason);
                    break;
            }
        }

        private void HandleRelayClientSnapshot(Net.GameStateSnapshot snapshot)
        {
            ApplyRelaySnapshot(snapshot);
        }

        private void HandleRelayClientActionConfirmed(Net.ActionConfirmed confirmed)
        {
            if (confirmed?.State == null) return;
            ApplyRelayState(confirmed.State, 1);
        }

        private void HandleRelayClientActionRejected(Net.ActionRejected rejected)
        {
            AddLogEntry(new LogEntry { Message = $"Action rejected: {rejected.Reason}", Type = LogEntryType.System });
        }

        private void HandleRelayClientGameOver(Net.GameOver gameOver)
        {
            if (gameOver?.FinalState != null)
                ApplyRelayState(gameOver.FinalState, 1);
            HandleGameOver(gameOver.WinnerIndex == 1 ? 0 : 1, gameOver.WinReason);
        }

        private void HandleRelayDisconnect(Net.OpponentDisconnected disconnected)
        {
            string msg = disconnected?.Message ?? "Opponent disconnected.";
            AddLogEntry(new LogEntry { Message = msg, Type = LogEntryType.System });
        }

        private void ApplyRelaySnapshot(Net.GameStateSnapshot snapshot)
        {
            if (snapshot?.State == null) return;
            ApplyRelayState(snapshot.State, snapshot.YourPlayerIndex);
        }

        private void ApplyRelayState(Net.SerializableGameState networkState, int localSeat)
        {
            var projected = Net.NetworkStateProjector.ToLocalGameState(networkState, localSeat, cardDatabase);
            if (projected == null) return;
            _battle.SetExternalState(projected);
            RefreshUI(projected);
        }

        private bool SubmitPlayerAction(GameAction action)
        {
            if (action == null || _battle == null) return false;

            if (!_onlineBattle)
                return _battle.ProcessAction(LocalPlayer, action);

            _onlineActionSequence++;
            if (_relayHost != null)
            {
                _relayHost.SubmitHostAction(action, _onlineActionSequence);
                return true;
            }

            if (_relayClient != null)
            {
                _relayClient.SubmitAction(action);
                return true;
            }

            return false;
        }

        public void ForceHybridLayoutRefresh()
        {
            RefreshBattlefieldLayout(transform as RectTransform);
            ReapplyAllHandCurves();
        }

        public void ForceHybridHandRefan()
        {
            ReapplyAllHandCurves();
        }

        public void LogHybridDiagnosticsSnapshot()
        {
            Debug.Log(GetHybridDiagnosticsSnapshot());
        }

        private void LogRenderDiagnostics()
        {
            var canvas = GetComponentInParent<Canvas>();
            var root = transform as RectTransform;
            var camera = Camera.main;
            Debug.Log($"[BattleRender] controllerActive={gameObject.activeInHierarchy} enabled={enabled} root={root?.name ?? "null"} rootChildren={root?.childCount.ToString() ?? "-"} canvas={canvas?.name ?? "null"} canvasActive={canvas?.gameObject.activeInHierarchy.ToString() ?? "-"} canvasEnabled={canvas?.enabled.ToString() ?? "-"} renderMode={canvas?.renderMode.ToString() ?? "-"} camera={camera?.name ?? "null"} cameraActive={camera?.gameObject.activeInHierarchy.ToString() ?? "-"} cameraClear={camera?.clearFlags.ToString() ?? "-"}");
            Debug.Log($"[BattleRender] p1Hand={DescribeTransform(p1HandContainer)} p1Field={DescribeTransform(p1FieldContainer)} p1Pillar={DescribeTransform(p1PillarContainer)} p2Hand={DescribeTransform(p2HandContainer)} p2Field={DescribeTransform(p2FieldContainer)} p2Pillar={DescribeTransform(p2PillarContainer)}");
        }

        private static string DescribeTransform(Transform target)
        {
            if (target == null)
                return "null";

            return $"{target.name}:active={target.gameObject.activeInHierarchy},children={target.childCount}";
        }

        public string GetHybridDiagnosticsSnapshot()
        {
            var report = new StringBuilder(1024);
            report.AppendLine("=== Battle Hybrid Diagnostics ===");
            report.AppendLine($"phase={_battle?.State?.Phase.ToString() ?? "none"} turn={_battle?.State?.TurnNumber.ToString() ?? "-"} waitingForTarget={_waitingForTarget} selectedAttacker={_selectedAttackerIndex} selectedCardForPlay={_selectedCardForPlay}");
            report.AppendLine($"hashes p1(hand/field/pillar)={_p1HandHash}/{_p1FieldHash}/{_p1PillarHash}");
            report.AppendLine($"hashes p2(hand/field/pillar)={_p2HandHash}/{_p2FieldHash}/{_p2PillarHash}");
            AppendContainerDiagnostics(report, "P1 Hand", p1HandContainer, false, 10);
            AppendContainerDiagnostics(report, "P2 Hand", p2HandContainer, false, 10);
            AppendContainerDiagnostics(report, "P1 Field", p1FieldContainer, true, GameConstants.MaxFieldDaemons);
            AppendContainerDiagnostics(report, "P2 Field", p2FieldContainer, true, GameConstants.MaxFieldDaemons);
            AppendContainerDiagnostics(report, "P1 Pillars", p1PillarContainer, true, GameConstants.PillarCount);
            AppendContainerDiagnostics(report, "P2 Pillars", p2PillarContainer, true, GameConstants.PillarCount);
            return report.ToString();
        }

        private void ReapplyAllHandCurves()
        {
            ArrangeHandCurve(p1HandContainer, p1HandContainer != null ? p1HandContainer.childCount : 0, false);
            ArrangeHandCurve(p2HandContainer, p2HandContainer != null ? p2HandContainer.childCount : 0, true);
        }

        private void AppendContainerDiagnostics(StringBuilder report, string label, Transform container, bool seatContainer, int maxEntries)
        {
            if (report == null)
                return;

            if (container == null)
            {
                report.AppendLine($"{label}: <missing>");
                return;
            }

            var rect = container as RectTransform;
            report.AppendLine($"{label}: children={container.childCount} size={FormatVector(rect != null ? rect.rect.size : Vector2.zero)}");

            int entryCount = Mathf.Min(container.childCount, maxEntries);
            for (int i = 0; i < entryCount; i++)
            {
                Transform child = container.GetChild(i);
                RectTransform childRect = child as RectTransform;
                string baseLine = $"  [{i}] {child.name} pos={FormatVector(childRect != null ? childRect.anchoredPosition : Vector2.zero)} rotZ={FormatFloat(childRect != null ? childRect.localEulerAngles.z : 0f)}";
                if (!seatContainer)
                {
                    report.AppendLine(baseLine);
                    continue;
                }

                Transform slotCard = FindSlotCard(child);
                Transform slotContent = FindSlotContent(child);
                string slotState = slotCard != null
                    ? $" occupied={slotCard.name} cardPos={FormatVector(slotCard is RectTransform slotCardRect ? slotCardRect.anchoredPosition : Vector2.zero)}"
                    : $" occupied=<empty> content={(slotContent != null ? slotContent.name : "missing")}";
                report.AppendLine(baseLine + slotState);
            }
        }

        private static string FormatVector(Vector2 value)
        {
            return $"({FormatFloat(value.x)},{FormatFloat(value.y)})";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.0");
        }

        private bool ShouldUseBoardStage3D()
        {
            // The imported Meshy board is useful as a source asset, but it does
            // not read like the Arena-style mat the user wants when dropped in raw.
            // Keep the battle on a clean 2D board until we have a purpose-built stage.
            return false;
        }

        private void Ensure3DBoardStage()
        {
            if (!ShouldUseBoardStage3D() || !BattleBoardStage3D.HasBoardPrefab())
                return;

            _boardStage3D ??= GetComponent<BattleBoardStage3D>() ?? gameObject.AddComponent<BattleBoardStage3D>();
            _boardStage3D.Setup(Camera.main);
        }

        private void Sync3DBoardStageUsage()
        {
            if (ShouldUseBoardStage3D())
            {
                Ensure3DBoardStage();
                return;
            }

            _boardStage3D ??= GetComponent<BattleBoardStage3D>();
            if (_boardStage3D != null)
                _boardStage3D.Teardown();

            var strayStage = GameObject.Find("BattleBoardStage3D");
            if (strayStage != null)
            {
                if (Application.isPlaying)
                    Destroy(strayStage);
                else
                    DestroyImmediate(strayStage);
            }
        }

        private void HideLegacyHudElements()
        {
            SetGraphicActive(phaseText, false);
            SetGraphicActive(turnText, false);
            SetGraphicActive(p1NameText, false);
            SetGraphicActive(p2NameText, false);
            SetGraphicActive(p1HpText, false);
            SetGraphicActive(p2HpText, false);
            SetGraphicActive(p1WillText, false);
            SetGraphicActive(p2WillText, false);
            SetGraphicActive(p1DeckCountText, false);
            SetGraphicActive(p2DeckCountText, false);
            SetGraphicActive(p1HpBar, false);
            SetGraphicActive(p2HpBar, false);
            SetGraphicActive(nextPhaseButton, false);
        }

        private static void SetGraphicActive(Component component, bool active)
        {
            if (component != null && component.gameObject.activeSelf != active)
                component.gameObject.SetActive(active);
        }

        private void HidePersistentBattleLog()
        {
            if (logScrollRect != null && logScrollRect.gameObject.activeSelf)
                logScrollRect.gameObject.SetActive(false);

            if (logContainer != null && logContainer.gameObject.activeSelf)
                logContainer.gameObject.SetActive(false);
        }

        private void RefreshBattlefieldLayout(RectTransform canvasRoot)
        {
            if (canvasRoot == null)
                return;

            Debug.Log("[Battle] RefreshBattlefieldLayout begin");

            ForceBattleCanvasVisible(canvasRoot);
            BattlefieldLayoutMetrics metrics = GetBattlefieldLayoutMetrics();
            ConfigureBoardAtmosphere(canvasRoot, metrics);
            SuppressLegacyBattlefieldChrome(canvasRoot);
            NeutralizeFullscreenBlackOverlays(canvasRoot);
            EnsureBattlefieldRuntimeRoots(canvasRoot);
            ApplyResponsiveBattlefieldLayout(metrics);

            int handPad = Mathf.RoundToInt(Mathf.Lerp(8f, 14f, metrics.WideT));
            int fieldPad = Mathf.RoundToInt(Mathf.Lerp(8f, 12f, metrics.WideT));
            int pillarPad = Mathf.RoundToInt(Mathf.Lerp(8f, 12f, metrics.WideT));
            float handSpacing = Mathf.Lerp(-64f, -74f, metrics.WideT);
            float fieldSpacing = Mathf.Lerp(14f, 18f, metrics.WideT);
            float pillarSpacing = Mathf.Lerp(12f, 15f, metrics.WideT);

            ConfigureBoardLane(p1HandContainer, "Player Hand", string.Empty, HandTint, new RectOffset(handPad, handPad, 0, 0), handSpacing);
            ConfigureBoardLane(p2HandContainer, "Opponent Hand", string.Empty, HandTint, new RectOffset(handPad, handPad, 0, 0), handSpacing);
            // Disable HLG on hand containers — bezier curve handles card positioning
            DisableHLG(p1HandContainer);
            DisableHLG(p2HandContainer);
            ConfigureBoardLane(p1FieldContainer, "Player Field", string.Empty, FieldTint, new RectOffset(fieldPad, fieldPad, 6, 6), fieldSpacing);
            ConfigureBoardLane(p2FieldContainer, "Opponent Field", string.Empty, FieldTint, new RectOffset(fieldPad, fieldPad, 6, 6), fieldSpacing);
            ConfigureBoardLane(p1PillarContainer, "Player Pillars", string.Empty, PillarTint, new RectOffset(pillarPad, pillarPad, 2, 2), pillarSpacing);
            ConfigureBoardLane(p2PillarContainer, "Opponent Pillars", string.Empty, PillarTint, new RectOffset(pillarPad, pillarPad, 2, 2), pillarSpacing);
            DisableHLG(p1FieldContainer);
            DisableHLG(p2FieldContainer);
            DisableHLG(p1PillarContainer);
            DisableHLG(p2PillarContainer);

#pragma warning disable CS0162
            if (UseDragDropHandPlay)
            {
                EnsureDropZone(p1FieldContainer, DropZone.DropZoneKind.Battlefield);
                EnsureDropZone(p1HandContainer, DropZone.DropZoneKind.HandReturn);
            }
            else
            {
                DisableDropZone(p1FieldContainer);
                DisableDropZone(p1HandContainer);
            }
#pragma warning restore CS0162

            EnsureZonePlate("P1InvokerPlate", FindRootRect("P1InvokerZone"), string.Empty, Color.clear, Vector2.zero, Vector2.one, new Vector2(8f, 8f));
            EnsureZonePlate("P2InvokerPlate", FindRootRect("P2InvokerZone"), string.Empty, Color.clear, Vector2.zero, Vector2.one, new Vector2(8f, 8f));

            StyleInvokerStrip(FindRootRect("P1InvokerZone"), true);
            StyleInvokerStrip(FindRootRect("P2InvokerZone"), false);
            StyleSidePile(FindRootRect("P1VoidZone"), string.Empty, VoidTint, false);
            StyleSidePile(FindRootRect("P2VoidZone"), string.Empty, VoidTint, false);
            StyleSidePile(FindRootRect("P1DeckPile"), string.Empty, DeckTint, true);
            StyleSidePile(FindRootRect("P2DeckPile"), string.Empty, DeckTint, true);

            Sync3DBoardStageUsage();
            Debug.Log("[Battle] RefreshBattlefieldLayout end");
        }

        private void ForceBattleCanvasVisible(RectTransform canvasRoot)
        {
            if (canvasRoot == null)
                return;

            var canvas = canvasRoot.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = true;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 5000;
            }

            var group = canvasRoot.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.blocksRaycasts = true;
                group.interactable = true;
            }
        }

        private void NeutralizeFullscreenBlackOverlays(RectTransform canvasRoot)
        {
            if (canvasRoot == null)
                return;

            foreach (Transform child in canvasRoot)
            {
                if (child == null || !child.gameObject.activeSelf)
                    continue;

                if (child.name.StartsWith("Board", StringComparison.OrdinalIgnoreCase)
                    || child.name.StartsWith("P1", StringComparison.OrdinalIgnoreCase)
                    || child.name.StartsWith("P2", StringComparison.OrdinalIgnoreCase)
                    || child.name.Contains("Hand")
                    || child.name.Contains("Field")
                    || child.name.Contains("Pillar")
                    || child.name.Contains("Invoker")
                    || child.name.Contains("Seal")
                    || child.name.Contains("Domain")
                    || child.name.Contains("Void")
                    || child.name.Contains("Deck"))
                    continue;

                var rect = child as RectTransform;
                var image = child.GetComponent<Image>();
                if (rect == null || image == null)
                    continue;

                bool fillsCanvas = rect.anchorMin.x <= 0.02f
                    && rect.anchorMin.y <= 0.02f
                    && rect.anchorMax.x >= 0.98f
                    && rect.anchorMax.y >= 0.98f;
                Color color = image.color;
                bool opaqueBlack = color.a >= 0.82f && color.r <= 0.04f && color.g <= 0.04f && color.b <= 0.04f;
                if (!fillsCanvas || !opaqueBlack)
                    continue;

                Debug.LogWarning($"[BattleRender] Disabled fullscreen black overlay '{child.name}'.");
                child.gameObject.SetActive(false);
            }
        }

        private void EnsureBattlefieldRuntimeRoots(RectTransform canvasRoot)
        {
            EnsureContainerActive(p1HandContainer);
            EnsureContainerActive(p2HandContainer);
            EnsureContainerActive(p1FieldContainer);
            EnsureContainerActive(p2FieldContainer);
            EnsureContainerActive(p1PillarContainer);
            EnsureContainerActive(p2PillarContainer);

            EnsureRuntimeZone(canvasRoot, "P1DeckPile", "P1SEDeck");
            EnsureRuntimeZone(canvasRoot, "P2DeckPile", "P2SEDeck");
            EnsureRuntimeZone(canvasRoot, "P1VoidZone");
            EnsureRuntimeZone(canvasRoot, "P2VoidZone");
            EnsureRuntimeZone(canvasRoot, "P1SealZone");
            EnsureRuntimeZone(canvasRoot, "P2SealZone");
            EnsureRuntimeZone(canvasRoot, "LeftDomainZone");
            EnsureRuntimeZone(canvasRoot, "ActiveDomainZone", "DomainZone");
        }

        private static void EnsureContainerActive(Transform container)
        {
            if (container != null && !container.gameObject.activeSelf)
                container.gameObject.SetActive(true);
        }

        private RectTransform EnsureRuntimeZone(RectTransform canvasRoot, string name, params string[] aliases)
        {
            RectTransform rect = FindDeepRect(canvasRoot, name);
            if (rect == null && aliases != null)
            {
                for (int i = 0; i < aliases.Length && rect == null; i++)
                    rect = FindDeepRect(canvasRoot, aliases[i]);
            }

            if (rect == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(canvasRoot, false);
                rect = go.GetComponent<RectTransform>();
            }
            else
            {
                if (rect.transform.parent != canvasRoot)
                    rect.SetParent(canvasRoot, false);

                rect.gameObject.name = name;
                if (!rect.gameObject.activeSelf)
                    rect.gameObject.SetActive(true);

                if (rect.GetComponent<Image>() == null)
                    rect.gameObject.AddComponent<Image>();
            }

            var image = rect.GetComponent<Image>();
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform FindDeepRect(Transform root, string name)
        {
            if (root == null)
                return null;

            foreach (Transform child in root)
            {
                if (child.name == name)
                    return child as RectTransform;

                RectTransform nested = FindDeepRect(child, name);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private BattlefieldLayoutMetrics GetBattlefieldLayoutMetrics()
        {
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            float wideT = Mathf.InverseLerp(1.45f, 2.1f, aspect);
            float leftLaneLeft = 0.018f;
            float leftLaneRight = Mathf.Lerp(0.112f, 0.124f, wideT);
            float rightLaneLeft = Mathf.Lerp(0.872f, 0.884f, wideT);
            float rightLaneRight = 0.982f;
            float arenaLeft = 0.018f;
            float arenaRight = 0.982f;
            float shoulderWidth = Mathf.Lerp(0.102f, 0.116f, wideT);
            float pillarLeft = Mathf.Clamp(rightLaneLeft - shoulderWidth - 0.014f, arenaLeft + 0.30f, rightLaneLeft - 0.12f);
            float pillarRight = rightLaneLeft - 0.018f;
            float invokerLeft = pillarLeft - Mathf.Lerp(0.018f, 0.022f, wideT);
            float invokerRight = pillarRight;
            float handInset = Mathf.Lerp(0.018f, 0.030f, wideT);
            float logLeft = rightLaneLeft;

            return new BattlefieldLayoutMetrics(
                wideT,
                arenaLeft,
                arenaRight,
                pillarLeft,
                pillarRight,
                logLeft,
                arenaLeft + 0.10f,
                pillarLeft - 0.020f,
                invokerLeft,
                invokerRight,
                leftLaneLeft,
                leftLaneRight,
                rightLaneLeft,
                rightLaneRight,
                leftLaneRight + handInset,
                rightLaneLeft - handInset);
        }

        private void ConfigureBoardAtmosphere(RectTransform canvasRoot, BattlefieldLayoutMetrics metrics)
        {
            bool use3DStage = ShouldUseBoardStage3D() && BattleBoardStage3D.HasBoardPrefab();
            BattlefieldPalette palette = ResolveBattlefieldPalette(player1Deck);
            Sprite boardMat = null;
            if (!use3DStage)
            {
                boardMat = BattlePresentationRuntime.ResolveBoardMat();
            }
            Sprite boardOverlay = GetGeneratedBoardOverlay();
            Sprite boardGlow = GetGeneratedGlowSprite();
            const float enemyFieldMin = 0.620f;
            const float enemyFieldMax = 0.764f;
            const float playerFieldMin = 0.292f;
            const float playerFieldMax = 0.436f;
            const float enemyPillarMin = 0.748f;
            const float enemyPillarMax = 0.826f;
            const float playerPillarMin = 0.174f;
            const float playerPillarMax = 0.252f;
            float matSideBay = Mathf.Lerp(0.124f, 0.138f, metrics.WideT);
            float fieldBandLeft = metrics.BoardLeft + matSideBay;
            float fieldBandRight = metrics.BoardRight - matSideBay;
            EnsureBackdropLayer(canvasRoot, "BoardBackdrop", 0, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                string.Empty, palette.Backdrop);
            EnsureBackdropLayer(canvasRoot, "BoardBackdropTint", 1, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                string.Empty, WithAlpha(palette.Glow, 0.11f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardLeftCurtain", 2, new Vector2(0f, 0f), new Vector2(metrics.BoardLeft - 0.02f, 1f), Vector2.zero, Vector2.zero,
                string.Empty, Color.clear);
            EnsureBackdropLayer(canvasRoot, "BoardRightCurtain", 3, new Vector2(metrics.BoardRight + 0.02f, 0f), Vector2.one, Vector2.zero, Vector2.zero,
                string.Empty, Color.clear);
            EnsureBackdropLayer(canvasRoot, "BoardTopVeil", 4, new Vector2(metrics.BoardLeft - 0.03f, 0.90f), new Vector2(metrics.BoardRight + 0.03f, 1f), Vector2.zero, Vector2.zero,
                string.Empty, Color.clear);
            EnsureBackdropLayer(canvasRoot, "BoardBottomVeil", 5, new Vector2(metrics.BoardLeft - 0.03f, 0f), new Vector2(metrics.BoardRight + 0.03f, 0.10f), Vector2.zero, Vector2.zero,
                string.Empty, Color.clear);
            EnsureBackdropLayer(canvasRoot, "BoardArenaShadow", 6, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                string.Empty, Color.clear);
            // The selected deck supplies the playmat image. Keep it inside the mat, not across the whole scene.
            EnsureBackdropLayer(canvasRoot, "BoardArenaSurface", 7, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                string.Empty, use3DStage ? new Color(0f, 0f, 0f, 0.02f) : boardMat != null ? new Color(1f, 1f, 1f, 0.90f) : palette.Surface, boardMat);
            EnsureBackdropLayer(canvasRoot, "BoardArenaRim", 8, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                string.Empty, Color.clear);
            EnsureBackdropLayer(canvasRoot, "BoardArenaInset", 9, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                string.Empty, Color.clear, boardOverlay);
            EnsureBackdropLayer(canvasRoot, "BoardEnemyFieldAura", 10, new Vector2(fieldBandLeft, enemyFieldMin), new Vector2(fieldBandRight, enemyFieldMax), Vector2.zero, Vector2.zero,
                string.Empty, WithAlpha(palette.EnemyAura, 0.026f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardPlayerFieldAura", 11, new Vector2(fieldBandLeft, playerFieldMin), new Vector2(fieldBandRight, playerFieldMax), Vector2.zero, Vector2.zero,
                string.Empty, WithAlpha(palette.PlayerAura, 0.028f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardEnemyPillarAura", 12, new Vector2(metrics.BoardLeft + 0.150f, enemyPillarMin), new Vector2(metrics.BoardRight - 0.150f, enemyPillarMax), Vector2.zero, Vector2.zero,
                string.Empty, new Color(1f, 0.82f, 0.48f, 0.052f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardPlayerPillarAura", 13, new Vector2(metrics.BoardLeft + 0.150f, playerPillarMin), new Vector2(metrics.BoardRight - 0.150f, playerPillarMax), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.76f, 0.88f, 1f, 0.050f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardArenaTrimTop", 14, new Vector2(metrics.BoardLeft + 0.022f, 0.110f), new Vector2(metrics.BoardRight - 0.022f, 0.116f), Vector2.zero, Vector2.zero,
                "UI/divider-gold", new Color(0.98f, 0.88f, 0.58f, 0.10f));
            EnsureBackdropLayer(canvasRoot, "BoardArenaTrimBottom", 15, new Vector2(metrics.BoardLeft + 0.022f, 0.884f), new Vector2(metrics.BoardRight - 0.022f, 0.890f), Vector2.zero, Vector2.zero,
                "UI/divider-gold", new Color(0.98f, 0.88f, 0.58f, 0.10f));
            EnsureBackdropLayer(canvasRoot, "BoardEnemyFieldTrack", 16, new Vector2(fieldBandLeft, enemyFieldMin + 0.008f), new Vector2(fieldBandRight, enemyFieldMax - 0.008f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.06f, 0.04f, 0.09f, 0.025f));
            EnsureBackdropLayer(canvasRoot, "BoardPlayerFieldTrack", 17, new Vector2(fieldBandLeft, playerFieldMin + 0.008f), new Vector2(fieldBandRight, playerFieldMax - 0.008f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.03f, 0.10f, 0.13f, 0.025f));
            EnsureBackdropLayer(canvasRoot, "BoardEnemyPillarTrack", 18, new Vector2(metrics.BoardLeft + 0.140f, enemyPillarMin + 0.004f), new Vector2(metrics.BoardRight - 0.140f, enemyPillarMax - 0.004f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(palette.EnemyAura.r, palette.EnemyAura.g, palette.EnemyAura.b, 0.048f));
            EnsureBackdropLayer(canvasRoot, "BoardPlayerPillarTrack", 19, new Vector2(metrics.BoardLeft + 0.140f, playerPillarMin + 0.004f), new Vector2(metrics.BoardRight - 0.140f, playerPillarMax - 0.004f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(palette.PlayerAura.r, palette.PlayerAura.g, palette.PlayerAura.b, 0.052f));
            EnsureBackdropLayer(canvasRoot, "BoardCenterLine", 20, new Vector2(metrics.BoardLeft + 0.08f, 0.498f), new Vector2(metrics.BoardRight - 0.08f, 0.502f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.92f, 0.78f, 0.52f, 0.18f));
            EnsureBackdropLayer(canvasRoot, "BoardCenterSealGlow", 21, new Vector2(metrics.BoardLeft + 0.30f, 0.390f), new Vector2(metrics.BoardRight - 0.30f, 0.610f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(1f, 0.95f, 0.78f, 0.024f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardArenaGlow", 22, new Vector2(metrics.BoardLeft + 0.018f, 0.126f), new Vector2(metrics.BoardRight - 0.018f, 0.874f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(1f, 0.96f, 0.82f, 0.018f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardVignette", 23, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                string.Empty, new Color(0f, 0f, 0f, 0.14f));
            EnsureBackdropLayer(canvasRoot, "BoardCornerGlowTL", 24, new Vector2(metrics.BoardLeft - 0.010f, 0.804f), new Vector2(metrics.BoardLeft + 0.108f, 0.914f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.98f, 0.82f, 0.46f, 0.03f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardCornerGlowTR", 25, new Vector2(metrics.BoardRight - 0.108f, 0.804f), new Vector2(metrics.BoardRight + 0.010f, 0.914f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.72f, 0.86f, 1f, 0.03f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardCornerGlowBL", 26, new Vector2(metrics.BoardLeft - 0.010f, 0.086f), new Vector2(metrics.BoardLeft + 0.108f, 0.196f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.72f, 0.86f, 1f, 0.03f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardCornerGlowBR", 27, new Vector2(metrics.BoardRight - 0.108f, 0.086f), new Vector2(metrics.BoardRight + 0.010f, 0.196f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.98f, 0.82f, 0.46f, 0.03f), boardGlow);
            EnsureBackdropLayer(canvasRoot, "BoardCenterSigil", 28, new Vector2(0.404f, 0.384f), new Vector2(0.596f, 0.616f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(1f, 0.94f, 0.76f, 0.026f), boardOverlay);
            EnsureBackdropLayer(canvasRoot, "BoardLeftZoneWash", 29, new Vector2(metrics.BoardLeft, 0f), new Vector2(metrics.BoardLeft + 0.164f, 1f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.080f, 0.060f, 0.130f, 1f));
            EnsureBackdropLayer(canvasRoot, "BoardRightZoneWash", 30, new Vector2(metrics.BoardRight - 0.164f, 0f), new Vector2(metrics.BoardRight, 1f), Vector2.zero, Vector2.zero,
                string.Empty, new Color(0.060f, 0.078f, 0.125f, 1f));

            if (Camera.main != null)
                Camera.main.backgroundColor = palette.Camera;
        }

        private BattlefieldPalette ResolveBattlefieldPalette(DeckData opponentDeck)
        {
            CreatureType type = opponentDeck != null ? opponentDeck.primaryCreatureType : CreatureType.Elemental;
            Element element = opponentDeck != null ? opponentDeck.element : Element.Flame;

            Color accent = type switch
            {
                CreatureType.Machine => new Color(0.45f, 0.74f, 0.88f, 1f),
                CreatureType.Artificial => new Color(0.86f, 0.72f, 1f, 1f),
                CreatureType.Spirit => new Color(0.48f, 0.92f, 0.86f, 1f),
                CreatureType.Undead => new Color(0.63f, 0.38f, 0.92f, 1f),
                _ => CardVisual.GetElementColor(element),
            };

            Color deep = type switch
            {
                CreatureType.Machine => new Color(0.025f, 0.045f, 0.058f, 1f),
                CreatureType.Artificial => new Color(0.045f, 0.036f, 0.072f, 1f),
                CreatureType.Spirit => new Color(0.025f, 0.064f, 0.066f, 1f),
                CreatureType.Undead => new Color(0.030f, 0.020f, 0.050f, 1f),
                _ => Color.Lerp(new Color(0.025f, 0.032f, 0.045f, 1f), accent, 0.16f),
            };

            Color surface = Color.Lerp(deep, accent, type == CreatureType.Elemental ? 0.28f : 0.22f);
            surface.a = 1f;
            Color matTint = Color.Lerp(Color.white, accent, 0.18f);
            matTint.a = 0.94f;
            Color glow = accent;
            glow.a = 0.20f;
            Color enemyAura = Color.Lerp(accent, Color.white, 0.18f);
            enemyAura.a = 0.16f;
            Color playerAura = new Color(0.44f, 0.78f, 1f, 0.12f);
            return new BattlefieldPalette(
                deep,
                surface,
                matTint,
                glow,
                enemyAura,
                playerAura,
                Color.Lerp(deep, Color.black, 0.35f));
        }

        private void EnsureBackdropLayer(RectTransform parent, string name, int siblingIndex,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, string spritePath, Color color, Sprite spriteOverride = null)
        {
            Transform existing = parent.Find(name);
            GameObject layer = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            if (existing == null)
            {
                layer.transform.SetParent(parent, false);
                layer.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
            }

            var rt = layer.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            rt.localScale = Vector3.one;

            var image = layer.GetComponent<Image>();
            image.sprite = spriteOverride ?? Resources.Load<Sprite>(spritePath);
            image.type = ShouldUseSlicedImage(spriteOverride, spritePath) ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
            image.color = color;
            image.raycastTarget = false;
        }

        private static bool ShouldUseSlicedImage(Sprite spriteOverride, string spritePath)
        {
            if (spriteOverride != null)
                return false;

            return !string.IsNullOrEmpty(spritePath)
                && (spritePath.Contains("panel") || spritePath.Contains("header"));
        }

        private static Sprite GetGeneratedBoardMat()
        {
            if (_generatedBoardMat == null)
            {
                var stoneTex = Resources.Load<Texture2D>("UI/board-stone");
                if (stoneTex != null)
                    _generatedBoardMat = Sprite.Create(stoneTex, new Rect(0, 0, stoneTex.width, stoneTex.height), new Vector2(0.5f, 0.5f), 100f);
                if (_generatedBoardMat == null)
                    _generatedBoardMat = BattlePresentationRuntime.ResolveBoardMat();
                if (_generatedBoardMat == null)
                    _generatedBoardMat = Resources.Load<Sprite>("UI/battle-bg");
                if (_generatedBoardMat == null)
                    _generatedBoardMat = CreateGeneratedBoardSprite(false);
            }
            return _generatedBoardMat;
        }

        private static Sprite GetGeneratedBoardOverlay()
        {
            if (_generatedBoardOverlay == null)
            {
                _generatedBoardOverlay = BattlePresentationRuntime.ResolveBoardOverlay();
                if (_generatedBoardOverlay == null)
                    _generatedBoardOverlay = CreateGeneratedBoardSprite(true);
            }
            return _generatedBoardOverlay;
        }

        private static Sprite GetGeneratedGlowSprite()
        {
            _generatedGlowSprite ??= CreateGeneratedGlowSprite();
            return _generatedGlowSprite;
        }

        private static Sprite CreateGeneratedBoardSprite(bool overlay)
        {
            const int size = 1024;
            const float topFieldCenter = 0.607f;
            const float bottomFieldCenter = 0.393f;
            const float fieldWidth = 0.868f;
            const float fieldHeight = 0.146f;
            const float topPillarCenter = 0.786f;
            const float bottomPillarCenter = 0.214f;
            const float pillarWidth = 0.74f;
            const float pillarHeight = 0.074f;
            const float topInvokerCenter = 0.872f;
            const float bottomInvokerCenter = 0.128f;
            const float invokerWidth = 0.26f;
            const float invokerHeight = 0.054f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = overlay ? "GeneratedBoardOverlay" : "GeneratedBoardMat"
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float dx = u - 0.5f;
                    float dy = v - 0.5f;
                    float radial = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float grainA = Mathf.PerlinNoise(u * 5.4f + 0.11f, v * 5.8f + 0.37f);
                    float grainB = Mathf.PerlinNoise(u * 15.3f + 3.1f, v * 14.7f + 1.7f);
                    float grain = ((grainA * 0.62f) + (grainB * 0.38f)) - 0.5f;

                    float topFieldFill = RoundedRectFill(u, v, 0.5f, topFieldCenter, fieldWidth, fieldHeight, 0.040f, 0.010f);
                    float bottomFieldFill = RoundedRectFill(u, v, 0.5f, bottomFieldCenter, fieldWidth, fieldHeight, 0.040f, 0.010f);
                    float topPillarFill = RoundedRectFill(u, v, 0.5f, topPillarCenter, pillarWidth, pillarHeight, 0.032f, 0.010f);
                    float bottomPillarFill = RoundedRectFill(u, v, 0.5f, bottomPillarCenter, pillarWidth, pillarHeight, 0.032f, 0.010f);
                    float topInvokerFill = RoundedRectFill(u, v, 0.5f, topInvokerCenter, invokerWidth, invokerHeight, 0.026f, 0.010f);
                    float bottomInvokerFill = RoundedRectFill(u, v, 0.5f, bottomInvokerCenter, invokerWidth, invokerHeight, 0.026f, 0.010f);
                    float centerMajorRing = SoftRing(radial, 0.182f, 0.0045f);
                    float centerMinorRing = SoftRing(radial, 0.118f, 0.0036f);
                    float centerCoreRing = SoftRing(radial, 0.060f, 0.0032f);
                    float crossLine = SoftLine(Mathf.Abs(u - 0.5f), 0f, 0.0022f) * 0.11f
                        + SoftLine(Mathf.Abs(v - 0.5f), 0f, 0.0022f) * 0.11f;

                    if (overlay)
                    {
                        float fieldOutline = RoundedRectOutline(u, v, 0.5f, topFieldCenter, fieldWidth, fieldHeight, 0.040f, 0.0045f) * 0.62f
                            + RoundedRectOutline(u, v, 0.5f, bottomFieldCenter, fieldWidth, fieldHeight, 0.040f, 0.0045f) * 0.62f;
                        float pillarOutline = RoundedRectOutline(u, v, 0.5f, topPillarCenter, pillarWidth, pillarHeight, 0.032f, 0.0040f) * 0.48f
                            + RoundedRectOutline(u, v, 0.5f, bottomPillarCenter, pillarWidth, pillarHeight, 0.032f, 0.0040f) * 0.48f;
                        float invokerOutline = RoundedRectOutline(u, v, 0.5f, topInvokerCenter, invokerWidth, invokerHeight, 0.026f, 0.0034f) * 0.28f
                            + RoundedRectOutline(u, v, 0.5f, bottomInvokerCenter, invokerWidth, invokerHeight, 0.026f, 0.0034f) * 0.28f;
                        float centerSeal = centerMajorRing * 0.34f
                            + centerMinorRing * 0.24f
                            + centerCoreRing * 0.18f
                            + crossLine;
                        float divider = SoftLine(Mathf.Abs(v - 0.5f), 0f, 0.0024f) * 0.16f;
                        float fillGlow = topFieldFill * 0.08f + bottomFieldFill * 0.08f
                            + topPillarFill * 0.06f + bottomPillarFill * 0.06f
                            + topInvokerFill * 0.04f + bottomInvokerFill * 0.04f;
                        float alpha = Mathf.Clamp01(fieldOutline + pillarOutline + invokerOutline + centerSeal + divider + fillGlow);
                        Color overlayColor = Color.Lerp(
                            new Color(0.72f, 0.80f, 0.90f, 1f),
                            new Color(0.90f, 0.94f, 1f, 1f),
                            Mathf.Clamp01(fieldOutline + pillarOutline * 0.8f + centerSeal * 0.5f));
                        overlayColor.a = alpha * 0.72f;
                        pixels[y * size + x] = overlayColor;
                        continue;
                    }

                    float boardCenter = Mathf.Clamp01(1f - radial / 0.74f);
                    Color edgeColor = new(0.16f, 0.12f, 0.09f, 1f);
                    Color centerColor = new(0.46f, 0.37f, 0.27f, 1f);
                    Color baseColor = Color.Lerp(edgeColor, centerColor, Mathf.SmoothStep(0f, 1f, boardCenter));
                    baseColor = Color.Lerp(baseColor, new Color(0.62f, 0.52f, 0.38f, 1f), Mathf.Clamp01((0.16f - radial) / 0.16f) * 0.18f);

                    float warmField = topFieldFill * 0.06f + topPillarFill * 0.03f;
                    float coolField = bottomFieldFill * 0.08f + bottomPillarFill * 0.04f;
                    float laneEmboss = warmField + coolField + topInvokerFill * 0.07f + bottomInvokerFill * 0.07f;
                    baseColor = Color.Lerp(baseColor, new Color(0.28f, 0.22f, 0.17f, 1f), laneEmboss * 0.46f);
                    baseColor = Color.Lerp(baseColor, new Color(0.56f, 0.45f, 0.31f, 1f), warmField * 0.18f);
                    baseColor = Color.Lerp(baseColor, new Color(0.64f, 0.54f, 0.40f, 1f), coolField * 0.16f);

                    float centerEngraving = centerMajorRing * 0.11f
                        + centerMinorRing * 0.07f
                        + centerCoreRing * 0.05f
                        + crossLine * 0.30f;
                    baseColor = Color.Lerp(baseColor, new Color(0.22f, 0.17f, 0.12f, 1f), centerEngraving);

                    float border = RoundedRectOutline(u, v, 0.5f, 0.5f, 0.972f, 0.972f, 0.046f, 0.010f) * 0.26f
                        + RoundedRectOutline(u, v, 0.5f, 0.5f, 0.934f, 0.934f, 0.036f, 0.0065f) * 0.14f;
                    float wear = Mathf.Clamp01((0.08f - edge) / 0.08f) * 0.20f;
                    float grainStrength = grain * 0.08f;
                    baseColor.r = Mathf.Clamp01(baseColor.r + grainStrength);
                    baseColor.g = Mathf.Clamp01(baseColor.g + grainStrength * 0.82f);
                    baseColor.b = Mathf.Clamp01(baseColor.b + grainStrength * 0.62f);
                    baseColor = Color.Lerp(baseColor, new Color(0.08f, 0.06f, 0.04f, 1f), border + wear);

                    pixels[y * size + x] = baseColor;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateGeneratedGlowSprite()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GeneratedBoardGlow"
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = ((y + 0.5f) / size) - 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float u = ((x + 0.5f) / size) - 0.5f;
                    float dist = Mathf.Sqrt(u * u + v * v) / 0.7072f;
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    alpha = Mathf.Pow(alpha, 1.85f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static float SoftRing(float distance, float radius, float width)
        {
            return Mathf.Clamp01(1f - Mathf.Abs(distance - radius) / Mathf.Max(0.0001f, width));
        }

        private static float SoftLine(float distance, float center, float width)
        {
            return Mathf.Clamp01(1f - Mathf.Abs(distance - center) / Mathf.Max(0.0001f, width));
        }

        private static float RoundedRectOutline(float u, float v, float cx, float cy, float width, float height, float radius, float thickness)
        {
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float qx = Mathf.Abs(u - cx) - (halfW - radius);
            float qy = Mathf.Abs(v - cy) - (halfH - radius);
            float outer = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
            return Mathf.Clamp01(1f - Mathf.Abs(outer) / Mathf.Max(0.0001f, thickness));
        }

        private static float RoundedRectFill(float u, float v, float cx, float cy, float width, float height, float radius, float softness)
        {
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float qx = Mathf.Abs(u - cx) - (halfW - radius);
            float qy = Mathf.Abs(v - cy) - (halfH - radius);
            float distance = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
            return Mathf.Clamp01(1f - Mathf.Max(distance, 0f) / Mathf.Max(0.0001f, softness));
        }

        private void SuppressLegacyBattlefieldChrome(RectTransform canvasRoot)
        {
            SetRootElementActive(canvasRoot, false,
                "LeftSidebar",
                "RightSidebar",
                "RightPanel",
                "P1FieldZoneBg",
                "P2FieldZoneBg",
                "P1PillarZoneBg",
                "P2PillarZoneBg",
                "P1FieldLabel",
                "P2FieldLabel",
                "P1PillarLabel",
                "P2PillarLabel",
                "GameLogPanel",
                "CenterControls",
                "P1SEDeck",
                "P2SEDeck",
                "P1SEPool",
                "P2SEPool");

            // Also hide GameLogPanel if nested inside another panel. Do not recursively hide
            // every "Background" object; card frames, panels, and generated board layers reuse
            // that name internally.
            HideNestedElement(canvasRoot, "GameLogPanel");
            HideNestedElement(canvasRoot, "P1SEDeck");
            HideNestedElement(canvasRoot, "P2SEDeck");
            HideNestedElement(canvasRoot, "P1SEPool");
            HideNestedElement(canvasRoot, "P2SEPool");
            SuppressLegacyInvokerContents(FindRootRect("P1InvokerZone"));
            SuppressLegacyInvokerContents(FindRootRect("P2InvokerZone"));

            // Reparent END TURN button out of CenterControls so it remains usable
            if (endTurnButton != null && endTurnButton.transform.parent != canvasRoot)
                endTurnButton.transform.SetParent(canvasRoot, false);

            // Dice area: small centered square for a standard D6
            var diceAreaT = canvasRoot.Find("DiceArea");
            if (diceAreaT != null)
            {
                var diceRT = diceAreaT.GetComponent<RectTransform>();
                diceRT.anchorMin = new Vector2(0.43f, 0.64f);
                diceRT.anchorMax = new Vector2(0.57f, 0.78f);
                diceRT.offsetMin = Vector2.zero;
                diceRT.offsetMax = Vector2.zero;

                // Don't hide DiceArea if dice is rolling or opening sequence is active
                if (diceRoller != null && !diceRoller.IsRolling && !_suppressSERolledVisual)
                    diceAreaT.gameObject.SetActive(false);
            }

            // Disable raycast on full-screen background so card clicks pass through
            var bg = canvasRoot.Find("Background");
            if (bg != null)
            {
                bg.gameObject.SetActive(false);
            }
        }

        private void SetRootElementActive(RectTransform canvasRoot, bool active, params string[] names)
        {
            if (canvasRoot == null)
                return;

            foreach (string name in names)
            {
                Transform child = canvasRoot.Find(name);
                if (child != null && child.gameObject.activeSelf != active)
                    child.gameObject.SetActive(active);
            }
        }

        private void HideNestedElement(Transform root, string name)
        {
            // Recursively find and hide elements that aren't direct children
            foreach (Transform child in root)
            {
                if (child.name == name)
                {
                    child.gameObject.SetActive(false);
                    return;
                }
                HideNestedElement(child, name);
            }
        }

        private void HideAllNestedElements(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name && child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);

                HideAllNestedElements(child, name);
            }
        }

        private void SuppressLegacyInvokerContents(RectTransform zone)
        {
            if (zone == null)
                return;

            foreach (Transform child in zone)
            {
                if (child.name == "RuntimeInvokerAnchor")
                    continue;

                child.gameObject.SetActive(false);
            }
        }

        private void ApplyResponsiveBattlefieldLayout(BattlefieldLayoutMetrics metrics)
        {
            Debug.Log("[Battle] >>> LAYOUT v12 — measured playmat zones <<<");

            // Reference-driven geometry for a 1600x1000 playtest:
            // daemon lanes use the real card footprint and mirror across the center line.
            const float leftColumnMin = 0.022f;
            const float leftColumnMax = 0.146f;
            const float rightColumnMin = 0.854f;
            const float rightColumnMax = 0.978f;
            const float combatMin = 0.245f;
            const float combatMax = 0.755f;
            const float handMin = 0.262f;
            const float handMax = 0.738f;

            ApplyRect(p2HandContainer as RectTransform, new Vector2(0.372f, 0.875f), new Vector2(0.628f, 0.998f), Vector2.zero, Vector2.zero);
            ApplyRect(p2FieldContainer as RectTransform, new Vector2(combatMin, 0.640f), new Vector2(combatMax, 0.865f), Vector2.zero, Vector2.zero);
            ApplyRect(p2PillarContainer as RectTransform, new Vector2(0.155f, 0.810f), new Vector2(0.242f, 0.948f), Vector2.zero, Vector2.zero);
            ApplyRect(FindRootRect("P2InvokerZone"), new Vector2(0.854f, 0.914f), new Vector2(0.978f, 0.982f), Vector2.zero, Vector2.zero);

            ApplyRect(p1FieldContainer as RectTransform, new Vector2(combatMin, 0.270f), new Vector2(combatMax, 0.460f), Vector2.zero, Vector2.zero);
            ApplyRect(p1PillarContainer as RectTransform, new Vector2(0.155f, 0.025f), new Vector2(0.242f, 0.209f), Vector2.zero, Vector2.zero);
            ApplyRect(FindRootRect("P1InvokerZone"), new Vector2(0.854f, 0.018f), new Vector2(0.978f, 0.086f), Vector2.zero, Vector2.zero);
            ApplyRect(p1HandContainer as RectTransform, new Vector2(handMin, 0.000f), new Vector2(handMax, 0.148f), Vector2.zero, Vector2.zero);

            ApplyRect(FindRootRect("P2VoidZone"), new Vector2(leftColumnMin, 0.654f), new Vector2(leftColumnMax, 0.910f), Vector2.zero, Vector2.zero);
            ApplyRect(FindRootRect("LeftDomainZone"), new Vector2(leftColumnMin, 0.372f), new Vector2(leftColumnMax, 0.626f), Vector2.zero, Vector2.zero);
            ApplyRect(FindRootRect("P1VoidZone"), new Vector2(leftColumnMin, 0.090f), new Vector2(leftColumnMax, 0.346f), Vector2.zero, Vector2.zero);

            ApplyRect(FindRootRect("P2SealZone"), new Vector2(rightColumnMin, 0.654f), new Vector2(rightColumnMax, 0.910f), Vector2.zero, Vector2.zero);
            ApplyRect(FindRootRect("ActiveDomainZone"), new Vector2(rightColumnMin, 0.372f), new Vector2(rightColumnMax, 0.626f), Vector2.zero, Vector2.zero);
            ApplyRect(FindRootRect("P1SealZone"), new Vector2(rightColumnMin, 0.090f), new Vector2(rightColumnMax, 0.346f), Vector2.zero, Vector2.zero);
            ApplyRect(FindRootRect("P2DeckPile"), new Vector2(0.744f, 0.755f), new Vector2(0.852f, 0.982f), Vector2.zero, Vector2.zero);
            ApplyRect(FindRootRect("P1DeckPile"), new Vector2(0.744f, 0.006f), new Vector2(0.852f, 0.233f), Vector2.zero, Vector2.zero);

            StyleStaticZonePanel(FindRootRect("LeftDomainZone"), "DOMAIN", new Color(0.10f, 0.16f, 0.24f, 0.40f), new Color(0.55f, 0.78f, 1f, 0.32f));
            EnsureZoneHeader(p1PillarContainer as RectTransform, "PILLARS");
            EnsureZoneHeader(p2PillarContainer as RectTransform, "PILLARS");
            EnsureZoneHeader(FindRootRect("P1DeckPile"), "DECK");

            // Kill any HorizontalLayoutGroups that could override our anchors
            DestroyHLG(p1HandContainer);
            DestroyHLG(p2HandContainer);
            DestroyHLG(p1FieldContainer);
            DestroyHLG(p2FieldContainer);
            DestroyHLG(p1PillarContainer);
            DestroyHLG(p2PillarContainer);

            LayoutActionRail(metrics);
        }

        private static void DestroyHLG(Transform container)
        {
            if (container == null) return;
            var hlg = container.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) Destroy(hlg);
        }

        private void ApplyRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            if (rect == null)
                return;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private RectTransform FindRootRect(string name)
        {
            var root = transform as RectTransform;
            if (root == null)
                return null;

            return root.Find(name) as RectTransform;
        }

        private void LayoutActionRail(BattlefieldLayoutMetrics metrics)
        {
            PositionActionButton(endTurnButton, new Vector2(metrics.LeftLaneLeft, 0.028f), new Vector2(metrics.LeftLaneRight + 0.028f, 0.082f));
            PositionActionButton(_forfeitButton, new Vector2(metrics.LeftLaneLeft, 0.088f), new Vector2(metrics.LeftLaneLeft + 0.072f, 0.124f));
        }

        private static void PositionActionButton(Button button, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (button == null)
                return;

            var rt = button.GetComponent<RectTransform>();
            if (rt == null)
                return;

            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private void StylePeripheralPanel(RectTransform panel, string spritePath, Color color, bool compact)
        {
            if (panel == null)
                return;

            var image = panel.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = Resources.Load<Sprite>(spritePath);
                image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = color;
            }

            var outline = panel.GetComponent<Outline>() ?? panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, compact ? 0.22f : 0.28f);
            outline.effectDistance = compact ? new Vector2(1f, -1f) : new Vector2(2f, -2f);

            var layout = panel.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = compact ? new RectOffset(16, 16, 8, 8) : new RectOffset(12, 12, 12, 12);
                layout.spacing = compact ? 12f : 8f;
            }
        }

        private void StyleInvokerStrip(RectTransform strip, bool isPlayer)
        {
            if (strip == null)
                return;

            var image = strip.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = Resources.Load<Sprite>("UI/panel-dark");
                image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = isPlayer
                    ? new Color(0.06f, 0.08f, 0.10f, 0.08f)
                    : new Color(0.08f, 0.06f, 0.08f, 0.08f);
                image.raycastTarget = false;
            }

            var layout = strip.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
                layout.enabled = false;

            Transform accent = strip.Find("Accent");
            if (accent != null)
                accent.gameObject.SetActive(false);
        }

        private void StyleSidePile(RectTransform pile, string spritePath, Color tint, bool tall)
        {
            if (pile == null)
                return;

            var image = pile.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = Resources.Load<Sprite>(spritePath);
                image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = new Color(tint.r, tint.g, tint.b, tint.a * 0.14f);
                image.raycastTarget = true;
            }

            var outline = pile.GetComponent<Outline>() ?? pile.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(tint.r * 1.5f, tint.g * 1.2f, tint.b * 1.4f, tint.a * 0.30f);
            outline.effectDistance = new Vector2(2f, -2f);

            Transform glowTransform = pile.Find("PileGlow");
            GameObject glow = glowTransform != null ? glowTransform.gameObject : new GameObject("PileGlow", typeof(RectTransform), typeof(Image));
            if (glowTransform == null)
                glow.transform.SetParent(pile, false);
            glow.transform.SetSiblingIndex(0);
            var glowRect = glow.GetComponent<RectTransform>();
            glowRect.anchorMin = new Vector2(-0.10f, -0.08f);
            glowRect.anchorMax = new Vector2(1.10f, 1.08f);
            glowRect.offsetMin = Vector2.zero;
            glowRect.offsetMax = Vector2.zero;
            var glowImage = glow.GetComponent<Image>();
            glowImage.sprite = GetGeneratedGlowSprite();
            glowImage.type = Image.Type.Simple;
            glowImage.color = new Color(tint.r, tint.g, tint.b, tall ? 0.10f : 0.08f);
            glowImage.raycastTarget = false;

            Transform frameTransform = pile.Find("PileFrame");
            GameObject frame = frameTransform != null ? frameTransform.gameObject : new GameObject("PileFrame", typeof(RectTransform), typeof(Image), typeof(Outline));
            if (frameTransform == null)
                frame.transform.SetParent(pile, false);
            frame.transform.SetSiblingIndex(1);
            var frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.12f, 0.12f);
            frameRect.anchorMax = new Vector2(0.88f, 0.84f);
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = Vector2.zero;
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            frameImage.type = frameImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            frameImage.color = tall
                ? new Color(tint.r, tint.g, tint.b, 0.16f)
                : new Color(tint.r, tint.g, tint.b, 0.13f);
            frameImage.raycastTarget = false;
            var frameOutline = frame.GetComponent<Outline>();
            frameOutline.effectColor = tall
                ? new Color(1f, 0.92f, 0.74f, 0.16f)
                : new Color(0.84f, 0.92f, 1f, 0.16f);
            frameOutline.effectDistance = new Vector2(1f, -1f);

            LayoutGroup layout = pile.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = pile.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = tall ? new RectOffset(2, 2, 4, 4) : new RectOffset(2, 2, 2, 2);
                layout.childAlignment = TextAnchor.MiddleCenter;
            }
        }

        private void StyleStaticZonePanel(RectTransform zone, string title, Color fill, Color outlineColor)
        {
            if (zone == null)
                return;

            var image = zone.GetComponent<Image>() ?? zone.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/panel-dark");
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = fill;
            image.raycastTarget = false;

            var outline = zone.GetComponent<Outline>() ?? zone.gameObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            EnsureZoneHeader(zone, title);
        }

        private void EnsureZoneHeader(RectTransform zone, string title)
        {
            if (zone == null)
                return;

            EnsureSocketLabel(zone, "ZoneHeader", title,
                new Vector2(8f, -7f), new Vector2(-8f, -25f), ZoneLabelFontSize, FontStyles.Bold,
                new Color(0.96f, 0.90f, 0.74f, ZoneLabelOpacity), TextAlignmentOptions.Top);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || !Application.isPlaying || !gameObject.activeInHierarchy || _layoutRefreshQueued)
                return;
            _layoutRefreshQueued = true;
            StartCoroutine(DeferredBattlefieldLayoutRefresh());
        }

        private IEnumerator DeferredBattlefieldLayoutRefresh()
        {
            yield return null;
            _layoutRefreshQueued = false;
            if (isActiveAndEnabled && gameObject.activeInHierarchy)
                RefreshBattlefieldLayout(transform as RectTransform);
        }

        private readonly struct BattlefieldLayoutMetrics
        {
            public readonly float WideT;
            public readonly float BoardLeft;
            public readonly float BoardRight;
            public readonly float PillarLeft;
            public readonly float PillarRight;
            public readonly float LogLeft;
            public readonly float ControlLeft;
            public readonly float ControlRight;
            public readonly float InvokerLeft;
            public readonly float InvokerRight;
            public readonly float LeftLaneLeft;
            public readonly float LeftLaneRight;
            public readonly float RightLaneLeft;
            public readonly float RightLaneRight;
            public readonly float HandLeft;
            public readonly float HandRight;

            public BattlefieldLayoutMetrics(float wideT, float boardLeft, float boardRight, float pillarLeft, float pillarRight,
                float logLeft, float controlLeft, float controlRight, float invokerLeft, float invokerRight,
                float leftLaneLeft, float leftLaneRight, float rightLaneLeft, float rightLaneRight,
                float handLeft, float handRight)
            {
                WideT = wideT;
                BoardLeft = boardLeft;
                BoardRight = boardRight;
                PillarLeft = pillarLeft;
                PillarRight = pillarRight;
                LogLeft = logLeft;
                ControlLeft = controlLeft;
                ControlRight = controlRight;
                InvokerLeft = invokerLeft;
                InvokerRight = invokerRight;
                LeftLaneLeft = leftLaneLeft;
                LeftLaneRight = leftLaneRight;
                RightLaneLeft = rightLaneLeft;
                RightLaneRight = rightLaneRight;
                HandLeft = handLeft;
                HandRight = handRight;
            }
        }

        private readonly struct BattlefieldPalette
        {
            public readonly Color Backdrop;
            public readonly Color Surface;
            public readonly Color MatTint;
            public readonly Color Glow;
            public readonly Color EnemyAura;
            public readonly Color PlayerAura;
            public readonly Color Camera;

            public BattlefieldPalette(Color backdrop, Color surface, Color matTint, Color glow, Color enemyAura, Color playerAura, Color camera)
            {
                Backdrop = backdrop;
                Surface = surface;
                MatTint = matTint;
                Glow = glow;
                EnemyAura = enemyAura;
                PlayerAura = playerAura;
                Camera = camera;
            }
        }

        private void ConfigureBoardLane(Transform container, string plateName, string spritePath, Color tint, RectOffset padding, float spacing)
        {
            if (container == null)
                return;

            var rect = container as RectTransform;
            if (rect == null)
                return;

            if (string.IsNullOrEmpty(spritePath) && tint.a <= 0.001f)
            {
                Transform existingPlate = rect.parent != null ? rect.parent.Find(plateName) : null;
                if (existingPlate != null)
                    existingPlate.gameObject.SetActive(false);
            }
            else
            {
                EnsureZonePlate(plateName, rect, spritePath, tint, Vector2.zero, Vector2.one, LanePlateInset);
            }

            var layout = container.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = padding;
                layout.spacing = spacing;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }
        }

        private void EnsureZonePlate(string name, RectTransform target, string spritePath, Color tint,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 inset)
        {
            if (target == null || target.parent == null)
                return;

            var parent = target.parent as RectTransform;
            if (parent == null)
                return;

            Transform existing = parent.Find(name);
            GameObject plate = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            if (existing == null)
            {
                plate.transform.SetParent(parent, false);
                plate.transform.SetSiblingIndex(Mathf.Max(0, target.GetSiblingIndex()));
            }

            var rt = plate.GetComponent<RectTransform>();
            if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
            {
                rt.anchorMin = target.anchorMin;
                rt.anchorMax = target.anchorMax;
                rt.offsetMin = target.offsetMin + inset;
                rt.offsetMax = target.offsetMax - inset;
                rt.pivot = target.pivot;
            }
            else
            {
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.offsetMin = new Vector2(inset.x, inset.y);
                rt.offsetMax = new Vector2(-inset.x, -inset.y);
            }

            var image = plate.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>(spritePath);
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = tint;
            image.raycastTarget = false;
        }

        private void StyleActionButton(Button button, string fallbackLabel)
        {
            if (button == null)
                return;

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = Resources.Load<Sprite>("UI/btn-gold");
                image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = Color.white;
            }

            var rt = button.GetComponent<RectTransform>();
            if (rt != null)
                rt.sizeDelta = Vector2.zero;

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = fallbackLabel;
                label.fontSize = 15;
                label.fontStyle = FontStyles.Bold;
                label.color = new Color(0.18f, 0.12f, 0.06f);
            }

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.28f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private void StyleTextBlock(TextMeshProUGUI text, float size, FontStyles style, Color color)
        {
            if (text == null)
                return;

            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;

            var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private void StyleHudText(TextMeshProUGUI text, float size, FontStyles style)
        {
            StyleTextBlock(text, size, style, new Color(0.98f, 0.95f, 0.90f));
        }

        private void StyleSlider(Slider slider, Color fillColor, Color backgroundColor)
        {
            if (slider == null)
                return;

            if (slider.fillRect != null)
            {
                var fill = slider.fillRect.GetComponent<Image>();
                if (fill != null)
                    fill.color = fillColor;
            }

            var background = slider.GetComponentInChildren<Image>();
            if (background != null)
                background.color = backgroundColor;
        }

        private void EnsureLogBackdrop(RectTransform logRect)
        {
            if (logRect == null || logRect.parent == null)
                return;

            var parent = logRect.parent as RectTransform;
            if (parent == null)
                return;

            const string backdropName = "LogBackdrop";
            Transform existing = parent.Find(backdropName);
            GameObject backdrop = existing != null ? existing.gameObject : new GameObject(backdropName, typeof(RectTransform), typeof(Image));
            if (existing == null)
                backdrop.transform.SetParent(parent, false);

            backdrop.transform.SetSiblingIndex(Mathf.Max(0, logRect.GetSiblingIndex()));

            var rt = backdrop.GetComponent<RectTransform>();
            rt.anchorMin = logRect.anchorMin;
            rt.anchorMax = logRect.anchorMax;
            rt.anchoredPosition = logRect.anchoredPosition;
            rt.sizeDelta = logRect.sizeDelta + new Vector2(20f, 20f);

            var image = backdrop.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/panel-dark");
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = ControlsTint;
            image.raycastTarget = false;
        }

        private void EnsureCombatFxLayer(RectTransform canvasRoot)
        {
            if (canvasRoot == null)
                return;

            Transform existingLayer = canvasRoot.Find("CombatFxLayer");
            GameObject layer = existingLayer != null ? existingLayer.gameObject : new GameObject("CombatFxLayer", typeof(RectTransform), typeof(CanvasGroup));
            if (existingLayer == null)
                layer.transform.SetParent(canvasRoot, false);

            _combatFxLayer = layer.GetComponent<RectTransform>();
            _combatFxLayer.anchorMin = Vector2.zero;
            _combatFxLayer.anchorMax = Vector2.one;
            _combatFxLayer.offsetMin = Vector2.zero;
            _combatFxLayer.offsetMax = Vector2.zero;
            _combatFxLayer.SetAsLastSibling();

            // Ensure the FX layer doesn't block raycasts to gameplay elements
            var fxCanvasGroup = layer.GetComponent<CanvasGroup>();
            if (fxCanvasGroup != null)
                fxCanvasGroup.blocksRaycasts = false;

            Transform existingText = _combatFxLayer.Find("TextLayer");
            GameObject textLayer = existingText != null ? existingText.gameObject : new GameObject("TextLayer", typeof(RectTransform));
            if (existingText == null)
                textLayer.transform.SetParent(_combatFxLayer, false);

            _combatTextLayer = textLayer.GetComponent<RectTransform>();
            _combatTextLayer.anchorMin = Vector2.zero;
            _combatTextLayer.anchorMax = Vector2.one;
            _combatTextLayer.offsetMin = Vector2.zero;
            _combatTextLayer.offsetMax = Vector2.zero;

            Transform existingFlash = _combatFxLayer.Find("FlashOverlay");
            GameObject flash = existingFlash != null ? existingFlash.gameObject : new GameObject("FlashOverlay", typeof(RectTransform), typeof(Image));
            if (existingFlash == null)
                flash.transform.SetParent(_combatFxLayer, false);

            _combatFlashOverlay = flash.GetComponent<Image>();
            var flashRect = flash.GetComponent<RectTransform>();
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = Vector2.zero;
            flashRect.offsetMax = Vector2.zero;
            _combatFlashOverlay.color = Color.clear;
            _combatFlashOverlay.raycastTarget = false;
            flash.SetActive(false);
        }

        private void EnsureBattleDialogueBox(RectTransform canvasRoot)
        {
            if (canvasRoot == null)
                return;

            Transform existing = canvasRoot.Find("BattleDialogueBox");
            _battleDialoguePanel = existing != null ? existing.gameObject : new GameObject("BattleDialogueBox", typeof(RectTransform), typeof(Image), typeof(Button));
            if (existing == null)
                _battleDialoguePanel.transform.SetParent(canvasRoot, false);

            var rect = _battleDialoguePanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.225f, 0.186f);
            rect.anchorMax = new Vector2(0.775f, 0.276f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            var image = _battleDialoguePanel.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/panel-dark");
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = new Color(0.025f, 0.026f, 0.040f, 0.90f);
            image.raycastTarget = true;

            var outline = _battleDialoguePanel.GetComponent<Outline>() ?? _battleDialoguePanel.AddComponent<Outline>();
            outline.effectColor = new Color(0.95f, 0.78f, 0.42f, 0.58f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);

            var button = _battleDialoguePanel.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.RemoveListener(AdvanceBattleDialogue);
            button.onClick.AddListener(AdvanceBattleDialogue);

            _battleDialogueText = EnsureDialogueText(_battleDialoguePanel.transform, "DialogueText",
                new Vector2(0.040f, 0.20f), new Vector2(0.860f, 0.84f), 19f, TextAlignmentOptions.Left);
            _battleDialoguePromptText = EnsureDialogueText(_battleDialoguePanel.transform, "Prompt",
                new Vector2(0.780f, 0.08f), new Vector2(0.955f, 0.30f), 12f, TextAlignmentOptions.Right);
            _battleDialoguePromptText.text = "ENTER / A";
            _battleDialoguePromptText.color = new Color(1f, 0.88f, 0.56f, 0.9f);

            _battleDialoguePanel.SetActive(false);
        }

        private TextMeshProUGUI EnsureDialogueText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float fontSize, TextAlignmentOptions alignment)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            if (existing == null)
                go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.color = new Color(0.98f, 0.96f, 0.91f, 1f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(13f, fontSize - 7f);
            text.fontSizeMax = fontSize;
            text.raycastTarget = false;
            return text;
        }

        private void QueueBattleDialogue(LogEntry entry)
        {
            if (!ShouldQueueBattleDialogue(entry))
                return;

            _battleDialogueQueue.Enqueue(entry);
            if (!_battleDialogueAwaitingAdvance)
                ShowNextBattleDialogue();
        }

        private void QueueBattleDialogueText(string message, LogEntryType type)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _battleDialogueQueue.Enqueue(new LogEntry { Message = message.Trim(), Type = type });
            if (!_battleDialogueAwaitingAdvance)
                ShowNextBattleDialogue();
        }

        private void QueueCombatResultDialogue(CombatResolution resolution)
        {
            if (resolution == null)
                return;

            string headline = resolution.WasCritical
                ? "SUPER EFFECTIVE!"
                : resolution.WasWeak
                    ? "Not very effective..."
                    : resolution.TargetDestroyed
                        ? "Direct hit!"
                        : "Hit!";

            string target = string.IsNullOrWhiteSpace(resolution.TargetName)
                ? "the target"
                : resolution.TargetName;
            string detail = resolution.HitInvoker
                ? $"{resolution.AttackerName} strikes the Invoker for {resolution.Damage}."
                : resolution.HitPillar
                    ? $"{resolution.AttackerName} hits {target} for {resolution.Damage}."
                    : $"{resolution.AttackerName} attacks {target} for {resolution.Damage}.";

            if (resolution.TargetDestroyed)
                detail += $" {target} falls.";

            QueueBattleDialogueText($"{headline}\n{detail}", LogEntryType.Combat);
        }

        private void ShowNextBattleDialogue()
        {
            if (_battleDialoguePanel == null || _battleDialogueText == null)
                return;

            if (_battleDialogueQueue.Count == 0)
            {
                _battleDialogueAwaitingAdvance = false;
                _battleDialoguePanel.SetActive(false);
                return;
            }

            LogEntry entry = _battleDialogueQueue.Dequeue();
            _battleDialogueText.text = FormatDialogueMessage(entry);
            _battleDialoguePanel.SetActive(true);
            _battleDialoguePanel.transform.SetAsLastSibling();
            _battleDialogueAwaitingAdvance = true;
        }

        private void AdvanceBattleDialogue()
        {
            if (!_battleDialogueAwaitingAdvance)
                return;

            _battleDialogueAwaitingAdvance = false;
            ShowNextBattleDialogue();
        }

        private static bool ShouldQueueBattleDialogue(LogEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Message))
                return false;

            return entry.Type == LogEntryType.Effect && IsSpecialEffectMessage(entry.Message);
        }

        private static string FormatDialogueMessage(LogEntry entry)
        {
            string message = entry.Message.Trim();
            return entry.Type switch
            {
                LogEntryType.Effect => message,
                LogEntryType.System => message,
                _ => message,
            };
        }

        private void HandleCombatResolved(CombatResolution resolution)
        {
            if (resolution.AttackerPlayer == LocalPlayer)
                _storyTutorialAttackSeen = true;

            Transform attackerContainer = resolution.AttackerPlayer == LocalPlayer ? p1FieldContainer : p2FieldContainer;

            Vector3 fromWorld = GetFieldWorldPoint(resolution.AttackerPlayer, resolution.AttackerIndex);
            Vector3 toWorld = GetTargetWorldPoint(resolution);
            Color impactColor = ResolveAttackEffectColor(resolution);
            Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.Attack, 0.72f, 0.92f);

            // When the AI is attacking the local player, show a dramatic incoming-attack banner
            bool isIncomingAttack = resolution.AttackerPlayer != LocalPlayer;
            if (isIncomingAttack)
                StartCoroutine(ShowIncomingAttackBanner(impactColor));

            // Resolve attacker element for flavoured animations
            Element attackerElement = Element.Flame; // default, overridden below
            var resolveState = _battle?.State;
            if (resolveState != null && resolution.AttackerPlayer >= 0 && resolution.AttackerPlayer < resolveState.Players.Length)
            {
                var aField = resolveState.Players[resolution.AttackerPlayer].Field;
                if (aField != null && resolution.AttackerIndex >= 0 && resolution.AttackerIndex < aField.Count)
                {
                    var card = aField[resolution.AttackerIndex]?.Card;
                    if (card != null)
                        attackerElement = card.element;
                }
            }
            Element capturedElement = attackerElement;
            string effectTag = ResolveAttackEffectTag(resolution);

            if (!string.IsNullOrEmpty(effectTag))
                SpawnFloatingCombatText(fromWorld + Vector3.up * 22f, effectTag, new Color(impactColor.r, impactColor.g, impactColor.b, 0.92f), 0.62f);

            // Lunge the attacker card toward target, trigger effects on impact
            PlayAttackLungeOnField(attackerContainer, resolution.AttackerIndex, toWorld, () =>
            {
                StartCoroutine(UIAnimUtils.HitPause(resolution.WasCritical ? 0.055f : resolution.WasWeak ? 0.020f : 0.030f));

                // Play impact sounds
                var sfxOnImpact = Audio.SfxManager.EnsureInstance();
                sfxOnImpact.Play(Audio.SfxCue.Hit, 0.80f, resolution.HitInvoker ? 0.82f : 0.95f);
                if (resolution.TargetDestroyed)
                    sfxOnImpact.Play(Audio.SfxCue.Destroy, 0.92f, 0.88f);

                SpawnImpactBurst(fromWorld, new Color(impactColor.r, impactColor.g, impactColor.b, 0.82f), resolution.WasCritical ? 0.92f : 0.72f);
                SpawnCombatBeam(fromWorld, toWorld, impactColor);
                SpawnElementalTrail(fromWorld, toWorld, impactColor);
                SpawnElementalAttackFX(fromWorld, toWorld, capturedElement);
                SpawnImpactBurst(toWorld, impactColor, resolution.TargetDestroyed ? 1.7f : 1.15f);
                SpawnAetherSheetImpactFX(toWorld, capturedElement, impactColor, resolution.TargetDestroyed ? 1.18f : 0.96f);
                SpawnElementalImpactPulse(toWorld, impactColor);
                SpawnImpactShards(
                    toWorld,
                    impactColor,
                    resolution.TargetDestroyed ? 14 : resolution.WasCritical ? 11 : 8,
                    resolution.HitPillar ? 102f : 78f,
                    resolution.TargetDestroyed ? 1.16f : resolution.WasCritical ? 1.06f : 0.94f);

                if (resolution.HitPillar && resolution.TargetDestroyed)
                {
                    SpawnFloatingCombatText(toWorld + Vector3.up * 38f, "SHATTER!", CombatPillarTint, 0.9f);
                    SpawnImpactBurst(toWorld, CombatPillarTint, 2.05f);
                    SpawnAetherSheetImpactFX(toWorld, BattleEffectAssetKind.Stone, CombatPillarTint, 1.55f);
                    SpawnImpactShards(toWorld, CombatPillarTint, 24, 148f, 1.32f);
                    if (_combatFlashOverlay != null)
                        StartCoroutine(UIAnimUtils.ScreenFlash(_combatFlashOverlay, new Color(1f, 0.82f, 0.32f, 0.34f), 0.14f));
                }

                if (resolution.WasCritical)
                {
                    SpawnFloatingCombatText(toWorld + Vector3.up * 34f, "CRITICAL!", CombatCritTint, 0.78f);
                    SpawnImpactShards(toWorld, CombatCritTint, 8, 118f, 1.22f);
                    if (_combatFlashOverlay != null)
                        StartCoroutine(UIAnimUtils.ScreenFlash(_combatFlashOverlay, new Color(1f, 0.82f, 0.22f, 0.52f), 0.16f));
                }
                else if (resolution.WasWeak)
                {
                    SpawnFloatingCombatText(toWorld + Vector3.up * 28f, "GLANCING", CombatWeakTint, 0.68f);
                }

                if (resolution.HitInvoker)
                {
                    SpawnFloatingCombatText(toWorld, $"-{resolution.Damage} to INVOKER", CombatHitTint, 1.4f);
                    if (_combatFlashOverlay != null)
                        StartCoroutine(UIAnimUtils.ScreenFlash(_combatFlashOverlay, new Color(0.92f, 0.14f, 0.14f, 0.9f), 0.28f));
                    StartCoroutine(UIAnimUtils.ScreenShake(transform, 14f, 0.22f));
                }
                else if (resolution.HitPillar)
                {
                    string pillarOutcome = resolution.TargetDestroyed
                        ? $"{resolution.TargetName} DESTROYED!"
                        : $"-{resolution.Damage} to {resolution.TargetName}";
                    SpawnFloatingCombatText(toWorld, pillarOutcome, CombatPillarTint, resolution.TargetDestroyed ? 1.25f : 1.05f);
                    StartCoroutine(UIAnimUtils.ScreenShake(transform, resolution.TargetDestroyed ? 10f : 6f, 0.16f));
                    if (resolution.PillarIntercepted)
                        SpawnFloatingCombatText(toWorld + Vector3.up * 28f, "PILLAR INTERCEPTS!", new Color(1f, 0.85f, 0.35f), 0.9f);
                }
                else
                {
                    string hitOutcome = resolution.TargetDestroyed
                        ? $"{resolution.TargetName} DESTROYED!"
                        : $"-{resolution.Damage}";
                    SpawnFloatingCombatText(toWorld, hitOutcome, CombatHitTint, resolution.TargetDestroyed ? 1.28f : 1f);
                    if (resolution.TargetDestroyed)
                    {
                        if (_combatFlashOverlay != null)
                            StartCoroutine(UIAnimUtils.ScreenFlash(_combatFlashOverlay, new Color(impactColor.r, impactColor.g, impactColor.b, 0.65f), 0.18f));
                        StartCoroutine(UIAnimUtils.ScreenShake(transform, 6f, 0.14f));
                    }
                }

                if (resolution.AttackerDestroyed)
                {
                    SpawnImpactBurst(fromWorld, new Color(0.76f, 0.46f, 0.88f, 0.92f), 1.1f);
                    SpawnImpactShards(fromWorld, new Color(0.82f, 0.6f, 1f, 0.92f), 7, 64f, 0.86f);
                    SpawnFloatingCombatText(fromWorld + Vector3.up * 18f, $"{resolution.AttackerName} FALLS!", new Color(0.82f, 0.6f, 1f), 0.9f);
                }

                QueueCombatResultDialogue(resolution);
            });
        }

        private IEnumerator ShowIncomingAttackBanner(Color attackColor)
        {
            // Flash a pulsing red-tinted border before the lunge impact so the player knows they are being attacked.
            var canvasTransform = (transform as RectTransform) ?? transform;

            var bannerGO = new GameObject("IncomingAttackBanner");
            bannerGO.transform.SetParent(canvasTransform, false);
            var bannerRT = bannerGO.AddComponent<RectTransform>();
            bannerRT.anchorMin = Vector2.zero;
            bannerRT.anchorMax = Vector2.one;
            bannerRT.offsetMin = Vector2.zero;
            bannerRT.offsetMax = Vector2.zero;
            bannerRT.SetAsLastSibling();

            // Semi-transparent red vignette tinted to the attacker's element
            var bannerImg = bannerGO.AddComponent<Image>();
            Color warningColor = Color.Lerp(new Color(0.92f, 0.12f, 0.12f), attackColor, 0.35f);
            bannerImg.color = new Color(warningColor.r, warningColor.g, warningColor.b, 0f);
            bannerImg.raycastTarget = false;

            // "INCOMING ATTACK" label centred on screen
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(bannerGO.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0.2f, 0.40f);
            labelRT.anchorMax = new Vector2(0.8f, 0.60f);
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
            labelTMP.text = "INCOMING ATTACK!";
            labelTMP.fontSize = 52;
            labelTMP.fontStyle = FontStyles.Bold;
            labelTMP.color = new Color(1f, 0.92f, 0.88f, 0f);
            labelTMP.alignment = TextAlignmentOptions.Center;
            labelTMP.raycastTarget = false;

            // Fade in over 0.18s, hold briefly, then fade out
            float fadeIn = 0.18f, hold = 0.28f, fadeOut = 0.22f;
            float t = 0f;

            while (t < fadeIn)
            {
                t += Time.deltaTime;
                float alpha = Mathf.Clamp01(t / fadeIn);
                bannerImg.color = new Color(warningColor.r, warningColor.g, warningColor.b, alpha * 0.45f);
                labelTMP.color = new Color(1f, 0.92f, 0.88f, alpha);
                yield return null;
            }

            t = 0f;
            while (t < hold)
            {
                // Pulse the border alpha
                float pulse = 0.45f + Mathf.Sin(t * Mathf.PI * 6f) * 0.08f;
                bannerImg.color = new Color(warningColor.r, warningColor.g, warningColor.b, pulse);
                t += Time.deltaTime;
                yield return null;
            }

            t = 0f;
            while (t < fadeOut)
            {
                t += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(t / fadeOut);
                bannerImg.color = new Color(warningColor.r, warningColor.g, warningColor.b, alpha * 0.45f);
                labelTMP.color = new Color(1f, 0.92f, 0.88f, alpha);
                yield return null;
            }

            Destroy(bannerGO);
        }

        private Color ResolveAttackEffectColor(CombatResolution resolution)
        {
            if (resolution.HitPillar)
                return CombatPillarTint;

            var state = _battle?.State;
            if (state == null || resolution.AttackerPlayer < 0 || resolution.AttackerPlayer >= state.Players.Length)
                return CombatHitTint;

            var field = state.Players[resolution.AttackerPlayer].Field;
            if (field == null || resolution.AttackerIndex < 0 || resolution.AttackerIndex >= field.Count)
                return CombatHitTint;

            var daemon = field[resolution.AttackerIndex];
            if (daemon?.Card == null)
                return CombatHitTint;

            Color elementTint = CardVisual.GetElementColor(daemon.Card.element);
            return Color.Lerp(CombatHitTint, elementTint, 0.62f);
        }

        private string ResolveAttackEffectTag(CombatResolution resolution)
        {
            var state = _battle?.State;
            if (state == null || resolution.AttackerPlayer < 0 || resolution.AttackerPlayer >= state.Players.Length)
                return string.Empty;

            var field = state.Players[resolution.AttackerPlayer].Field;
            if (field == null || resolution.AttackerIndex < 0 || resolution.AttackerIndex >= field.Count)
                return string.Empty;

            var daemon = field[resolution.AttackerIndex];
            if (daemon?.Card == null)
                return string.Empty;

            return daemon.Card.element switch
            {
                Element.Flame => "FLAME",
                Element.Ice => "FROST",
                Element.Water => "TIDE",
                Element.Earth => "STONE",
                Element.Air => "GALE",
                Element.Light => "RADIANT",
                Element.Dark => "SHADOW",
                Element.Nature => "WILD",
                _ => "ARCANE",
            };
        }

        /// <summary>Lunge the attacker card toward the target, then fire impact callback.</summary>
        private void PlayAttackLungeOnField(Transform fieldContainer, int index, Vector3 targetWorld, Action onImpact)
        {
            if (fieldContainer == null || index < 0 || index >= fieldContainer.childCount)
            {
                onImpact?.Invoke();
                return;
            }
            Transform slot = fieldContainer.GetChild(index);
            Transform card = FindSlotCard(slot);
            var go = (card ?? slot).gameObject;
            var visual = go.GetComponent<CardVisual>();
            if (visual != null)
                visual.PlayLungeToward(targetWorld, onImpact);
            else
            {
                StartCoroutine(UIAnimUtils.ClickBounce(go.transform, 0.88f, 0.16f));
                onImpact?.Invoke();
            }
        }

        private Vector3 GetFieldWorldPoint(int playerIndex, int slotIndex)
        {
            Transform container = playerIndex == LocalPlayer ? p1FieldContainer : p2FieldContainer;
            return GetSocketWorldPoint(container, slotIndex);
        }

        private Vector3 GetTargetWorldPoint(CombatResolution resolution)
        {
            // Pillar intercepted a Invoker attack — animate toward the pillar stack instead
            if (resolution.PillarIntercepted)
            {
                Transform pillarC = resolution.TargetPlayer == LocalPlayer ? p1PillarContainer : p2PillarContainer;
                return GetSocketWorldPoint(pillarC, 0);
            }

            return resolution.TargetType switch
            {
                TargetType.Daemon => GetSocketWorldPoint(resolution.TargetPlayer == LocalPlayer ? p1FieldContainer : p2FieldContainer, resolution.TargetIndex),
                TargetType.Pillar => GetSocketWorldPoint(resolution.TargetPlayer == LocalPlayer ? p1PillarContainer : p2PillarContainer, 0),
                TargetType.Invoker => GetRectWorldPoint(FindInvokerCardRect(resolution.TargetPlayer)),
                _ => Vector3.zero,
            };
        }

        private Vector3 GetSocketWorldPoint(Transform container, int slotIndex)
        {
            RectTransform rect = GetSocketTargetRect(container, slotIndex);
            return GetRectWorldPoint(rect);
        }

        private RectTransform GetSocketTargetRect(Transform container, int slotIndex)
        {
            if (container == null || slotIndex < 0 || slotIndex >= container.childCount)
                return null;

            Transform slot = container.GetChild(slotIndex);
            Transform card = FindSlotCard(slot);
            if (card is RectTransform cardRect)
                return cardRect;

            Transform content = FindSlotContent(slot);
            return content as RectTransform ?? slot as RectTransform;
        }

        private static Transform FindSlotContent(Transform slot)
        {
            if (slot == null)
                return null;

            return slot.Find("Seat/InnerSeat/Content")
                ?? slot.Find("Content")
                ?? slot.Find("Seat/Content")
                ?? slot;
        }

        private static Transform FindSlotCard(Transform slot)
        {
            if (slot == null)
                return null;

            return slot.Find("Seat/InnerSeat/Content/Card")
                ?? slot.Find("Content/Card")
                ?? slot.Find("Seat/Content/Card")
                ?? slot.Find("Card");
        }

        private RectTransform FindInvokerCardRect(int playerIndex)
        {
            RectTransform zone = FindRootRect(playerIndex == LocalPlayer ? "P1InvokerZone" : "P2InvokerZone");
            return zone != null ? zone.Find("RuntimeInvokerAnchor/Card") as RectTransform : null;
        }

        private Vector3 GetRectWorldPoint(RectTransform rect)
        {
            return rect != null ? rect.TransformPoint(rect.rect.center) : Vector3.zero;
        }

        private void SpawnCombatBeam(Vector3 fromWorld, Vector3 toWorld, Color color)
        {
            if (_combatFxLayer == null)
                return;

            if (!WorldToFxPoint(fromWorld, out Vector2 fromLocal) || !WorldToFxPoint(toWorld, out Vector2 toLocal))
                return;

            Vector2 delta = toLocal - fromLocal;
            if (delta.sqrMagnitude < 1f)
                return;

            Vector2 dir = delta.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Color glowColor = new Color(color.r, color.g, color.b, 0.28f);
            Color coreColor = Color.Lerp(color, Color.white, 0.35f);
            coreColor.a = 0.92f;
            Color slashColor = Color.Lerp(color, Color.white, 0.52f);
            slashColor.a = 0.72f;

            SpawnBeamLayer("CombatBeamGlow", fromLocal, toLocal, 1.04f, 18f, 0f, Vector2.zero, glowColor, 0.24f, 0.84f);
            SpawnBeamLayer("CombatBeamMid", fromLocal, toLocal, 0.98f, 9f, 0f, Vector2.zero, color, 0.20f, 0.68f);
            SpawnBeamLayer("CombatBeamCore", fromLocal, toLocal, 0.84f, 4f, 0f, Vector2.zero, coreColor, 0.16f, 0.52f);
            SpawnBeamLayer("CombatBeamSlashA", fromLocal, toLocal, 0.62f, 5f, 8f, perp * 6f, slashColor, 0.17f, 0.44f);
            SpawnBeamLayer("CombatBeamSlashB", fromLocal, toLocal, 0.56f, 4f, -9f, -perp * 7f, slashColor, 0.17f, 0.40f);
        }

        private void SpawnElementalTrail(Vector3 fromWorld, Vector3 toWorld, Color color)
        {
            if (_combatFxLayer == null)
                return;

            if (!WorldToFxPoint(fromWorld, out Vector2 fromLocal) || !WorldToFxPoint(toWorld, out Vector2 toLocal))
                return;

            Vector2 delta = toLocal - fromLocal;
            if (delta.sqrMagnitude < 1f)
                return;

            Vector2 dir = delta.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            int streakCount = 5;
            for (int i = 0; i < streakCount; i++)
            {
                var streak = new GameObject("ElementalTrail", typeof(RectTransform), typeof(Image));
                streak.transform.SetParent(_combatFxLayer, false);
                var rect = streak.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);

                float along = Mathf.Lerp(0.18f, 0.86f, i / (float)Mathf.Max(1, streakCount - 1));
                float spread = UnityEngine.Random.Range(-16f, 16f);
                float length = Mathf.Max(48f, delta.magnitude * UnityEngine.Random.Range(0.24f, 0.48f));
                rect.sizeDelta = new Vector2(length, UnityEngine.Random.Range(4.5f, 9.5f));
                rect.anchoredPosition = Vector2.Lerp(fromLocal, toLocal, along) + perp * spread;
                rect.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + UnityEngine.Random.Range(-18f, 18f));

                var image = streak.GetComponent<Image>();
                image.sprite = Resources.Load<Sprite>("UI/panel-dark");
                image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = new Color(color.r, color.g, color.b, Mathf.Lerp(0.56f, 0.18f, i / (float)Mathf.Max(1, streakCount - 1)));
                image.raycastTarget = false;

                StartCoroutine(FadeAndShrink(rect, image, 0.14f + i * 0.04f, 0.46f));
            }
        }

        private void SpawnBeamLayer(
            string name,
            Vector2 fromLocal,
            Vector2 toLocal,
            float lengthScale,
            float thickness,
            float angleOffset,
            Vector2 centerOffset,
            Color color,
            float duration,
            float endScaleFactor)
        {
            var beam = new GameObject(name, typeof(RectTransform), typeof(Image));
            beam.transform.SetParent(_combatFxLayer, false);
            var rect = beam.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);

            Vector2 delta = toLocal - fromLocal;
            float length = Mathf.Max(40f, delta.magnitude * lengthScale);
            rect.sizeDelta = new Vector2(length, thickness);
            rect.anchoredPosition = (fromLocal + toLocal) * 0.5f + centerOffset;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + angleOffset);

            var image = beam.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/panel-dark");
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;

            StartCoroutine(FadeAndShrink(rect, image, duration, endScaleFactor));
        }

        /// <summary>
        /// Element-specific attack visual: fires a cascade of themed particles along the attack path.
        /// Fire → floating ember squares  |  Ice → sharp diamond shards  |  Water → ripple arcs
        /// Earth → slow heavy stones  |  Air → fast thin streaks  |  Nature → spore blobs
        /// Light → star flares  |  Dark → shadow wisps  |  Neutral/fallback → plain sparks
        /// </summary>
        private void SpawnElementalAttackFX(Vector3 fromWorld, Vector3 toWorld, Element element)
        {
            if (TrySpawnAetherSheetAttackFX(fromWorld, toWorld, element))
                return;

            if (_combatFxLayer == null) return;
            if (!WorldToFxPoint(fromWorld, out Vector2 fromLocal) || !WorldToFxPoint(toWorld, out Vector2 toLocal))
                return;

            Vector2 dir = (toLocal - fromLocal).normalized;
            float dist = (toLocal - fromLocal).magnitude;

            // Per-element visual parameters
            int count;
            Color baseColor;
            Vector2 sizeMin;
            Vector2 sizeMax;
            float rotBase;
            float speedMin, speedMax;
            float fadeTime;

            switch (element)
            {
                case Element.Flame:
                    count = 9; baseColor = new Color(1f, 0.42f, 0.06f);
                    sizeMin = new Vector2(10f, 10f); sizeMax = new Vector2(22f, 22f);
                    rotBase = 45f; speedMin = 0.14f; speedMax = 0.24f; fadeTime = 0.32f;
                    break;
                case Element.Ice:
                    count = 7; baseColor = new Color(0.56f, 0.86f, 1f);
                    sizeMin = new Vector2(8f, 14f); sizeMax = new Vector2(16f, 26f);
                    rotBase = 0f; speedMin = 0.16f; speedMax = 0.22f; fadeTime = 0.28f;
                    break;
                case Element.Water:
                    count = 6; baseColor = new Color(0.18f, 0.56f, 1f);
                    sizeMin = new Vector2(14f, 8f); sizeMax = new Vector2(30f, 12f);
                    rotBase = 0f; speedMin = 0.18f; speedMax = 0.26f; fadeTime = 0.30f;
                    break;
                case Element.Earth:
                    count = 5; baseColor = new Color(0.62f, 0.42f, 0.18f);
                    sizeMin = new Vector2(14f, 14f); sizeMax = new Vector2(28f, 28f);
                    rotBase = 30f; speedMin = 0.22f; speedMax = 0.32f; fadeTime = 0.38f;
                    break;
                case Element.Air:
                    count = 10; baseColor = new Color(0.82f, 0.96f, 1f);
                    sizeMin = new Vector2(6f, 4f); sizeMax = new Vector2(32f, 6f);
                    rotBase = 0f; speedMin = 0.10f; speedMax = 0.18f; fadeTime = 0.22f;
                    break;
                case Element.Nature:
                    count = 8; baseColor = new Color(0.28f, 0.82f, 0.22f);
                    sizeMin = new Vector2(10f, 10f); sizeMax = new Vector2(20f, 20f);
                    rotBase = 0f; speedMin = 0.18f; speedMax = 0.28f; fadeTime = 0.36f;
                    break;
                case Element.Light:
                    count = 10; baseColor = new Color(1f, 0.96f, 0.60f);
                    sizeMin = new Vector2(8f, 8f); sizeMax = new Vector2(18f, 18f);
                    rotBase = 45f; speedMin = 0.12f; speedMax = 0.20f; fadeTime = 0.26f;
                    break;
                case Element.Dark:
                    count = 8; baseColor = new Color(0.46f, 0.14f, 0.72f);
                    sizeMin = new Vector2(10f, 16f); sizeMax = new Vector2(18f, 28f);
                    rotBase = 20f; speedMin = 0.16f; speedMax = 0.24f; fadeTime = 0.34f;
                    break;
                default: // Neutral/Arcane
                    count = 6; baseColor = new Color(0.78f, 0.78f, 0.82f);
                    sizeMin = new Vector2(8f, 8f); sizeMax = new Vector2(16f, 16f);
                    rotBase = 0f; speedMin = 0.14f; speedMax = 0.22f; fadeTime = 0.28f;
                    break;
            }

            var rng = new System.Random();
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Mathf.Max(1, count - 1);
                Vector2 basePos = Vector2.Lerp(fromLocal, toLocal, t * 0.85f);

                // Perpendicular scatter
                Vector2 perp = new Vector2(-dir.y, dir.x);
                float scatter = ((float)rng.NextDouble() - 0.5f) * Mathf.Min(60f, dist * 0.22f);
                Vector2 spawnPos = basePos + perp * scatter;

                float w = Mathf.Lerp(sizeMin.x, sizeMax.x, (float)rng.NextDouble());
                float h = Mathf.Lerp(sizeMin.y, sizeMax.y, (float)rng.NextDouble());
                float rot = rotBase + (float)(rng.NextDouble() - 0.5) * 60f;
                float delay = (float)rng.NextDouble() * 0.12f;
                float dur = Mathf.Lerp(speedMin, speedMax, (float)rng.NextDouble()) + delay;

                var go = new GameObject("ElemFX", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_combatFxLayer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = spawnPos;
                rt.sizeDelta = new Vector2(w, h);
                rt.localRotation = Quaternion.Euler(0f, 0f, rot);

                var img = go.GetComponent<Image>();
                img.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.80f);
                img.raycastTarget = false;

                // Upward drift offset varies by element
                float driftY = element == Element.Earth ? -18f :
                               element == Element.Dark  ? -10f : 22f + (float)rng.NextDouble() * 16f;
                StartCoroutine(ElemFXParticle(go, driftY, dur, fadeTime, delay));
            }
        }

        private bool TrySpawnAetherSheetAttackFX(Vector3 fromWorld, Vector3 toWorld, Element element)
        {
            if (_combatFxLayer == null)
                return false;

            BattleEffectAssetKind kind = ResolveAetherEffectKind(element);
            if (!BattleEffectSpriteLibrary.TryGetFrames(kind, out Sprite[] frames))
                return false;

            if (!WorldToFxPoint(fromWorld, out Vector2 fromLocal) || !WorldToFxPoint(toWorld, out Vector2 toLocal))
                return false;

            Vector2 delta = toLocal - fromLocal;
            if (delta.sqrMagnitude < 1f)
                return false;

            Vector2 dir = delta.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float baseSize = ResolveAetherSheetSize(kind);
            float travelScale = Mathf.Clamp(delta.magnitude / 520f, 0.78f, 1.22f);

            SpawnAetherSheetFx(
                "AetherAttackFX",
                frames,
                Vector2.Lerp(fromLocal, toLocal, 0.66f) + perp * UnityEngine.Random.Range(-18f, 18f),
                baseSize * travelScale,
                0.42f,
                angle,
                dir * UnityEngine.Random.Range(28f, 58f) + perp * UnityEngine.Random.Range(-12f, 12f),
                1.08f);

            if (delta.magnitude > 190f)
            {
                SpawnAetherSheetFx(
                    "AetherAttackEchoFX",
                    frames,
                    Vector2.Lerp(fromLocal, toLocal, 0.88f) + perp * UnityEngine.Random.Range(-12f, 12f),
                    baseSize * 0.78f * travelScale,
                    0.34f,
                    angle + UnityEngine.Random.Range(-10f, 10f),
                    dir * UnityEngine.Random.Range(16f, 34f),
                    0.94f);
            }

            return true;
        }

        private void SpawnAetherSheetImpactFX(Vector3 worldPos, Element element, Color fallbackColor, float scale)
        {
            SpawnAetherSheetImpactFX(worldPos, ResolveAetherEffectKind(element), fallbackColor, scale);
        }

        private void SpawnAetherSheetImpactFX(Vector3 worldPos, BattleEffectAssetKind kind, Color fallbackColor, float scale)
        {
            if (_combatFxLayer == null)
                return;

            if (!BattleEffectSpriteLibrary.TryGetFrames(kind, out Sprite[] frames))
                return;

            if (!WorldToFxPoint(worldPos, out Vector2 localPos))
                return;

            SpawnAetherSheetFx(
                "AetherImpactFX",
                frames,
                localPos,
                ResolveAetherSheetSize(kind) * 1.08f * scale,
                0.46f,
                UnityEngine.Random.Range(-8f, 8f),
                UnityEngine.Random.insideUnitCircle * 12f,
                1.18f);
        }

        private void SpawnAetherSheetFx(
            string name,
            Sprite[] frames,
            Vector2 localPos,
            float size,
            float duration,
            float rotation,
            Vector2 drift,
            float maxScale)
        {
            if (_combatFxLayer == null || frames == null || frames.Length == 0)
                return;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_combatFxLayer, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = localPos;
            rect.sizeDelta = new Vector2(size, size);
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            rect.localScale = Vector3.one * 0.72f;

            var image = go.GetComponent<Image>();
            image.sprite = frames[0];
            image.preserveAspect = true;
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = false;

            StartCoroutine(PlayAetherSheetFx(rect, image, frames, duration, drift, maxScale));
        }

        private IEnumerator PlayAetherSheetFx(
            RectTransform rect,
            Image image,
            Sprite[] frames,
            float duration,
            Vector2 drift,
            float maxScale)
        {
            if (rect == null || image == null || frames == null || frames.Length == 0)
                yield break;

            Vector2 startPos = rect.anchoredPosition;
            float t = 0f;

            while (t < duration)
            {
                if (rect == null || image == null)
                    yield break;

                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));
                int frameIndex = Mathf.Clamp(Mathf.FloorToInt(p * frames.Length), 0, frames.Length - 1);
                image.sprite = frames[frameIndex];

                float eased = UIAnimUtils.EaseOutQuad(p);
                float fadeIn = Mathf.Clamp01(p / 0.12f);
                float fadeOut = 1f - Mathf.Clamp01((p - 0.72f) / 0.28f);
                float alpha = Mathf.Min(fadeIn, fadeOut);
                image.color = new Color(1f, 1f, 1f, alpha * 0.96f);

                rect.anchoredPosition = startPos + drift * eased;
                rect.localScale = Vector3.one * Mathf.Lerp(0.72f, maxScale, Mathf.Sin(p * Mathf.PI * 0.72f));
                yield return null;
            }

            if (rect != null)
                Destroy(rect.gameObject);
        }

        private static BattleEffectAssetKind ResolveAetherEffectKind(Element element)
        {
            switch (element)
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

        private static float ResolveAetherSheetSize(BattleEffectAssetKind kind)
        {
            switch (kind)
            {
                case BattleEffectAssetKind.Flame:
                    return 188f;
                case BattleEffectAssetKind.Water:
                    return 172f;
                case BattleEffectAssetKind.Storm:
                    return 184f;
                case BattleEffectAssetKind.Shadow:
                    return 176f;
                case BattleEffectAssetKind.Radiant:
                    return 174f;
                case BattleEffectAssetKind.Frost:
                    return 178f;
                case BattleEffectAssetKind.Stone:
                    return 206f;
                case BattleEffectAssetKind.Gale:
                    return 180f;
                case BattleEffectAssetKind.Verdant:
                    return 164f;
                case BattleEffectAssetKind.Bind:
                    return 176f;
                default:
                    return 172f;
            }
        }

        private IEnumerator ElemFXParticle(GameObject go, float driftY, float travelTime, float fadeTime, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            if (go == null) yield break;

            var rt = go.GetComponent<RectTransform>();
            var img = go.GetComponent<Image>();
            if (rt == null || img == null) { Destroy(go); yield break; }

            Vector2 startPos = rt.anchoredPosition;
            Color startColor = img.color;
            float t = 0f;

            while (t < travelTime)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / travelTime);
                rt.anchoredPosition = startPos + new Vector2(0f, driftY * p);
                rt.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.4f, p);
                yield return null;
            }

            t = 0f;
            while (t < fadeTime)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / fadeTime);
                if (img != null)
                    img.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(startColor.a, 0f, p));
                yield return null;
            }

            if (go != null) Destroy(go);
        }

        private void SpawnElementalImpactPulse(Vector3 worldPos, Color color)
        {
            if (_combatFxLayer == null || !WorldToFxPoint(worldPos, out Vector2 localPos))
                return;

            for (int i = 0; i < 2; i++)
            {
                var ring = new GameObject("ElementalImpactPulse", typeof(RectTransform), typeof(Image));
                ring.transform.SetParent(_combatFxLayer, false);
                var rect = ring.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = localPos;
                rect.sizeDelta = new Vector2(84f + i * 26f, 84f + i * 26f);

                var image = ring.GetComponent<Image>();
                image.sprite = Resources.Load<Sprite>("UI/panel-dark");
                image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = new Color(color.r, color.g, color.b, 0.34f - i * 0.10f);
                image.raycastTarget = false;

                StartCoroutine(UIAnimUtils.RadialBurst(rect, 1.35f + i * 0.2f, 0.20f + i * 0.05f));
                StartCoroutine(DestroyAfter(ring, 0.26f + i * 0.05f));
            }
        }

        private void SpawnImpactBurst(Vector3 worldPos, Color color, float maxScale)
        {
            if (_combatFxLayer == null || !WorldToFxPoint(worldPos, out Vector2 localPos))
                return;

            var burst = new GameObject("ImpactBurst", typeof(RectTransform), typeof(Image));
            burst.transform.SetParent(_combatFxLayer, false);
            var rect = burst.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = localPos;
            rect.sizeDelta = new Vector2(108f, 108f);

            var image = burst.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/panel-dark");
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = new Color(color.r, color.g, color.b, 0.4f);
            image.raycastTarget = false;

            StartCoroutine(UIAnimUtils.RadialBurst(rect, maxScale, 0.24f));
            StartCoroutine(DestroyAfter(burst, 0.28f));
        }

        private void SpawnImpactShards(Vector3 worldPos, Color color, int count, float spread, float intensity)
        {
            if (_combatFxLayer == null || !WorldToFxPoint(worldPos, out Vector2 localPos))
                return;

            for (int i = 0; i < count; i++)
            {
                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                var shard = new GameObject("ImpactShard", typeof(RectTransform), typeof(Image));
                shard.transform.SetParent(_combatFxLayer, false);
                var rect = shard.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = localPos + dir * UnityEngine.Random.Range(8f, 20f);
                rect.sizeDelta = new Vector2(
                    UnityEngine.Random.Range(14f, 28f) * intensity,
                    UnityEngine.Random.Range(3.5f, 7.5f) * Mathf.Lerp(0.92f, 1.18f, Mathf.Clamp01(intensity - 0.8f)));
                rect.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);

                var image = shard.GetComponent<Image>();
                image.sprite = Resources.Load<Sprite>("UI/panel-dark");
                image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = new Color(color.r, color.g, color.b, UnityEngine.Random.Range(0.54f, 0.92f));
                image.raycastTarget = false;

                Vector2 travel = dir * UnityEngine.Random.Range(spread * 0.55f, spread);
                float duration = UnityEngine.Random.Range(0.18f, 0.30f);
                float endScale = UnityEngine.Random.Range(0.22f, 0.42f);
                float spin = UnityEngine.Random.Range(-96f, 96f);
                StartCoroutine(ScatterAndFade(rect, image, travel, duration, endScale, spin));
            }
        }

        private IEnumerator ScatterAndFade(RectTransform rect, Image image, Vector2 travel, float duration, float endScaleFactor, float spinDegrees)
        {
            if (rect == null || image == null)
                yield break;

            Vector2 startPos = rect.anchoredPosition;
            Vector3 startScale = rect.localScale;
            float startAngle = rect.localEulerAngles.z;
            float startAlpha = image.color.a;
            float t = 0f;

            while (t < duration)
            {
                if (rect == null || image == null)
                    yield break;

                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                float eased = UIAnimUtils.EaseOutQuad(p);
                rect.anchoredPosition = startPos + travel * eased;
                rect.localScale = Vector3.Lerp(startScale, startScale * endScaleFactor, p);
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(startAngle, startAngle + spinDegrees, eased));

                Color tint = image.color;
                image.color = new Color(tint.r, tint.g, tint.b, Mathf.Lerp(startAlpha, 0f, p));
                yield return null;
            }

            if (rect != null)
                Destroy(rect.gameObject);
        }

        private void SpawnFloatingCombatText(Vector3 worldPos, string text, Color color, float scale)
        {
            if (_combatTextLayer == null || !WorldToFxPoint(worldPos, out Vector2 localPos))
                return;

            var go = new GameObject("CombatText", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_combatTextLayer, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = localPos;
            rect.sizeDelta = new Vector2(420f, 96f);

            var textLabel = go.AddComponent<TextMeshProUGUI>();
            textLabel.text = text;
            textLabel.fontSize = 34f * scale;
            textLabel.fontStyle = FontStyles.Bold;
            textLabel.alignment = TextAlignmentOptions.Center;
            textLabel.color = color;
            textLabel.textWrappingMode = TextWrappingModes.NoWrap;
            textLabel.outlineWidth = 0.24f;
            textLabel.outlineColor = new Color(0f, 0f, 0f, 0.82f);
            textLabel.raycastTarget = false;
            StartCoroutine(UIAnimUtils.FloatingText(textLabel, 110f, 0.9f));
        }

        private bool WorldToFxPoint(Vector3 worldPoint, out Vector2 localPoint)
        {
            if (_combatFxLayer == null)
            {
                localPoint = Vector2.zero;
                return false;
            }

            Camera cam = null;
            Canvas canvas = _combatFxLayer.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _combatFxLayer,
                RectTransformUtility.WorldToScreenPoint(cam, worldPoint),
                cam,
                out localPoint);
        }

        private IEnumerator FadeAndShrink(RectTransform rect, Image image, float duration, float endScaleFactor = 0.65f)
        {
            Vector3 startScale = rect.localScale;
            float startAlpha = image != null ? image.color.a : 1f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                rect.localScale = Vector3.Lerp(startScale, startScale * endScaleFactor, p);
                if (image != null)
                {
                    Color tint = image.color;
                    image.color = new Color(tint.r, tint.g, tint.b, Mathf.Lerp(startAlpha, 0f, p));
                }
                yield return null;
            }

            Destroy(rect.gameObject);
        }

        private IEnumerator DestroyAfter(GameObject go, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (go != null)
                Destroy(go);
        }

        // ─── Game Loop Driver ────────────────────────────
        private void OnStateChanged(GameState state)
        {
            // Debounce: batch multiple state changes within the same frame
            if (!_refreshScheduled)
            {
                _refreshScheduled = true;
                StartCoroutine(DeferredRefresh());
            }
        }

        private IEnumerator DeferredRefresh()
        {
            yield return null; // wait one frame to batch changes
            _refreshScheduled = false;
            if (_battle != null)
            {
                RefreshUI(_battle.State);
            }
        }

        /// <summary>Opening sequence: roll for first player, shuffle, deal, roll SE.</summary>
        private IEnumerator OpeningSequence()
        {
            Debug.Log("[Battle] OpeningSequence begin");
            // Suppress the HandleSERolled dice animation during the opening —
            // we handle the dice visuals manually in this coroutine.
            _suppressSERolledVisual = true;

            // Hide action buttons during setup
            if (nextPhaseButton) nextPhaseButton.gameObject.SetActive(false);
            if (endTurnButton) endTurnButton.interactable = false;

            // Keep startup deterministic. The animated dice intro can stall the first rendered
            // frame in standalone builds, which leaves the player trapped behind the Unity splash.
            var (quickR1, quickR2) = _battle.CompleteSetup();
            Debug.Log($"[Battle] CompleteSetup rolls p1={quickR1} p2={quickR2}");
            AddLogEntry(new LogEntry { Message = $"Player 1 rolls {quickR1}, Player 2 rolls {quickR2}!", Type = LogEntryType.System });
            string quickFirstName = _battle.State.Players[_battle.State.CurrentPlayer].Name;
            AddLogEntry(new LogEntry { Message = $"{quickFirstName} goes first!", Type = LogEntryType.System });
            int quickSeTotal = _battle.State.Players[_battle.State.CurrentPlayer].Will;
            AddLogEntry(new LogEntry { Message = $"{quickFirstName} stores {quickSeTotal} SE after the opening roll.", Type = LogEntryType.System });
            _suppressSERolledVisual = false;
            if (endTurnButton) endTurnButton.interactable = true;
            RefreshUI(_battle.State);
            Debug.Log("[Battle] OpeningSequence setup complete");

            if (_battle.State.CurrentPlayer == AIPlayerIndex)
                StartCoroutine(RunAITurn());
            else
                StartCoroutine(AutoDrawCoroutine());

            LogHybridDiagnosticsSnapshot();
            LogRenderDiagnostics();
            yield break;

            if (_storyBattleConfig != null)
                yield return PlayStoryBattleIntroBanner();
            else
                yield return new WaitForSecondsRealtime(0.25f);

            // Roll for first player (both roll 12d)
            var (r1, r2) = _battle.CompleteSetup();
            Debug.Log($"[Battle] CompleteSetup rolls p1={r1} p2={r2}");
            AddLogEntry(new LogEntry { Message = $"Player 1 rolls {r1}, Player 2 rolls {r2}!", Type = LogEntryType.System });

            // Animate dice if available
            if (diceRoller != null)
            {
                ActivateDiceArea();
                bool diceFinished = false;
                int higherRoll = Mathf.Max(r1, r2);
                Debug.Log($"[Battle] OpeningSequence: rolling dice to {higherRoll}");
                diceRoller.RollToValue(higherRoll, () => diceFinished = true);
                float diceDeadline = Time.realtimeSinceStartup + 3.0f;
                while (!diceFinished && Time.realtimeSinceStartup < diceDeadline) yield return null;
                if (!diceFinished)
                    Debug.LogWarning("[Battle] OpeningSequence: dice roll timed out; continuing setup.");
                Debug.Log("[Battle] OpeningSequence: dice callback received, hiding dice");
                yield return new WaitForSecondsRealtime(0.35f);
                DeactivateDiceArea();

                // Now show the SE roll that just happened
                int se = _battle.LastSERoll;
                if (se > 0)
                {
                    yield return new WaitForSecondsRealtime(0.18f);
                    ActivateDiceArea();
                    diceFinished = false;
                    diceRoller.RollToValue(Mathf.Clamp(se, 1, GameConstants.SEDiceSides), () => diceFinished = true);
                    diceDeadline = Time.realtimeSinceStartup + 3.0f;
                    while (!diceFinished && Time.realtimeSinceStartup < diceDeadline) yield return null;
                    if (!diceFinished)
                        Debug.LogWarning("[Battle] OpeningSequence: SE dice roll timed out; continuing setup.");
                    yield return new WaitForSecondsRealtime(0.55f);
                    DeactivateDiceArea();
                }
            }
            else
            {
                yield return new WaitForSecondsRealtime(0.55f);
            }

            string firstName = _battle.State.Players[_battle.State.CurrentPlayer].Name;
            AddLogEntry(new LogEntry { Message = $"{firstName} goes first!", Type = LogEntryType.System });
            yield return new WaitForSecondsRealtime(0.25f);

            // Show stored SE for the first player after the opening roll
            int seTotal = _battle.State.Players[_battle.State.CurrentPlayer].Will;
            AddLogEntry(new LogEntry { Message = $"{firstName} stores {seTotal} SE after the opening roll.", Type = LogEntryType.System });

            _suppressSERolledVisual = false;

            if (endTurnButton) endTurnButton.interactable = true;
            Debug.Log("[Battle] OpeningSequence setup complete");

            // If AI goes first, run AI turn; otherwise wait for player draw
            if (_battle.State.CurrentPlayer == AIPlayerIndex)
            {
                StartCoroutine(RunAITurn());
            }
            // else: player sees Draw phase — clicks a card in hand or the draw happens
            // Auto-draw for player convenience
            else
            {
                StartCoroutine(AutoDrawCoroutine());
            }
        }

        private IEnumerator AutoDrawCoroutine()
        {
            if (_autoDrawRunning) yield break;
            _autoDrawRunning = true;
            yield return new WaitForSeconds(StoryBattleDelay(0.38f));
            _autoDrawRunning = false;
            if (_battle.State.GameOver) yield break;

            // Auto-draw for whichever player's turn it is
            if (_battle.State.Phase == GamePhase.Draw)
            {
                _battle.ProcessAction(_battle.State.CurrentPlayer, new DrawCardAction());
                // Force immediate UI refresh so drawn card is visible without clicking
                yield return new WaitForSeconds(StoryBattleDelay(0.24f));
                OnStateChanged(_battle.State);
            }

            // If it's the AI's turn, run AI
            if (_battle.State.CurrentPlayer == AIPlayerIndex && !_battle.State.GameOver)
            {
                StartCoroutine(RunAITurn());
            }
        }

        private float StoryBattleDelay(float delay)
        {
            return _storyBattleConfig != null ? Mathf.Max(0.08f, delay * 0.72f) : delay;
        }

        private IEnumerator RunAITurn()
        {
            if (_aiTurnRunning) yield break;
            _aiTurnRunning = true;

            // Auto-draw for AI if in Draw phase
            if (_battle.State.Phase == GamePhase.Draw)
            {
                yield return new WaitForSeconds(StoryBattleDelay(0.48f));
                _battle.ProcessAction(AIPlayerIndex, new DrawCardAction());
            }

            while (!_battle.State.GameOver && _battle.State.CurrentPlayer == AIPlayerIndex)
            {
                var actions = _ai.DecideActions(_battle.State);
                if (actions.Count == 0)
                {
                    // If AI cannot find any action during its own non-draw phase,
                    // force end turn so control returns to the player automatically.
                    if (_battle.State.CurrentPlayer == AIPlayerIndex
                        && _battle.State.Phase != GamePhase.Draw
                        && !_battle.State.GameOver)
                    {
                        _battle.ProcessAction(AIPlayerIndex, new EndTurnAction());
                        yield return new WaitForSeconds(StoryBattleDelay(0.4f));
                    }
                    break;
                }

                foreach (var action in actions)
                {
                    if (_battle.State.GameOver) break;
                    float pacingDelay = StoryBattleDelay(action switch
                    {
                        AttackAction => 0.84f,
                        FuseDaemonsAction => 0.92f,
                        ActivateInvokerAction => 0.88f,
                        _ => AIActionDelay,
                    });
                    yield return new WaitForSeconds(pacingDelay);
                    _battle.ProcessAction(AIPlayerIndex, action);

                    // If attack triggered auto-end-turn, do it
                    if (_battle.CombatAutoEnd && !_battle.State.GameOver)
                    {
                        yield return new WaitForSeconds(StoryBattleDelay(0.72f));
                        _battle.ProcessAction(AIPlayerIndex, new EndTurnAction());
                        break;
                    }
                }

                // If AI ended turn, break and let the draw coroutine handle next
                if (_battle.State.CurrentPlayer == LocalPlayer)
                    break;
            }

            _aiTurnRunning = false;

            // After AI turn ends, auto-draw for player
            if (!_battle.State.GameOver && _battle.State.CurrentPlayer == LocalPlayer
                && _battle.State.Phase == GamePhase.Draw)
            {
                StartCoroutine(AutoDrawCoroutine());
            }
        }

        // ─── Player Button Handlers ─────────────────────

        private void OnEndTurnClicked()
        {
            if (_storyEncounterMode)
            {
                OnStoryEncounterEndTurnClicked();
                return;
            }

            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.GameOver) return;
            ClearSelection();

            // In Main phase, this button acts as "BATTLE" (transition to Combat)
            if (_battle.State.Phase == GamePhase.Main)
            {
                SubmitPlayerAction(new NextPhaseAction());
                RefreshUI(_battle.State);
                return;
            }

            SubmitPlayerAction(new EndTurnAction());

            if (!_battle.State.GameOver)
                StartCoroutine(AutoDrawCoroutine());
        }

        // ─── Card Click: Hand ────────────────────────────
        private void OnHandCardClicked(int handIndex)
        {
            if (_battle.State.GameOver) return;

            var player = _battle.State.Players[LocalPlayer];
            if (handIndex < 0 || handIndex >= player.Hand.Count) return;

            // On the opponent's turn: show read-only inspect so the player can read their hand.
            // Also outside Main phase on own turn: read-only inspect only.
            bool isOwnTurn = _battle.State.CurrentPlayer == LocalPlayer;
            bool isMainPhase = _battle.State.Phase == GamePhase.Main;

            if (!isOwnTurn || !isMainPhase)
            {
                if (_selectedCardForPlay == handIndex)
                {
                    DismissCardPreview();
                    return;
                }
                ShowCardInspectReadOnly(player.Hand[handIndex].Card, handIndex);
                return;
            }

            // If already previewing this card, dismiss
            if (_selectedCardForPlay == handIndex)
            {
                DismissCardPreview();
                return;
            }

            ShowCardPreview(handIndex);
        }

        // ─── Card Preview Overlay ────────────────────────
        private void ShowCardPreview(int handIndex)
        {
            DismissCardPreview();

            var player = _battle.State.Players[LocalPlayer];
            if (handIndex < 0 || handIndex >= player.Hand.Count) return;
            var card = player.Hand[handIndex].Card;
            int cost = card.GetWillCost();
            bool canAfford = player.Will >= cost;
            bool isDaemon = card is DaemonCardData;
            bool isAsheCard = card is AsheCardData;

            _selectedCardForPlay = handIndex;

            // Full-screen dim backdrop
            var canvasTransform = (transform as RectTransform) ?? transform;
            _cardPreviewOverlay = new GameObject("CardPreviewOverlay");
            _cardPreviewOverlay.transform.SetParent(canvasTransform, false);
            var overlayRT = _cardPreviewOverlay.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            overlayRT.SetAsLastSibling();

            var overlayImg = _cardPreviewOverlay.AddComponent<Image>();
            overlayImg.color = new Color(0.02f, 0.02f, 0.04f, 0.34f);
            overlayImg.raycastTarget = true;
            var cancelBtn = _cardPreviewOverlay.AddComponent<Button>();
            cancelBtn.onClick.AddListener(DismissCardPreview);

            // Keep the preview off the center lane so the battlefield remains readable.
            var cardGO = Instantiate(cardPrefab, _cardPreviewOverlay.transform);
            var cardRT = cardGO.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.03f, 0.13f);
            cardRT.anchorMax = new Vector2(0.46f, 0.88f);
            cardRT.offsetMin = Vector2.zero;
            cardRT.offsetMax = Vector2.zero;
            cardRT.localScale = Vector3.one;
            var visual = cardGO.GetComponent<CardVisual>();
            if (visual != null) visual.SetCard(card);

            // Prevent card click from dismissing
            var cardImg = cardGO.GetComponent<Image>();
            if (cardImg != null) cardImg.raycastTarget = true;
            var cardBtn = cardGO.GetComponent<Button>() ?? cardGO.AddComponent<Button>();
            cardBtn.onClick.AddListener(() => { }); // swallow click

            // Info text below card
            var infoGO = new GameObject("PreviewInfo");
            infoGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var infoRT = infoGO.AddComponent<RectTransform>();
            infoRT.anchorMin = new Vector2(0.03f, 0.055f);
            infoRT.anchorMax = new Vector2(0.46f, 0.115f);
            infoRT.offsetMin = Vector2.zero;
            infoRT.offsetMax = Vector2.zero;
            var infoTMP = infoGO.AddComponent<TextMeshProUGUI>();
            infoTMP.fontSize = 17;
            infoTMP.fontStyle = FontStyles.Bold;
            infoTMP.alignment = TextAlignmentOptions.Center;
            infoTMP.raycastTarget = false;
            infoTMP.enableAutoSizing = true;
            infoTMP.fontSizeMin = 13f;
            infoTMP.fontSizeMax = 17f;

            if (!canAfford)
            {
                infoTMP.text = $"Not enough SE ({player.Will}/{cost})";
                infoTMP.color = new Color(1f, 0.4f, 0.35f);
            }
            else if (isDaemon)
            {
                infoTMP.text = "Tap backdrop to cancel";
                infoTMP.color = new Color(0.78f, 0.66f, 0.42f);
                CreateCenteredActionButton(_cardPreviewOverlay.transform, "SUMMON", () => ConfirmDaemonPlay(handIndex));
            }
            else if (isAsheCard)
            {
                bool hasTarget = player.Field.Any(d => ((AsheCardData)card).Matches(d.Card));
                if (!hasTarget)
                {
                    infoTMP.text = "No compatible daemon on field";
                    infoTMP.color = new Color(1f, 0.55f, 0.40f);
                }
                else
                {
                    infoTMP.text = "Tap backdrop to cancel";
                    infoTMP.color = new Color(0.62f, 0.96f, 0.72f);
                    int idx = handIndex;
                    CreateCenteredActionButton(_cardPreviewOverlay.transform, "ASSIGN TO DAEMON", () =>
                    {
                        DismissCardPreview();
                        _pendingAsheHandIndex = idx;
                        if (_battle?.State != null)
                            OnStateChanged(_battle.State);
                    });
                }
            }
            else
            {
                infoTMP.text = "Tap backdrop to cancel";
                infoTMP.color = new Color(0.78f, 0.66f, 0.42f);
                int idx = handIndex;
                CreateCenteredActionButton(_cardPreviewOverlay.transform, "PLAY", () => ConfirmNonDaemonPlay(idx));
            }

            CreatePreviewCloseButton(_cardPreviewOverlay.transform);

            // Pop-in animation
            overlayRT.localScale = Vector3.one * 0.92f;
            StartCoroutine(UIAnimUtils.PopScale(overlayRT, 0.18f, 1.02f));
        }

        private void CreatePreviewCloseButton(Transform panel)
        {
            var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(panel, false);
            var closeRT = closeGo.GetComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(0.28f, 0.76f);
            closeRT.anchorMax = new Vector2(0.35f, 0.82f);
            closeRT.offsetMin = Vector2.zero;
            closeRT.offsetMax = Vector2.zero;

            var closeImage = closeGo.GetComponent<Image>();
            closeImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            closeImage.type = closeImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            closeImage.color = new Color(0.18f, 0.12f, 0.12f, 0.92f);

            var closeLabelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            closeLabelGo.transform.SetParent(closeGo.transform, false);
            var closeLabelRT = closeLabelGo.GetComponent<RectTransform>();
            closeLabelRT.anchorMin = Vector2.zero;
            closeLabelRT.anchorMax = Vector2.one;
            closeLabelRT.offsetMin = Vector2.zero;
            closeLabelRT.offsetMax = Vector2.zero;
            var closeLabel = closeLabelGo.GetComponent<TextMeshProUGUI>();
            closeLabel.text = "X";
            closeLabel.fontSize = 16;
            closeLabel.fontStyle = FontStyles.Bold;
            closeLabel.color = new Color(0.98f, 0.92f, 0.84f);
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.raycastTarget = false;

            var closeButton = closeGo.GetComponent<Button>();
            closeButton.onClick.AddListener(DismissCardPreview);
        }

        private void CreatePreviewActionButton(Transform panel, string label, Action onClick)
        {
            var actionGo = new GameObject("ActionButton", typeof(RectTransform), typeof(Image), typeof(Button));
            actionGo.transform.SetParent(panel, false);
            var actionRT = actionGo.GetComponent<RectTransform>();
            actionRT.anchorMin = new Vector2(0.08f, 0.02f);
            actionRT.anchorMax = new Vector2(0.92f, 0.12f);
            actionRT.offsetMin = Vector2.zero;
            actionRT.offsetMax = Vector2.zero;

            var actionImage = actionGo.GetComponent<Image>();
            actionImage.sprite = Resources.Load<Sprite>("UI/btn-gold");
            actionImage.type = actionImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            actionImage.color = Color.white;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(actionGo.transform, false);
            var labelRT = labelGo.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var labelText = labelGo.GetComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 14f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = new Color(0.18f, 0.12f, 0.06f);
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.raycastTarget = false;

            var button = actionGo.GetComponent<Button>();
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
        }

        private void CreateCenteredActionButton(Transform parent, string label, Action onClick)
        {
            var actionGo = new GameObject("ActionButton", typeof(RectTransform), typeof(Image), typeof(Button));
            actionGo.transform.SetParent(parent, false);
            var actionRT = actionGo.GetComponent<RectTransform>();
            actionRT.anchorMin = new Vector2(0.05f, 0.05f);
            actionRT.anchorMax = new Vector2(0.35f, 0.11f);
            actionRT.offsetMin = Vector2.zero;
            actionRT.offsetMax = Vector2.zero;

            var actionImage = actionGo.GetComponent<Image>();
            actionImage.sprite = Resources.Load<Sprite>("UI/btn-gold");
            actionImage.type = actionImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            actionImage.color = Color.white;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(actionGo.transform, false);
            var labelRT = labelGo.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var labelText = labelGo.GetComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 16f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = new Color(0.18f, 0.12f, 0.06f);
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.raycastTarget = false;

            var button = actionGo.GetComponent<Button>();
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
        }

        private void ConfirmDaemonPlay(int handIndex)
        {
            ConfirmDaemonPlay(handIndex, -1);
        }

        private void ConfirmDaemonPlay(int handIndex, int targetSlotIndex)
        {
            var player = _battle.State.Players[LocalPlayer];
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return;

            SyncFieldSlotMap(player, LocalPlayer);
            CardData summonCard = player.Hand[handIndex].Card;
            Vector2 summonSourceScreen = GetHandCardScreenPoint(handIndex);
            if (targetSlotIndex < 0)
                targetSlotIndex = FindFirstAvailableFieldSlot(GetFieldSlotMap(LocalPlayer).Values);

            if (targetSlotIndex < 0 || targetSlotIndex >= GameConstants.MaxFieldDaemons)
                return;

            DismissCardPreview();
            _pendingLocalSummonSlot = targetSlotIndex;
            bool played = SubmitPlayerAction(new PlayDaemonAction { HandIndex = handIndex });
            if (played)
            {
                AssignNewestDaemonToSlot(_battle.State.Players[LocalPlayer], LocalPlayer, targetSlotIndex);
                RefreshUI(_battle.State);
                StartCoroutine(SummonAnimation(p1FieldContainer, targetSlotIndex, summonSourceScreen, summonCard));
            }
            else
            {
                _pendingLocalSummonSlot = -1;
            }
        }

        private IEnumerator RefreshUiAfterPlay()
        {
            yield return null;
            yield return null;

            if (_battle?.State != null)
                RefreshUI(_battle.State);
        }

        private void ConfirmNonDaemonPlay(int handIndex)
        {
            DismissCardPreview();
            var player = _battle.State.Players[LocalPlayer];
            if (handIndex < 0 || handIndex >= player.Hand.Count) return;
            var card = player.Hand[handIndex].Card;

            bool played = false;
            switch (card)
            {
                case DomainCardData:
                    played = SubmitPlayerAction(new PlayDomainAction { HandIndex = handIndex });
                    break;
                case SealCardData:
                    played = SubmitPlayerAction(new SetSealAction { HandIndex = handIndex });
                    break;
                case MaskCardData when player.Field.Count > 0:
                    _pendingMaskHandIndex = handIndex;
                    if (_battle?.State != null)
                        OnStateChanged(_battle.State);
                    return;
                case AsheCardData ashe when player.Field.Any(d => ashe.Matches(d.Card)):
                    _pendingAsheHandIndex = handIndex;
                    if (_battle?.State != null)
                        OnStateChanged(_battle.State);
                    return;
                case DispelCardData:
                    played = SubmitPlayerAction(new PlayDispelAction
                    {
                        HandIndex = handIndex,
                        TargetType = DispelTarget.Any,
                        TargetIndex = 0,
                    });
                    break;
            }

            if (played && _battle?.State != null)
                RefreshUI(_battle.State);
        }

        /// <summary>Ensures a DropZone component + raycastable Image on a container.</summary>
        private static void EnsureDropZone(Transform container, DropZone.DropZoneKind zoneKind)
        {
            if (container == null) return;
            // DropZone needs a Graphic for EventSystem raycast detection
            var img = container.GetComponent<Image>();
            if (img == null)
            {
                img = container.gameObject.AddComponent<Image>();
                img.color = Color.clear;
            }
            img.raycastTarget = true;
            var dropZone = container.GetComponent<DropZone>();
            if (dropZone == null)
                dropZone = container.gameObject.AddComponent<DropZone>();
            dropZone.ZoneKind = zoneKind;
            dropZone.enabled = true;
        }

        private static void DisableDropZone(Transform container)
        {
            if (container == null)
                return;

            var dropZone = container.GetComponent<DropZone>();
            if (dropZone != null)
                dropZone.enabled = false;

            var img = container.GetComponent<Image>();
            if (img != null)
                img.raycastTarget = false;
        }

        /// <summary>Drag-drop callback: plays a card from the hand, skipping the preview overlay.</summary>
        private void OnCardDropped(int handIndex, DropZone.DropZoneKind zoneKind)
        {
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.GameOver) return;
            if (_battle.State.Phase != GamePhase.Main) return;

            var player = _battle.State.Players[LocalPlayer];
            if (handIndex < 0 || handIndex >= player.Hand.Count) return;
            var card = player.Hand[handIndex].Card;
            if (player.Will < card.GetWillCost() || !CanPlayCard(player, card)) return;

            if (zoneKind == DropZone.DropZoneKind.HandReturn)
            {
                DismissCardPreview();
                return;
            }

            DismissCardPreview();

            if (card is DaemonCardData && zoneKind == DropZone.DropZoneKind.Battlefield)
            {
                // Daemons: summon to the next open battlefield slot.
                ConfirmDaemonPlay(handIndex);
            }
        }

        private IEnumerator SummonAnimation(Transform fieldContainer, int slotIndex, Vector2 sourceScreenPoint, CardData cardData)
        {
            _summonAnimInFlight = true;
            yield return null; // wait one frame for RefreshUI to rebuild
            _pendingLocalSummonSlot = -1;

            if (fieldContainer == null || slotIndex < 0 || slotIndex >= fieldContainer.childCount || cardData == null)
                yield break;

            Transform slot = fieldContainer.GetChild(slotIndex);
            RectTransform target = (FindSlotCard(slot) ?? slot) as RectTransform;
            if (target == null)
                yield break;

            CanvasGroup targetGroup = target.GetComponent<CanvasGroup>();
            if (targetGroup == null)
            {
                try
                {
                    targetGroup = target.gameObject.AddComponent<CanvasGroup>();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SummonAnimation] Failed to add CanvasGroup to {target.name}: {ex}");
                    yield break;
                }
            }
            if (targetGroup == null)
            {
                Debug.LogError($"[SummonAnimation] CanvasGroup is null on {target.name} after attempted AddComponent");
                yield break;
            }
            targetGroup.alpha = 0.72f;
            target.localScale = Vector3.one;

            var canvasRoot = transform as RectTransform;
            if (canvasRoot != null)
            {
                var flyer = Instantiate(daemonFieldPrefab ?? cardPrefab, canvasRoot);
                flyer.name = "SummonFlyer";
                var flyerVisual = flyer.GetComponent<CardVisual>();
                if (flyerVisual != null)
                    flyerVisual.SetCard(cardData);

                var flyerRT = flyer.GetComponent<RectTransform>();
                if (flyerRT != null)
                {
                    flyerRT.anchorMin = new Vector2(0.5f, 0.5f);
                    flyerRT.anchorMax = new Vector2(0.5f, 0.5f);
                    flyerRT.pivot = new Vector2(0.5f, 0.5f);
                    flyerRT.SetAsLastSibling();
                    ApplySocketCardPresentation(flyer, CardZoneStyle.Field, false);

                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, sourceScreenPoint, null, out Vector2 localFrom) &&
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, RectTransformUtility.WorldToScreenPoint(null, target.position), null, out Vector2 localTo))
                    {
                        Vector2 control = (localFrom + localTo) * 0.5f + Vector2.up * Mathf.Lerp(90f, 140f, GetBattlefieldLayoutT());
                        float duration = 0.32f;
                        float elapsed = 0f;
                        flyerRT.anchoredPosition = localFrom;
                        flyerRT.localScale = Vector3.one * 0.68f;
                        flyerRT.localRotation = Quaternion.Euler(0f, 0f, -9f);

                        while (elapsed < duration)
                        {
                            elapsed += Time.unscaledDeltaTime;
                            float p = Mathf.Clamp01(elapsed / duration);
                            float eased = UIAnimUtils.EaseOutQuad(p);
                            float u = 1f - eased;
                            flyerRT.anchoredPosition = u * u * localFrom + 2f * u * eased * control + eased * eased * localTo;
                            flyerRT.localScale = Vector3.one * Mathf.Lerp(0.68f, 0.94f, eased);
                            flyerRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-9f, 0f, eased));
                            yield return null;
                        }
                    }
                }

                Destroy(flyer);
            }

            targetGroup.alpha = 1f;
            Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.Summon, 0.86f, 1f);
            StartCoroutine(UIAnimUtils.PopScale(target, 0.22f, 1.06f));

            var glow = new GameObject("SummonGlow");
            glow.transform.SetParent(target, false);
            var glowRT = glow.AddComponent<RectTransform>();
            glowRT.anchorMin = new Vector2(-0.1f, -0.1f);
            glowRT.anchorMax = new Vector2(1.1f, 1.1f);
            glowRT.offsetMin = Vector2.zero;
            glowRT.offsetMax = Vector2.zero;
            var glowImg = glow.AddComponent<Image>();
            glowImg.color = new Color(0.78f, 0.66f, 0.42f, 0.6f);
            glowImg.raycastTarget = false;

            float glowDur = 0.4f;
            float glowT = 0f;
            Color startColor = glowImg.color;
            while (glowT < glowDur)
            {
                glowT += Time.deltaTime;
                glowImg.color = Color.Lerp(startColor, new Color(startColor.r, startColor.g, startColor.b, 0f), glowT / glowDur);
                yield return null;
            }
            Destroy(glow);

            // Animation complete — allow field rebuilds and sync the board
            _summonAnimInFlight = false;
            if (_battle?.State != null)
                OnStateChanged(_battle.State);  // Deferred refresh to ensure field is updated
        }

        private Vector2 GetHandCardScreenPoint(int handIndex)
        {
            if (p1HandContainer != null && handIndex >= 0 && handIndex < p1HandContainer.childCount)
                return RectTransformUtility.WorldToScreenPoint(null, p1HandContainer.GetChild(handIndex).position);

            if (_cardPreviewOverlay != null)
            {
                Transform previewCard = _cardPreviewOverlay.transform.Find("Card(Clone)");
                if (previewCard != null)
                    return RectTransformUtility.WorldToScreenPoint(null, previewCard.position);
            }

            return RectTransformUtility.WorldToScreenPoint(null, p1HandContainer != null ? p1HandContainer.position : transform.position);
        }

        private void DismissCardPreview()
        {
            DismissCardPreview(true);
        }

        /// <summary>
        /// Shows an inspect-only overlay for a card — no action buttons.
        /// Used when the player wants to read a hand card outside Main phase,
        /// or inspect a daemon on the field.
        /// </summary>
        private void ShowCardInspectReadOnly(CardData card, int handIndex = -1)
        {
            DismissCardPreview();
            _selectedCardForPlay = handIndex; // track so toggling works

            var canvasTransform = (transform as RectTransform) ?? transform;
            _cardPreviewOverlay = new GameObject("CardInspectOverlay");
            _cardPreviewOverlay.transform.SetParent(canvasTransform, false);
            var overlayRT = _cardPreviewOverlay.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            overlayRT.SetAsLastSibling();

            var overlayImg = _cardPreviewOverlay.AddComponent<Image>();
            overlayImg.color = new Color(0.02f, 0.02f, 0.04f, 0.60f);
            overlayImg.raycastTarget = true;
            var cancelBtn = _cardPreviewOverlay.AddComponent<Button>();
            cancelBtn.onClick.AddListener(DismissCardPreview);

            // Large centered card
            var cardGO = Instantiate(cardPrefab, _cardPreviewOverlay.transform);
            var cardRT = cardGO.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.22f, 0.22f);
            cardRT.anchorMax = new Vector2(0.78f, 0.82f);
            cardRT.offsetMin = Vector2.zero;
            cardRT.offsetMax = Vector2.zero;
            cardRT.localScale = Vector3.one;
            var visual = cardGO.GetComponent<CardVisual>();
            if (visual != null) visual.SetCard(card);
            var cardBtn = cardGO.GetComponent<Button>() ?? cardGO.AddComponent<Button>();
            cardBtn.onClick.AddListener(() => { });

            // Description / info text
            var infoGO = new GameObject("InspectInfo");
            infoGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var infoRT = infoGO.AddComponent<RectTransform>();
            infoRT.anchorMin = new Vector2(0.15f, 0.12f);
            infoRT.anchorMax = new Vector2(0.85f, 0.21f);
            infoRT.offsetMin = Vector2.zero;
            infoRT.offsetMax = Vector2.zero;
            var infoTMP = infoGO.AddComponent<TextMeshProUGUI>();
            infoTMP.text = string.IsNullOrEmpty(card.description) ? card.cardName : card.description;
            infoTMP.fontSize = 13;
            infoTMP.color = new Color(0.88f, 0.84f, 0.78f);
            infoTMP.alignment = TextAlignmentOptions.Center;
            infoTMP.raycastTarget = false;

            // "tap to close" hint
            var hintGO = new GameObject("Hint");
            hintGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var hintRT = hintGO.AddComponent<RectTransform>();
            hintRT.anchorMin = new Vector2(0.25f, 0.06f);
            hintRT.anchorMax = new Vector2(0.75f, 0.12f);
            hintRT.offsetMin = Vector2.zero;
            hintRT.offsetMax = Vector2.zero;
            var hintTMP = hintGO.AddComponent<TextMeshProUGUI>();
            hintTMP.text = "Tap anywhere to close";
            hintTMP.fontSize = 11;
            hintTMP.color = new Color(0.55f, 0.52f, 0.48f);
            hintTMP.alignment = TextAlignmentOptions.Center;
            hintTMP.raycastTarget = false;

            StartCoroutine(UIAnimUtils.PopScale(overlayRT, 0.15f, 1.02f));
        }

        /// <summary>Shows a read-only inspect overlay for a daemon already on the field.</summary>
        private void ShowFieldDaemonInspect(DaemonInstance daemon)
        {
            DismissCardPreview();

            var canvasTransform = (transform as RectTransform) ?? transform;
            _cardPreviewOverlay = new GameObject("DaemonInspectOverlay");
            _cardPreviewOverlay.transform.SetParent(canvasTransform, false);
            var overlayRT = _cardPreviewOverlay.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            overlayRT.SetAsLastSibling();

            var overlayImg = _cardPreviewOverlay.AddComponent<Image>();
            overlayImg.color = new Color(0.02f, 0.02f, 0.04f, 0.60f);
            overlayImg.raycastTarget = true;
            var cancelBtn = _cardPreviewOverlay.AddComponent<Button>();
            cancelBtn.onClick.AddListener(DismissCardPreview);

            // Large card
            var cardGO = Instantiate(daemonFieldPrefab ?? cardPrefab, _cardPreviewOverlay.transform);
            var cardRT = cardGO.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.22f, 0.22f);
            cardRT.anchorMax = new Vector2(0.78f, 0.82f);
            cardRT.offsetMin = Vector2.zero;
            cardRT.offsetMax = Vector2.zero;
            cardRT.localScale = Vector3.one;
            var visual = cardGO.GetComponent<CardVisual>();
            if (visual != null)
            {
                visual.SetCard(daemon.Card);
                visual.SetRuntimeMainStat(daemon.CurrentAshe);
            }
            var cardBtn = cardGO.GetComponent<Button>() ?? cardGO.AddComponent<Button>();
            cardBtn.onClick.AddListener(() => { });

            // Build a stat / status line
            var daemonCard = daemon.Card as DaemonCardData;
            string statLine = $"{daemon.CurrentAshe}/{daemon.MaxAshe} Ashe";
            if (daemonCard != null)
                statLine += $"  •  ATK {daemon.Attack}";

            string status = "";
            if (daemon.Frozen)  status += " [Frozen]";
            if (daemon.Entangled) status += " [Entangled]";
            if (daemon.Stealthed) status += " [Stealth]";
            if (daemon.Poisoned) status += " [Poison]";
            if (daemon.Burning) status += " [Burn]";
            if (daemon.HasTaunt) status += " [Taunt]";
            if (daemon.ShieldAmount > 0) status += $" [Shield {daemon.ShieldAmount}]";
            if (daemon.NextAttackDouble) status += " [×2 ATK]";
            if (!daemon.CanAttack && !daemon.Frozen) status += " [Summoning Sickness]";

            // Description
            var descGO = new GameObject("Description");
            descGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var descRT = descGO.AddComponent<RectTransform>();
            descRT.anchorMin = new Vector2(0.12f, 0.15f);
            descRT.anchorMax = new Vector2(0.88f, 0.22f);
            descRT.offsetMin = Vector2.zero;
            descRT.offsetMax = Vector2.zero;
            var descTMP = descGO.AddComponent<TextMeshProUGUI>();
            descTMP.text = string.IsNullOrEmpty(daemon.Card.description) ? daemon.Card.cardName : daemon.Card.description;
            descTMP.fontSize = 13;
            descTMP.color = new Color(0.88f, 0.84f, 0.78f);
            descTMP.alignment = TextAlignmentOptions.Center;
            descTMP.raycastTarget = false;

            // Stats
            var statsGO = new GameObject("Stats");
            statsGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var statsRT = statsGO.AddComponent<RectTransform>();
            statsRT.anchorMin = new Vector2(0.12f, 0.10f);
            statsRT.anchorMax = new Vector2(0.88f, 0.16f);
            statsRT.offsetMin = Vector2.zero;
            statsRT.offsetMax = Vector2.zero;
            var statsTMP = statsGO.AddComponent<TextMeshProUGUI>();
            statsTMP.text = statLine + status;
            statsTMP.fontSize = 12;
            statsTMP.color = new Color(0.78f, 0.88f, 1f);
            statsTMP.alignment = TextAlignmentOptions.Center;
            statsTMP.raycastTarget = false;

            // Tap to close hint
            var hintGO = new GameObject("Hint");
            hintGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var hintRT = hintGO.AddComponent<RectTransform>();
            hintRT.anchorMin = new Vector2(0.25f, 0.05f);
            hintRT.anchorMax = new Vector2(0.75f, 0.10f);
            hintRT.offsetMin = Vector2.zero;
            hintRT.offsetMax = Vector2.zero;
            var hintTMP = hintGO.AddComponent<TextMeshProUGUI>();
            hintTMP.text = "Tap anywhere to close";
            hintTMP.fontSize = 11;
            hintTMP.color = new Color(0.55f, 0.52f, 0.48f);
            hintTMP.alignment = TextAlignmentOptions.Center;
            hintTMP.raycastTarget = false;

            StartCoroutine(UIAnimUtils.PopScale(overlayRT, 0.15f, 1.02f));
        }

        /// <summary>
        /// Pokemon TCG-style attack selection panel. Shows the attacker daemon in large,
        /// lists its available attack(s) with element colour, damage preview and ashe cost,
        /// and pre-computes type effectiveness against every visible opponent daemon.
        /// Clicking an attack dismisses the panel and enters target-selection mode.
        /// </summary>
        private void ShowAttackSelectionPanel(int fieldIndex)
        {
            var player  = _battle.State.Players[LocalPlayer];
            var opp     = _battle.State.Players[AIPlayerIndex];
            if (fieldIndex < 0 || fieldIndex >= player.Field.Count) return;

            var daemon     = player.Field[fieldIndex];
            var daemonCard = daemon.Card as DaemonCardData;
            if (daemonCard == null) return;

            DismissCardPreview();

            var canvasTransform = (transform as RectTransform) ?? transform;
            _cardPreviewOverlay = new GameObject("AttackSelectionPanel");
            _cardPreviewOverlay.transform.SetParent(canvasTransform, false);
            var overlayRT = _cardPreviewOverlay.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            overlayRT.SetAsLastSibling();

            var overlayImg = _cardPreviewOverlay.AddComponent<Image>();
            overlayImg.color = new Color(0.03f, 0.03f, 0.08f, 0.78f);
            overlayImg.raycastTarget = true;
            // Tapping the bare background cancels
            var cancelTap = _cardPreviewOverlay.AddComponent<Button>();
            cancelTap.onClick.AddListener(DismissCardPreview);

            // ── Large daemon card (zoomed ~60 % screen height) ──
            var cardGO = Instantiate(daemonFieldPrefab ?? cardPrefab, _cardPreviewOverlay.transform);
            var cardRT  = cardGO.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.18f, 0.32f);
            cardRT.anchorMax = new Vector2(0.82f, 0.93f);
            cardRT.offsetMin = Vector2.zero;
            cardRT.offsetMax = Vector2.zero;
            cardRT.localScale = Vector3.one;
            var visual = cardGO.GetComponent<CardVisual>();
            if (visual != null)
            {
                visual.SetCard(daemon.Card);
                visual.SetRuntimeMainStat(daemon.CurrentAshe);
            }
            var cardBtn = cardGO.GetComponent<Button>() ?? cardGO.AddComponent<Button>();
            cardBtn.onClick.AddListener(() => { }); // absorb clicks

            // ── "Choose Attack" header ──
            var headerGO = new GameObject("ChooseAttackHeader");
            headerGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var headerRT  = headerGO.AddComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0.10f, 0.88f);
            headerRT.anchorMax = new Vector2(0.90f, 0.94f);
            headerRT.offsetMin = Vector2.zero;
            headerRT.offsetMax = Vector2.zero;
            var headerTMP = headerGO.AddComponent<TextMeshProUGUI>();
            headerTMP.text = $"<b>{daemon.Card.cardName}</b>  —  Choose Attack";
            headerTMP.fontSize = 16;
            headerTMP.color = new Color(0.92f, 0.88f, 0.80f);
            headerTMP.alignment = TextAlignmentOptions.Center;
            headerTMP.raycastTarget = false;

            // ── Pre-compute matchup text for each visible opponent daemon ──
            var matchupLines = new System.Text.StringBuilder();
            foreach (var oppDaemon in opp.Field)
            {
                if (oppDaemon?.Card is not DaemonCardData oppCard) continue;
                float mult = Core.ElementSystem.GetElementMatchup(daemonCard.element, oppCard.element);
                string matchLabel = mult >= Core.GameConstants.SuperEffectiveMult ? "<color=#FFB347>SUPER EFFECTIVE</color>"
                                  : mult <= Core.GameConstants.WeakMult ? "<color=#8899AA>NOT VERY EFFECTIVE</color>"
                                  : "<color=#AABBCC>Neutral</color>";
                matchupLines.Append($"vs {oppCard.cardName}: {matchLabel}  ");
            }
            if (matchupLines.Length == 0) matchupLines.Append("Select a target daemon or Invoker");

            var matchupGO  = new GameObject("MatchupLine");
            matchupGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var matchupRT  = matchupGO.AddComponent<RectTransform>();
            matchupRT.anchorMin = new Vector2(0.08f, 0.25f);
            matchupRT.anchorMax = new Vector2(0.92f, 0.31f);
            matchupRT.offsetMin = Vector2.zero;
            matchupRT.offsetMax = Vector2.zero;
            var matchupTMP = matchupGO.AddComponent<TextMeshProUGUI>();
            matchupTMP.text = matchupLines.ToString();
            matchupTMP.fontSize = 12;
            matchupTMP.color = Color.white;
            matchupTMP.alignment = TextAlignmentOptions.Center;
            matchupTMP.richText = true;
            matchupTMP.raycastTarget = false;

            // ── Main attack button (element-themed, Pokemon TCG style) ──
            Color elemColor = Core.ElementColors.GetElementColor(daemonCard.element);
            Color btnBg     = Color.Lerp(elemColor, new Color(0.06f, 0.06f, 0.10f), 0.58f);

            string attackLabel = daemonCard.ability != null && !string.IsNullOrEmpty(daemonCard.ability.abilityName)
                ? daemonCard.ability.abilityName
                : $"{daemonCard.element} Strike";

            var atkBtnGO  = new GameObject("AttackButton");
            atkBtnGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var atkBtnRT  = atkBtnGO.AddComponent<RectTransform>();
            atkBtnRT.anchorMin = new Vector2(0.10f, 0.13f);
            atkBtnRT.anchorMax = new Vector2(0.90f, 0.24f);
            atkBtnRT.offsetMin = Vector2.zero;
            atkBtnRT.offsetMax = Vector2.zero;
            var atkBtnImg = atkBtnGO.AddComponent<Image>();
            atkBtnImg.color = btnBg;
            var atkBtn    = atkBtnGO.AddComponent<Button>();

            // Element badge (left)
            var badgeGO  = new GameObject("ElemBadge");
            badgeGO.transform.SetParent(atkBtnGO.transform, false);
            var badgeRT  = badgeGO.AddComponent<RectTransform>();
            badgeRT.anchorMin = new Vector2(0.01f, 0.10f);
            badgeRT.anchorMax = new Vector2(0.15f, 0.90f);
            badgeRT.offsetMin = Vector2.zero;
            badgeRT.offsetMax = Vector2.zero;
            var badgeImg = badgeGO.AddComponent<Image>();
            badgeImg.color = elemColor;
            badgeImg.raycastTarget = false;
            var badgeLblGO = new GameObject("BadgeLbl");
            badgeLblGO.transform.SetParent(badgeGO.transform, false);
            var badgeLblRT = badgeLblGO.AddComponent<RectTransform>();
            badgeLblRT.anchorMin = Vector2.zero;
            badgeLblRT.anchorMax = Vector2.one;
            badgeLblRT.offsetMin = Vector2.zero;
            badgeLblRT.offsetMax = Vector2.zero;
            var badgeTMP = badgeLblGO.AddComponent<TextMeshProUGUI>();
            badgeTMP.text = $"<b>{daemonCard.element.ToString().ToUpper()[0]}</b>";
            badgeTMP.fontSize = 20;
            badgeTMP.color = Color.white;
            badgeTMP.alignment = TextAlignmentOptions.Center;
            badgeTMP.raycastTarget = false;

            // Attack name (center)
            var nameLblGO  = new GameObject("AtkName");
            nameLblGO.transform.SetParent(atkBtnGO.transform, false);
            var nameLblRT  = nameLblGO.AddComponent<RectTransform>();
            nameLblRT.anchorMin = new Vector2(0.16f, 0.05f);
            nameLblRT.anchorMax = new Vector2(0.72f, 0.95f);
            nameLblRT.offsetMin = Vector2.zero;
            nameLblRT.offsetMax = Vector2.zero;
            var nameLblTMP = nameLblGO.AddComponent<TextMeshProUGUI>();
            nameLblTMP.text = $"<b>{attackLabel}</b>";
            nameLblTMP.fontSize = 18;
            nameLblTMP.color = Color.white;
            nameLblTMP.alignment = TextAlignmentOptions.MidlineLeft;
            nameLblTMP.raycastTarget = false;

            // Damage on right (shown as "−X" to indicate what the opponent daemon loses)
            var dmgGO  = new GameObject("DmgLabel");
            dmgGO.transform.SetParent(atkBtnGO.transform, false);
            var dmgRT  = dmgGO.AddComponent<RectTransform>();
            dmgRT.anchorMin = new Vector2(0.70f, 0.05f);
            dmgRT.anchorMax = new Vector2(0.99f, 0.95f);
            dmgRT.offsetMin = Vector2.zero;
            dmgRT.offsetMax = Vector2.zero;
            var dmgTMP = dmgGO.AddComponent<TextMeshProUGUI>();
            dmgTMP.text = $"<b>−{daemon.Attack}</b>";
            dmgTMP.fontSize = 22;
            dmgTMP.color = new Color(1f, 0.92f, 0.72f);
            dmgTMP.alignment = TextAlignmentOptions.MidlineRight;
            dmgTMP.raycastTarget = false;

            // A tiny "Ashe" unit label under the damage number
            var unitGO  = new GameObject("AtkUnit");
            unitGO.transform.SetParent(atkBtnGO.transform, false);
            var unitRT  = unitGO.AddComponent<RectTransform>();
            unitRT.anchorMin = new Vector2(0.70f, 0.00f);
            unitRT.anchorMax = new Vector2(0.99f, 0.35f);
            unitRT.offsetMin = Vector2.zero;
            unitRT.offsetMax = Vector2.zero;
            var unitTMP = unitGO.AddComponent<TextMeshProUGUI>();
            unitTMP.text = "Ashe";
            unitTMP.fontSize = 9;
            unitTMP.color = new Color(0.80f, 0.76f, 0.70f);
            unitTMP.alignment = TextAlignmentOptions.MidlineRight;
            unitTMP.raycastTarget = false;

            // ── Bind attack button ──
            int capturedIndex = fieldIndex;
            atkBtn.onClick.AddListener(() =>
            {
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.ButtonPress, 0.72f, 1.08f);
                DismissCardPreview();
                _selectedAttackerIndex = capturedIndex;
                _waitingForTarget = true;
                RefreshUI(_battle.State);
            });

            // Slight glow-on-hover via ColorBlock
            var colors = atkBtn.colors;
            colors.highlightedColor = Color.Lerp(btnBg, Color.white, 0.25f);
            atkBtn.colors = colors;

            // ── Cancel row ──
            var cancelGO  = new GameObject("CancelBtn");
            cancelGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var cancelRT  = cancelGO.AddComponent<RectTransform>();
            cancelRT.anchorMin = new Vector2(0.32f, 0.05f);
            cancelRT.anchorMax = new Vector2(0.68f, 0.12f);
            cancelRT.offsetMin = Vector2.zero;
            cancelRT.offsetMax = Vector2.zero;
            var cancelImg = cancelGO.AddComponent<Image>();
            cancelImg.color = new Color(0.25f, 0.10f, 0.10f, 0.82f);
            var cancelBtnC = cancelGO.AddComponent<Button>();
            cancelBtnC.onClick.AddListener(DismissCardPreview);
            var cancelLblGO  = new GameObject("CancelLbl");
            cancelLblGO.transform.SetParent(cancelGO.transform, false);
            var cancelLblRT  = cancelLblGO.AddComponent<RectTransform>();
            cancelLblRT.anchorMin = Vector2.zero;
            cancelLblRT.anchorMax = Vector2.one;
            cancelLblRT.offsetMin = Vector2.zero;
            cancelLblRT.offsetMax = Vector2.zero;
            var cancelLblTMP = cancelLblGO.AddComponent<TextMeshProUGUI>();
            cancelLblTMP.text = "Cancel";
            cancelLblTMP.fontSize = 14;
            cancelLblTMP.color = new Color(0.80f, 0.62f, 0.62f);
            cancelLblTMP.alignment = TextAlignmentOptions.Center;
            cancelLblTMP.raycastTarget = false;

            StartCoroutine(UIAnimUtils.PopScale(overlayRT, 0.12f, 1.02f));
        }

        private void DismissCardPreview(bool refreshUi)
        {
            bool hadPreview = _selectedCardForPlay >= 0 || _cardPreviewOverlay != null;
            _selectedCardForPlay = -1;
            if (_cardPreviewOverlay != null)
            {
                Destroy(_cardPreviewOverlay);
                _cardPreviewOverlay = null;
            }

            if (refreshUi && hadPreview && _battle != null && _battle.State != null && isActiveAndEnabled)
                RefreshUI(_battle.State);
        }

        // ─── Card Click: Own Field (select attacker / inspect) ────
        private void OnOwnDaemonClicked(int fieldIndex)
        {
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.GameOver) return;
            var player = _battle.State.Players[LocalPlayer];
            if (fieldIndex < 0 || fieldIndex >= player.Field.Count) return;

            if (_battle.State.Phase == GamePhase.Main)
            {
                if (CanAttemptFusion(player))
                {
                    HandleFusionSelectionClick(player, fieldIndex);
                }
                else
                {
                    // Not in fusion mode — inspect the daemon
                    ShowFieldDaemonInspect(player.Field[fieldIndex]);
                }
                return;
            }
            if (_battle.State.Phase != GamePhase.Combat) return;

            var daemon = player.Field[fieldIndex];
            if (!daemon.CanAttack || daemon.HasAttacked || daemon.Frozen || daemon.Entangled)
                return;

            if (_waitingForTarget && _selectedAttackerIndex == fieldIndex)
            {
                ClearSelection();
                DismissCardPreview();
                RefreshUI(_battle.State);
                return;
            }

            // Open the attack selection panel — enter targeting only after the player picks an attack
            ShowAttackSelectionPanel(fieldIndex);
        }

        // ─── Card Click: Opponent targets ────────────────
        private void OnOpponentDaemonClicked(int fieldIndex)
        {
            // Not in attack targeting — show card inspect instead.
            if (!_waitingForTarget || _selectedAttackerIndex < 0)
            {
                var opp = _battle.State.Players[AIPlayerIndex];
                if (fieldIndex >= 0 && fieldIndex < opp.Field.Count)
                    ShowFieldDaemonInspect(opp.Field[fieldIndex]);
                return;
            }
            bool attacked = SubmitPlayerAction(new AttackAction
            {
                AttackerIndex = _selectedAttackerIndex,
                Target = TargetType.Daemon,
                TargetIndex = fieldIndex,
            });
            if (attacked)
            {
                ClearSelection();
                CheckAttackEndsTurn();
            }
            else
            {
                ShowTargetingHint("Invalid target");
                RefreshUI(_battle.State);
            }
        }

        private void OnOpponentPillarClicked(int pillarIndex)
        {
            // Pillars are not direct combat targets in story-card battles.
            // Keep the target selection active and tell the player what happened.
            if (_waitingForTarget && _selectedAttackerIndex >= 0)
                ShowTargetingHint("Pillars protect automatically");
            return;
        }

        private void OnOpponentInvokerClicked()
        {
            if (!_waitingForTarget || _selectedAttackerIndex < 0) return;
            bool attacked = SubmitPlayerAction(new AttackAction
            {
                AttackerIndex = _selectedAttackerIndex,
                Target = TargetType.Invoker,
                TargetIndex = 0,
            });
            if (attacked)
            {
                ClearSelection();
                CheckAttackEndsTurn();
            }
            else
            {
                ShowTargetingHint("Defeat enemy daemons first");
                RefreshUI(_battle.State);
            }
        }

        private void ShowTargetingHint(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Vector3 center = _combatFxLayer != null ? GetRectWorldPoint(_combatFxLayer) : Vector3.zero;
            SpawnFloatingCombatText(center + Vector3.up * 40f, message.ToUpperInvariant(), new Color(1f, 0.72f, 0.48f, 0.96f), 0.72f);
            Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.Counter, 0.22f, 0.82f);
        }

        private void OnOwnInvokerClicked()
        {
            if (_battle?.State == null || _battle.State.GameOver)
                return;
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.Phase != GamePhase.Main)
                return;

            SubmitPlayerAction(new ActivateInvokerAction());
        }

        /// <summary>After an attack resolves, auto-end the turn with a short delay.</summary>
        private void CheckAttackEndsTurn()
        {
            if (_battle.CombatAutoEnd && !_battle.State.GameOver)
            {
                StartCoroutine(DelayedEndTurn(0.62f));
            }
        }

        private IEnumerator DelayedEndTurn(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_battle.State.GameOver) yield break;
            SubmitPlayerAction(new EndTurnAction());
            if (!_battle.State.GameOver)
                StartCoroutine(AutoDrawCoroutine());
        }

        private void ClearSelection()
        {
            _selectedAttackerIndex = -1;
            _waitingForTarget = false;
            _selectedFusionPrimaryIndex = -1;
        }

        private void PlayAttackAnimOnField(Transform fieldContainer, int index)
        {
            if (fieldContainer == null || index < 0 || index >= fieldContainer.childCount) return;
            Transform slot = fieldContainer.GetChild(index);
            Transform card = FindSlotCard(slot);
            var go = (card ?? slot).gameObject;
            var visual = go.GetComponent<CardVisual>();
            if (visual != null)
                visual.PlayAttackAnimation();
            else
                StartCoroutine(UIAnimUtils.ClickBounce(go.transform, 0.88f, 0.16f));
        }

        // ─── UI Refresh ──────────────────────────────────
        private void RefreshUI(GameState state)
        {
            if (state == null || _isRefreshingUi)
                return;

            // Clear any sticky hover before re-rendering cards.
            CardVisual.ClearActiveHover();

            _isRefreshingUi = true;
            try
            {
                bool isPlayerTurn = state.CurrentPlayer == LocalPlayer && !state.GameOver;
                bool isMainPhase = state.Phase == GamePhase.Main;
                bool isCombatPhase = state.Phase == GamePhase.Combat;
                bool canPlayCards = isPlayerTurn && isMainPhase;
                bool canAttack = isPlayerTurn && isCombatPhase;
                bool maskTargetMode = isPlayerTurn && isMainPhase && IsMaskTargetSelectionActive(state.Players[LocalPlayer]);
                bool asheTargetMode = isPlayerTurn && isMainPhase && IsAsheTargetSelectionActive(state.Players[LocalPlayer]);
                bool canFuse = isPlayerTurn && isMainPhase && CanAttemptFusion(state.Players[LocalPlayer]);
                bool turnChanged = state.TurnNumber != _lastTurnNumber;

                // Dismiss card preview on turn change without triggering a nested refresh.
                if (turnChanged)
                {
                    _pendingMaskHandIndex = -1;
                    _pendingAsheHandIndex = -1;
                    _selectedFusionPrimaryIndex = -1;
                    DismissCardPreview(refreshUi: false);
                }

                // Hand is playable during Main; field is selectable during Combat
                RefreshPlayerUI(state.Players[0], p1NameText, p1HpText, p1HpBar, p1WillText, p1DeckCountText,
                    p1HandContainer, p1FieldContainer, p1PillarContainer, 0,
                    canPlayCards, canAttack || maskTargetMode || asheTargetMode || canFuse, isPlayerTurn);
                RefreshPlayerUI(state.Players[1], p2NameText, p2HpText, p2HpBar, p2WillText, p2DeckCountText,
                    p2HandContainer, p2FieldContainer, p2PillarContainer, 1,
                    false, false, state.CurrentPlayer == AIPlayerIndex && !state.GameOver);
                RefreshInvokerAnchor(state.Players[0], LocalPlayer, isPlayerTurn, false);
                RefreshInvokerAnchor(state.Players[1], AIPlayerIndex, state.CurrentPlayer == AIPlayerIndex && !state.GameOver,
                    _waitingForTarget);
                RefreshSideZones(state);
                BringForegroundZonesToFront();

                UpdateActionButtons(state, isPlayerTurn);
                UpdateBattlefieldGuidance(state, isPlayerTurn, canPlayCards, canAttack, canFuse);
                UpdateStoryTutorial(state, isPlayerTurn);
                AnimateDaemonHealingFx(state);

                _lastPhase = state.Phase;
                _lastTurnNumber = state.TurnNumber;
            }
            finally
            {
                _isRefreshingUi = false;
            }
        }

        private void RefreshPlayerUI(PlayerState player,
            TextMeshProUGUI nameText, TextMeshProUGUI hpText, Slider hpBar,
            TextMeshProUGUI willText, TextMeshProUGUI deckCount,
            Transform handContainer, Transform fieldContainer, Transform pillarContainer,
            int playerIndex, bool handPlayable, bool fieldSelectable, bool activeTurn)
        {
            if (nameText) nameText.text = player.Name;
            if (hpText) hpText.text = $"{player.Invoker.Hp}/{player.Invoker.MaxHp}";
            if (hpBar)
            {
                hpBar.maxValue = player.Invoker.MaxHp;
                hpBar.value = player.Invoker.Hp;
            }
            if (willText) willText.text = $"Stored SE: {player.Will}";
            if (deckCount) deckCount.text = $"Grimware: {player.Deck.Count}";
            if (nameText) nameText.color = activeTurn ? new Color(0.99f, 0.92f, 0.72f) : new Color(0.86f, 0.86f, 0.90f);
            if (deckCount) deckCount.color = activeTurn ? new Color(0.93f, 0.88f, 0.72f) : new Color(0.70f, 0.72f, 0.78f);
            AnimateStatReadout(playerIndex, player.Invoker.Hp, player.Will, hpText, willText);

            // Compute hashes to detect actual content changes and skip unnecessary rebuilds
            int handHash = ComputeHandHash(player);
            int selectedSummonHandIndex = -1;
            bool summonSeatMode = playerIndex == LocalPlayer && fieldSelectable
                && TryGetSelectedSummonHandIndex(player, out selectedSummonHandIndex);
            bool maskTargetMode = playerIndex == LocalPlayer && IsMaskTargetSelectionActive(player);
            bool asheTargetMode = playerIndex == LocalPlayer && IsAsheTargetSelectionActive(player);
            bool fusionMode = playerIndex == LocalPlayer && CanAttemptFusion(player);
            bool targetMode = playerIndex == AIPlayerIndex && _waitingForTarget;
            int fieldHash = ComputeFieldHash(player, fieldSelectable, summonSeatMode, maskTargetMode, asheTargetMode, fusionMode, targetMode,
                playerIndex == LocalPlayer ? _selectedAttackerIndex : -1,
                playerIndex == LocalPlayer ? _selectedFusionPrimaryIndex : -1);
            int pillarHash = ComputePillarHash(player, targetMode);
            ref int savedHand = ref (playerIndex == 0 ? ref _p1HandHash : ref _p2HandHash);
            ref int savedField = ref (playerIndex == 0 ? ref _p1FieldHash : ref _p2FieldHash);
            ref int savedPillar = ref (playerIndex == 0 ? ref _p1PillarHash : ref _p2PillarHash);
            bool handChanged = handHash != savedHand;
            bool fieldChanged = fieldHash != savedField;
            bool pillarChanged = pillarHash != savedPillar;
            ref int previousHandCount = ref (playerIndex == 0 ? ref _p1PrevHandCount : ref _p2PrevHandCount);
            bool handCountIncreased = previousHandCount >= 0 && player.Hand.Count > previousHandCount;
            previousHandCount = player.Hand.Count;
            savedHand = handHash;
            savedField = fieldHash;
            savedPillar = pillarHash;

            // ─── Refresh hand ────────────────────────────
            if (handContainer && handChanged)
            {
                bool isLocal = playerIndex == LocalPlayer;
                var handGOs = new List<GameObject>(player.Hand.Count);
                var newHandGOs = new List<GameObject>(player.Hand.Count);
                var existingHandCards = GetExistingHandCardMap(handContainer);
                for (int i = 0; i < player.Hand.Count; i++)
                {
                    var cardInst = player.Hand[i];
                    string handCardId = GetHandCardRuntimeId(cardInst, i);
                    bool isNewCard = !existingHandCards.TryGetValue(handCardId, out var go);
                    if (isNewCard)
                        go = Instantiate(cardPrefab, handContainer);
                    else
                        existingHandCards.Remove(handCardId);

                    go.name = $"HandCard_{handCardId}";
                    go.transform.SetParent(handContainer, false);
                    go.transform.SetSiblingIndex(i);
                    ApplyCardPresentation(go, CardZoneStyle.Hand, i, player.Hand.Count, !isLocal);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                    {
                        if (isLocal)
                            visual.SetCard(cardInst.Card);
                        else
                            visual.SetFaceDown();
                    }
                    AttachCardHover(go);
                    RemoveNamedChild(go.transform, "PlayableHighlight");

                    // Make local hand cards clickable during main phases
                    if (isLocal)
                    {
                        int idx = i;
                        bool canAfford = player.Will >= cardInst.Card.GetWillCost();
                        bool canPlay = canAfford && CanPlayCard(player, cardInst.Card);
                        var btn = go.GetComponent<Button>();
                        if (btn == null) btn = go.AddComponent<Button>();
                        btn.onClick.RemoveAllListeners();
                        // Always allow click — during Main it plays/previews; other phases show inspect.
                        btn.onClick.AddListener(() => OnHandCardClicked(idx));

                        // Highlight playable cards
                        if (canPlay)
                            AddPlayableCardHighlight(go);

                        var drag = go.GetComponent<CardDrag>();
#pragma warning disable CS0162
                        if (UseDragDropHandPlay)
                        {
                            bool canDragPlay = handPlayable && canPlay && cardInst.Card is DaemonCardData;
                            if (drag == null) drag = go.AddComponent<CardDrag>();
                            drag.enabled = true;
                            drag.HandIndex = idx;
                            drag.EnableHover = false;
                            drag.IsPlayable = canDragPlay;
                            drag.AllowedZone = canDragPlay ? DropZone.DropZoneKind.Battlefield : DropZone.DropZoneKind.None;
                            drag.OnDroppedOnZone = OnCardDropped;
                        }
                        else if (drag != null)
                        {
                            drag.enabled = false;
                            drag.IsPlayable = false;
                            drag.AllowedZone = DropZone.DropZoneKind.None;
                        }
#pragma warning restore CS0162
                    }
                    handGOs.Add(go);
                    if (isNewCard)
                        newHandGOs.Add(go);
                }

                foreach (var staleCard in existingHandCards.Values)
                {
                    if (staleCard != null)
                        Destroy(staleCard);
                }

                // Position cards along bezier arc before starting slide-in animations
                ArrangeHandCurve(handContainer, player.Hand.Count, !isLocal);

                // Only animate truly new hand objects so re-fans do not fight a full rebuild.
                float handAnimDuration = 0f;
                for (int i = 0; i < newHandGOs.Count; i++)
                {
                    var handCard = newHandGOs[i];
                    var rt = handCard.GetComponent<RectTransform>();
                    var visual = handCard.GetComponent<CardVisual>();
                    if (rt == null || visual == null) continue;
                    handCard.transform.SetAsLastSibling();

                    bool isLastCard = i == newHandGOs.Count - 1 && handCountIncreased && _battle.State.Phase != GamePhase.Setup;
                    if (isLastCard)
                    {
                        // Arc from deck pile to card's final position
                        string pileName = isLocal ? "P1DeckPile" : "P2DeckPile";
                        var pileRT = FindRootRect(pileName);
                        if (pileRT != null)
                        {
                            Vector2 pileWorld = RectTransformUtility.WorldToScreenPoint(null, pileRT.position);
                            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                rt.parent as RectTransform, pileWorld, null, out Vector2 localFrom);
                            Vector2 finalPos = rt.anchoredPosition;
                            visual.StartCoroutine(UIAnimUtils.ArcDraw(rt, localFrom, finalPos, 0.5f));
                            handAnimDuration = Mathf.Max(handAnimDuration, 0.52f);
                            continue;
                        }
                    }
                    float slideDuration = 0.2f + i * 0.05f;
                    visual.StartCoroutine(UIAnimUtils.SlideIn(rt, new Vector2(0, -80), slideDuration));
                    handAnimDuration = Mathf.Max(handAnimDuration, slideDuration + 0.02f);
                }

                if (handAnimDuration > 0f)
                    StartCoroutine(ReapplyHandCurveAfterDelay(handContainer, player.Hand.Count, !isLocal, handAnimDuration));
            }

            // ─── Refresh field ───────────────────────────
            // Skip field rebuild while a summon animation is in-flight to prevent
            // destroying the slot the animation coroutine is fading in.
            if (fieldContainer && fieldChanged && !(playerIndex == LocalPlayer && _summonAnimInFlight))
            {
                var daemonRectMap = playerIndex == LocalPlayer ? _p1DaemonCardRects : _p2DaemonCardRects;
                daemonRectMap.Clear();
                SyncFieldSlotMap(player, playerIndex);
                var slotMap = GetFieldSlotMap(playerIndex);
                var daemonsBySlot = new DaemonInstance[GameConstants.MaxFieldDaemons];
                foreach (var daemon in player.Field)
                {
                    if (daemon == null || string.IsNullOrEmpty(daemon.InstanceId))
                        continue;
                    if (slotMap.TryGetValue(daemon.InstanceId, out int mappedSlot)
                        && mappedSlot >= 0
                        && mappedSlot < daemonsBySlot.Length)
                    {
                        daemonsBySlot[mappedSlot] = daemon;
                    }
                }

                ClearChildren(fieldContainer);
                Canvas.ForceUpdateCanvases();
                for (int i = 0; i < GameConstants.MaxFieldDaemons; i++)
                {
                    bool occupied = daemonsBySlot[i] != null;
                    RectTransform content = CreateFreeBoardSeat(fieldContainer, $"FieldSlot_{i}", BoardSocketStyle.Daemon,
                        i, GameConstants.MaxFieldDaemons, occupied, out var slot);

                    if (!occupied)
                    {
                        if (summonSeatMode)
                        {
                            int summonSlotIndex = i;
                            var slotImage = slot.GetComponent<Image>();
                            if (slotImage != null)
                                slotImage.raycastTarget = true;
                            var seatButton = slot.GetComponent<Button>() ?? slot.AddComponent<Button>();
                            seatButton.onClick.RemoveAllListeners();
                            seatButton.onClick.AddListener(() => ConfirmDaemonPlay(selectedSummonHandIndex, summonSlotIndex));
                            AddPulsingHighlightOverlay(slot, HighlightPlay, 0.14f, 0.38f, 2.8f);
                            SetSocketCaption(slot.transform as RectTransform, string.Empty, "summon here", new Color(0.98f, 0.90f, 0.74f, 0.92f));
                        }
                        continue;
                    }

                    var daemon = daemonsBySlot[i];
                    var go = Instantiate(daemonFieldPrefab ?? cardPrefab, content);
                    go.name = "Card";
                    ApplySocketCardPresentation(go, CardZoneStyle.Field, false);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                    {
                        visual.SetCard(daemon.Card);
                        visual.SetRuntimeMainStat(daemon.CurrentAshe);
                    }
                    if (!string.IsNullOrEmpty(daemon.InstanceId) && go.transform is RectTransform daemonRect)
                        daemonRectMap[daemon.InstanceId] = daemonRect;

                    if (!(playerIndex == LocalPlayer && i == _pendingLocalSummonSlot))
                        StartCoroutine(UIAnimUtils.PopScale(go.transform, 0.2f, 1.08f));
                    AttachCardHover(go);
                    AddStatOverlay(go, daemon, player.Will, player.AsheCards);
                    if (GetDisplayedKillCooldownTurns(daemon) > 0)
                        AddKillCooldownBadge(go, daemon);
                    if (daemon.Masks.Count > 0)
                        AddMaskBadge(go, daemon);
                    if (daemon.IsFusionApex)
                        AddFusionApexBadge(go, daemon);
                    if (player.AsheCards?.Count > 0)
                        AddAsheTrayBadge(go, daemon, player.AsheCards);

                    if (playerIndex == LocalPlayer && i == _pendingLocalSummonSlot)
                    {
                        var group = go.GetComponent<CanvasGroup>();
                        if (group == null)
                        {
                            try
                            {
                                group = go.AddComponent<CanvasGroup>();
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[Battle] Could not add CanvasGroup for summon fade on {go.name}: {ex.Message}");
                            }
                        }
                        if (group != null)
                            group.alpha = 0f;
                    }

                    // Summoning sickness indicator
                    if (!daemon.CanAttack && !daemon.Frozen && !daemon.Entangled)
                        AddSummoningSicknessOverlay(go);

                    int idx = i;
                    if (playerIndex == LocalPlayer && maskTargetMode)
                    {
                        int targetDaemonIndex = player.Field.FindIndex(d => d != null && d.InstanceId == daemon.InstanceId);
                        if (targetDaemonIndex >= 0)
                        {
                            var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                            btn.onClick.RemoveAllListeners();
                            btn.onClick.AddListener(() => ConfirmMaskPlayTarget(targetDaemonIndex));
                            AddPulsingHighlightOverlay(slot, new Color(0.75f, 0.40f, 0.92f, 1f), 0.10f, 0.45f, 3f);
                            SetSocketCaption(slot.transform as RectTransform, string.Empty, "mask target", new Color(0.96f, 0.86f, 0.98f, 0.95f));
                        }
                    }
                    else if (playerIndex == LocalPlayer && asheTargetMode)
                    {
                        int targetDaemonIndex = player.Field.FindIndex(d => d != null && d.InstanceId == daemon.InstanceId);
                        if (targetDaemonIndex >= 0 && _pendingAsheHandIndex >= 0 && _pendingAsheHandIndex < player.Hand.Count)
                        {
                            var asheCardData = player.Hand[_pendingAsheHandIndex].Card as AsheCardData;
                            if (asheCardData != null && asheCardData.Matches(daemon.Card))
                            {
                                var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                                btn.onClick.RemoveAllListeners();
                                btn.onClick.AddListener(() => ConfirmAshePlayTarget(targetDaemonIndex));
                                AddPulsingHighlightOverlay(slot, new Color(0.36f, 0.88f, 0.60f, 1f), 0.10f, 0.45f, 3f);
                                SetSocketCaption(slot.transform as RectTransform, string.Empty, "assign ashe", new Color(0.68f, 0.96f, 0.78f, 0.95f));
                            }
                        }
                    }
                    else if (playerIndex == LocalPlayer && fieldSelectable)
                    {
                        bool canAttackWithDaemon = daemon.CanAttack && !daemon.HasAttacked && !daemon.Frozen && !daemon.Entangled;
                        bool canFuseFromThisDaemon = fusionMode && CanDaemonStartFusion(player, idx);
                        bool canAct = canAttackWithDaemon || canFuseFromThisDaemon;
                        if (canAct)
                        {
                            var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                            btn.onClick.AddListener(() => OnOwnDaemonClicked(idx));
                            if (_selectedAttackerIndex == idx)
                            {
                                AddPulsingHighlightOverlay(slot, HighlightAttacker, 0.15f, 0.55f, 4f);
                                AddSelectTargetLabel(go);
                            }
                            else if (_battle.State.Phase == GamePhase.Combat && !_waitingForTarget)
                            {
                                // Subtle glow on all attackable daemons to indicate they can be clicked
                                AddPulsingHighlightOverlay(slot, new Color(0.78f, 0.66f, 0.42f, 1f), 0.05f, 0.20f, 2f);
                            }
                            else if (_selectedFusionPrimaryIndex == idx)
                            {
                                AddPulsingHighlightOverlay(slot, new Color(0.56f, 0.90f, 1f, 1f), 0.14f, 0.52f, 3.2f);
                                SetSocketCaption(slot.transform as RectTransform, string.Empty, "choose fusion pair", new Color(0.70f, 0.96f, 1f, 0.95f));
                            }
                            else if (fusionMode)
                            {
                                bool canCompletePair = _selectedFusionPrimaryIndex >= 0
                                    && _selectedFusionPrimaryIndex < player.Field.Count
                                    && _selectedFusionPrimaryIndex != idx
                                    && CanDaemonsFuse(player.Field[_selectedFusionPrimaryIndex], daemon);
                                if (canCompletePair || canFuseFromThisDaemon)
                                    AddPulsingHighlightOverlay(slot, new Color(0.46f, 0.86f, 0.95f, 1f), 0.06f, 0.24f, 2.1f);
                            }
                        }
                    }
                    else if (playerIndex == AIPlayerIndex)
                    {
                        // Always make opponent daemons clickable:
                        // in target mode they resolve the attack; otherwise they open an inspect.
                        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                        btn.onClick.AddListener(() => OnOpponentDaemonClicked(idx));
                        if (_waitingForTarget)
                        {
                            AddPulsingHighlightOverlay(slot, HighlightTarget, 0.10f, 0.50f, 3f);
                            // Show type-effectiveness badge so the player knows the matchup at a glance
                            AddEffectivenessLabel(go.transform as RectTransform ?? slot.transform as RectTransform, daemon);
                        }
                    }
                    else if (playerIndex == LocalPlayer)
                    {
                        // No special mode active — clicking inspects the daemon.
                        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                        if (btn.onClick.GetPersistentEventCount() == 0)
                            btn.onClick.AddListener(() => ShowFieldDaemonInspect(daemonsBySlot[idx]));
                    }
                }
            }

            // ─── Refresh pillars (compact side stack) ────────
            if (pillarContainer && pillarChanged)
            {
                ClearChildren(pillarContainer);
                Canvas.ForceUpdateCanvases();
                bool isLocal = playerIndex == LocalPlayer;

                int aliveCount = 0;
                int totalPillars = Mathf.Min(player.Pillars.Count, GameConstants.PillarCount);
                for (int i = 0; i < totalPillars; i++)
                    if (!player.Pillars[i].Destroyed) aliveCount++;

                if (aliveCount == 0)
                {
                    // All destroyed — show "SHATTERED" label
                    var emptySlot = CreateFreeBoardSeat(pillarContainer, "PillarStack_Empty", BoardSocketStyle.Pillar,
                        0, 1, false, out var eSlot);
                    SetSocketCaption(eSlot.transform as RectTransform, string.Empty, "shattered", new Color(0.82f, 0.50f, 0.42f, 0.84f));
                }
                else
                {
                    // Show the lead pillar with stacked shadows so the side-stack reads at a glance.
                    var topPillar = player.Pillars.First(p => !p.Destroyed);
                    RectTransform content = CreateFreeBoardSeat(pillarContainer, "PillarStack_Top", BoardSocketStyle.Pillar,
                        0, 1, true, out var slot);

                    int shadowCount = Mathf.Clamp(aliveCount - 1, 0, 3);
                    for (int shadowIndex = shadowCount; shadowIndex >= 1; shadowIndex--)
                    {
                        var shadow = Instantiate(pillarPrefab ?? cardPrefab, content);
                        shadow.name = $"Shadow_{shadowIndex}";
                        ApplySocketCardPresentation(shadow, CardZoneStyle.Pillar, true);

                        var shadowRt = shadow.GetComponent<RectTransform>();
                        if (shadowRt != null)
                        {
                            shadowRt.anchoredPosition += new Vector2(-shadowIndex * 6f, shadowIndex * 5f);
                            shadowRt.localScale *= 0.94f - shadowIndex * 0.04f;
                        }

                        var shadowVisual = shadow.GetComponent<CardVisual>();
                        if (shadowVisual != null)
                            shadowVisual.SetFaceDown();

                        CanvasGroup shadowCanvas = shadow.GetComponent<CanvasGroup>();
                        if (shadowCanvas == null)
                        {
                            try
                            {
                                shadowCanvas = shadow.AddComponent<CanvasGroup>();
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"[Battle] Could not add CanvasGroup to pillar stack shadow {shadow.name}: {ex.Message}");
                            }
                        }

                        if (shadowCanvas != null)
                            shadowCanvas.alpha = Mathf.Clamp01(0.34f - shadowIndex * 0.05f);
                    }

                    var go = Instantiate(pillarPrefab ?? cardPrefab, content);
                    go.name = "Card";
                    ApplySocketCardPresentation(go, CardZoneStyle.Pillar, !isLocal && !topPillar.Revealed);
                    var topRect = go.GetComponent<RectTransform>();
                    if (topRect != null)
                        topRect.anchoredPosition += new Vector2(6f, -4f);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                    {
                        if (isLocal || topPillar.Revealed)
                            visual.SetCard(topPillar.Card);
                        else
                            visual.SetFaceDown();
                    }

                    AttachCardHover(go);

                    // Clicking the pillar stack opens an inspect overlay.
                    bool canInspect = isLocal || topPillar.Revealed;
                    if (canInspect)
                    {
                        var pillarBtn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                        pillarBtn.onClick.AddListener(() => ShowPillarInspect(topPillar, aliveCount, player.Pillars));
                    }

                    // Badge showing remaining pillar count
                    string badge = aliveCount > 1 ? $"x{aliveCount}" : "";
                    string pillarHp = topPillar.Revealed ? $"{topPillar.CurrentHp}/{topPillar.MaxHp}" : "?";
                    SetSocketCaption(slot.transform as RectTransform, badge, pillarHp, new Color(0.78f, 0.66f, 0.42f, 1f));
                }
            }
        }

        /// <summary>Opens a read-only inspect overlay for a pillar stack.</summary>
        private void ShowPillarInspect(PillarInstance top, int aliveCount, System.Collections.Generic.List<PillarInstance> allPillars)
        {
            DismissCardPreview();

            var canvasTransform = (transform as RectTransform) ?? transform;
            _cardPreviewOverlay = new GameObject("PillarInspectOverlay");
            _cardPreviewOverlay.transform.SetParent(canvasTransform, false);
            var overlayRT = _cardPreviewOverlay.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            overlayRT.SetAsLastSibling();
            var overlayImg = _cardPreviewOverlay.AddComponent<Image>();
            overlayImg.color = new Color(0.02f, 0.02f, 0.04f, 0.62f);
            overlayImg.raycastTarget = true;
            var cancelBtn = _cardPreviewOverlay.AddComponent<Button>();
            cancelBtn.onClick.AddListener(DismissCardPreview);

            // Large pillar card
            var cardGO = Instantiate(pillarPrefab ?? cardPrefab, _cardPreviewOverlay.transform);
            var cardRT = cardGO.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.22f, 0.28f);
            cardRT.anchorMax = new Vector2(0.78f, 0.86f);
            cardRT.offsetMin = Vector2.zero;
            cardRT.offsetMax = Vector2.zero;
            cardRT.localScale = Vector3.one;
            var visual = cardGO.GetComponent<CardVisual>();
            if (visual != null) visual.SetCard(top.Card);
            var cardBtn = cardGO.GetComponent<Button>() ?? cardGO.AddComponent<Button>();
            cardBtn.onClick.AddListener(() => { });

            // HP / Loyalty bar
            var statsGO = new GameObject("PillarStats");
            statsGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var statsRT = statsGO.AddComponent<RectTransform>();
            statsRT.anchorMin = new Vector2(0.15f, 0.21f);
            statsRT.anchorMax = new Vector2(0.85f, 0.28f);
            statsRT.offsetMin = Vector2.zero;
            statsRT.offsetMax = Vector2.zero;
            var statsTMP = statsGO.AddComponent<TextMeshProUGUI>();
            statsTMP.text = $"HP  {top.CurrentHp} / {top.MaxHp}     Loyalty  {top.Loyalty}     Pillars remaining  {aliveCount}";
            statsTMP.fontSize = 14;
            statsTMP.color = new Color(0.78f, 0.88f, 1f);
            statsTMP.alignment = TextAlignmentOptions.Center;
            statsTMP.raycastTarget = false;

            // Passive ability text
            string abilityText = top.Card.passiveAbility;
            if (string.IsNullOrEmpty(abilityText)) abilityText = top.Card.description;
            var descGO = new GameObject("PillarDesc");
            descGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var descRT = descGO.AddComponent<RectTransform>();
            descRT.anchorMin = new Vector2(0.12f, 0.13f);
            descRT.anchorMax = new Vector2(0.88f, 0.21f);
            descRT.offsetMin = Vector2.zero;
            descRT.offsetMax = Vector2.zero;
            var descTMP = descGO.AddComponent<TextMeshProUGUI>();
            descTMP.text = string.IsNullOrEmpty(abilityText) ? top.Card.cardName : abilityText;
            descTMP.fontSize = 13;
            descTMP.color = new Color(0.88f, 0.84f, 0.78f);
            descTMP.alignment = TextAlignmentOptions.Center;
            descTMP.raycastTarget = false;

            // List remaining un-revealed pillars
            var unrevealed = allPillars
                .Where(p => !p.Destroyed && p != top && !p.Revealed)
                .Count();
            if (unrevealed > 0)
            {
                var hintGO = new GameObject("UnrevealedHint");
                hintGO.transform.SetParent(_cardPreviewOverlay.transform, false);
                var hintRT = hintGO.AddComponent<RectTransform>();
                hintRT.anchorMin = new Vector2(0.25f, 0.08f);
                hintRT.anchorMax = new Vector2(0.75f, 0.13f);
                hintRT.offsetMin = Vector2.zero;
                hintRT.offsetMax = Vector2.zero;
                var hintTMP = hintGO.AddComponent<TextMeshProUGUI>();
                hintTMP.text = $"+{unrevealed} unrevealed pillar{(unrevealed > 1 ? "s" : "")} behind this one";
                hintTMP.fontSize = 11;
                hintTMP.color = new Color(0.55f, 0.52f, 0.48f);
                hintTMP.alignment = TextAlignmentOptions.Center;
                hintTMP.raycastTarget = false;
            }

            var closeGO = new GameObject("CloseHint");
            closeGO.transform.SetParent(_cardPreviewOverlay.transform, false);
            var closeRT = closeGO.AddComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(0.25f, 0.04f);
            closeRT.anchorMax = new Vector2(0.75f, 0.09f);
            closeRT.offsetMin = Vector2.zero;
            closeRT.offsetMax = Vector2.zero;
            var closeTMP = closeGO.AddComponent<TextMeshProUGUI>();
            closeTMP.text = "Tap anywhere to close";
            closeTMP.fontSize = 11;
            closeTMP.color = new Color(0.45f, 0.42f, 0.40f);
            closeTMP.alignment = TextAlignmentOptions.Center;
            closeTMP.raycastTarget = false;

            StartCoroutine(UIAnimUtils.PopScale(overlayRT, 0.15f, 1.02f));
        }

        // ─── Content Hashing (skip unnecessary UI rebuilds) ──
        private static int ComputeHandHash(PlayerState player)
        {
            int hash = player.Hand.Count * 397 + player.Will;
            for (int i = 0; i < player.Hand.Count; i++)
                hash = hash * 31 + player.Hand[i].Card.cardName.GetHashCode();
            return hash;
        }

        private static Dictionary<string, GameObject> GetExistingHandCardMap(Transform handContainer)
        {
            var existing = new Dictionary<string, GameObject>();
            if (handContainer == null)
                return existing;

            for (int i = 0; i < handContainer.childCount; i++)
            {
                var child = handContainer.GetChild(i);
                if (child == null)
                    continue;

                string key = GetHandCardRuntimeId(child.gameObject.name);
                if (!string.IsNullOrEmpty(key) && !existing.ContainsKey(key))
                    existing.Add(key, child.gameObject);
            }

            return existing;
        }

        private static string GetHandCardRuntimeId(CardInstance cardInst, int index)
        {
            if (cardInst == null)
                return $"unknown_{index}";

            return !string.IsNullOrEmpty(cardInst.InstanceId)
                ? cardInst.InstanceId
                : $"{cardInst.Card?.cardName ?? "card"}_{index}";
        }

        private static string GetHandCardRuntimeId(string objectName)
        {
            const string prefix = "HandCard_";
            return !string.IsNullOrEmpty(objectName) && objectName.StartsWith(prefix, StringComparison.Ordinal)
                ? objectName.Substring(prefix.Length)
                : null;
        }

        private static int ComputeFieldHash(PlayerState player, bool selectable, bool summonSeatMode, bool maskTargetMode,
            bool asheTargetMode, bool fusionMode, bool targetMode, int selectedAttackerIndex, int selectedFusionPrimaryIndex)
        {
            int hash = player.Field.Count * 397
                + (selectable ? 1 : 0)
                + (summonSeatMode ? 7 : 0)
                + (maskTargetMode ? 19 : 0)
                + (asheTargetMode ? 37 : 0)
                + (fusionMode ? 23 : 0)
                + (targetMode ? 13 : 0)
                + (selectedAttackerIndex + 5) * 17
                + (selectedFusionPrimaryIndex + 7) * 29
                + (player.AsheCards?.Count ?? 0) * 11;
            for (int i = 0; i < player.Field.Count; i++)
            {
                var d = player.Field[i];
                hash = hash * 31 + d.Card.cardName.GetHashCode();
                hash = hash * 31 + d.CurrentAshe;
                hash = hash * 31 + (d.CanAttack ? 1 : 0) + (d.HasAttacked ? 2 : 0) + (d.Frozen ? 4 : 0);
                hash = hash * 31 + d.Masks.Count;
                hash = hash * 31 + (d.IsFusionApex ? 1 : 0) + d.FusionTurnsRemaining * 3;
            }
            return hash;
        }

        private void HandleFusionSelectionClick(PlayerState player, int fieldIndex)
        {
            if (!CanAttemptFusion(player))
                return;

            if (_selectedFusionPrimaryIndex < 0)
            {
                if (!CanDaemonStartFusion(player, fieldIndex))
                    return;

                _selectedFusionPrimaryIndex = fieldIndex;
                RefreshUI(_battle.State);
                return;
            }

            if (_selectedFusionPrimaryIndex == fieldIndex)
            {
                _selectedFusionPrimaryIndex = -1;
                RefreshUI(_battle.State);
                return;
            }

            int primary = _selectedFusionPrimaryIndex;
            if (primary < 0 || primary >= player.Field.Count)
            {
                _selectedFusionPrimaryIndex = -1;
                RefreshUI(_battle.State);
                return;
            }

            if (!CanDaemonsFuse(player.Field[primary], player.Field[fieldIndex]))
            {
                if (CanDaemonStartFusion(player, fieldIndex))
                {
                    _selectedFusionPrimaryIndex = fieldIndex;
                    RefreshUI(_battle.State);
                }
                return;
            }

            bool fused = SubmitPlayerAction(new FuseDaemonsAction
            {
                PrimaryIndex = primary,
                SecondaryIndex = fieldIndex,
            });

            _selectedFusionPrimaryIndex = -1;
            if (!fused)
                RefreshUI(_battle.State);
        }

        private static bool CanAttemptFusion(PlayerState player)
        {
            if (player == null || player.Field.Count < 2)
                return false;
            if (!HasFusionSealInHand(player))
                return false;

            return player.Field
                .Where(d => !d.IsFusionApex)
                .GroupBy(GetFusionCardKey)
                .Any(g => g.Count() >= 2);
        }

        private static bool CanDaemonStartFusion(PlayerState player, int daemonIndex)
        {
            if (player == null || daemonIndex < 0 || daemonIndex >= player.Field.Count)
                return false;

            var daemon = player.Field[daemonIndex];
            if (daemon.IsFusionApex)
                return false;

            var element = daemon.Card.element;
            for (int i = 0; i < player.Field.Count; i++)
            {
                if (i == daemonIndex)
                    continue;
                if (CanDaemonsFuse(daemon, player.Field[i]))
                    return true;
            }

            return false;
        }

        private static bool HasFusionSealInHand(PlayerState player)
        {
            return player?.Hand != null && player.Hand.Any(card => card.Card != null && card.Card.category == CardCategory.Seal && card.Card.cardId == "fusion_seal");
        }

        private static bool CanDaemonsFuse(DaemonInstance first, DaemonInstance second)
        {
            if (first?.Card == null || second?.Card == null)
                return false;

            string firstKey = GetFusionCardKey(first);
            string secondKey = GetFusionCardKey(second);
            return !string.IsNullOrEmpty(firstKey) && string.Equals(firstKey, secondKey, StringComparison.Ordinal);
        }

        private static string GetFusionCardKey(DaemonInstance daemon)
        {
            if (daemon?.Card == null)
                return string.Empty;

            return !string.IsNullOrEmpty(daemon.Card.cardId)
                ? daemon.Card.cardId
                : daemon.Card.cardName ?? string.Empty;
        }

        private static int ComputePillarHash(PlayerState player, bool targetMode)
        {
            int hash = player.Pillars.Count * 397 + (targetMode ? 11 : 0);
            for (int i = 0; i < player.Pillars.Count; i++)
            {
                var p = player.Pillars[i];
                hash = hash * 31 + (p.Destroyed ? 1 : 0) + (p.Revealed ? 2 : 0);
            }
            return hash;
        }

        private void RefreshInvokerAnchor(PlayerState player, int playerIndex, bool activeTurn, bool targetable)
        {
            RectTransform zone = FindRootRect(playerIndex == LocalPlayer ? "P1InvokerZone" : "P2InvokerZone");
            if (zone == null)
                return;

            SuppressLegacyInvokerContents(zone);
            float layoutT = GetBattlefieldLayoutT();
            int intactPillars = CountIntactPillars(player);
            InvokerCardData invokerCard = GetInvokerCardData(playerIndex);
            Sprite invokerPortrait = ResolveCardArtwork(invokerCard);
            Color accent = targetable
                ? new Color(1f, 0.57f, 0.38f)
                : activeTurn
                    ? new Color(0.95f, 0.86f, 0.58f)
                    : new Color(0.74f, 0.82f, 0.96f);

            Transform existing = zone.Find("RuntimeInvokerAnchor");
            GameObject anchor = existing != null ? existing.gameObject : new GameObject("RuntimeInvokerAnchor", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            if (existing == null)
                anchor.transform.SetParent(zone, false);

            var layout = anchor.GetComponent<LayoutElement>();
            layout.ignoreLayout = true;

            var anchorRect = anchor.GetComponent<RectTransform>();
            anchorRect.anchorMin = Vector2.zero;
            anchorRect.anchorMax = Vector2.one;
            anchorRect.offsetMin = Vector2.zero;
            anchorRect.offsetMax = Vector2.zero;

            var anchorImage = anchor.GetComponent<Image>();
            anchorImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            anchorImage.type = anchorImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            anchorImage.color = activeTurn
                ? new Color(0.08f, 0.09f, 0.12f, 0.03f)
                : new Color(0.06f, 0.06f, 0.08f, 0.015f);
            anchorImage.raycastTarget = false;

            Transform existingCard = anchor.transform.Find("Card");
            GameObject card = existingCard != null ? existingCard.gameObject : new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Outline));
            if (existingCard == null)
                card.transform.SetParent(anchor.transform, false);

            var cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = new Vector2(0f, -12f);
            cardRect.sizeDelta = new Vector2(Mathf.Lerp(188f, 212f, layoutT), Mathf.Lerp(92f, 104f, layoutT));
            cardRect.localScale = Vector3.one;

            var cardImage = card.GetComponent<Image>();
            cardImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            cardImage.type = cardImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            cardImage.color = activeTurn
                ? new Color(0.09f, 0.09f, 0.11f, 0.90f)
                : new Color(0.08f, 0.08f, 0.10f, 0.84f);

            var cardOutline = card.GetComponent<Outline>();
            Color elemColor = invokerCard != null
                ? CardVisual.GetElementColor(invokerCard.element)
                : new Color(0.78f, 0.68f, 0.44f, 1f);
            cardOutline.effectColor = targetable
                ? new Color(1f, 0.55f, 0.30f, 0.92f)
                : activeTurn
                    ? new Color(elemColor.r, elemColor.g, elemColor.b, 0.78f)
                    : new Color(0f, 0f, 0f, 0.38f);
            cardOutline.effectDistance = targetable ? new Vector2(4f, -4f) : activeTurn ? new Vector2(3f, -3f) : new Vector2(2f, -2f);

            foreach (string legacyName in new[] { "Title", "Kind", "Health", "Shield", "Sigil" })
            {
                Transform legacy = cardRect.Find(legacyName);
                if (legacy != null)
                    legacy.gameObject.SetActive(false);
            }

            EnsureInvokerBadgeImage(cardRect, "Header", "UI/panel-header",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(cardRect.sizeDelta.x - 4f, 17f),
                new Color(elemColor.r, elemColor.g, elemColor.b, targetable || activeTurn ? 0.88f : 0.46f));
            EnsureInvokerBadgeText(cardRect, "Name", (invokerCard != null ? invokerCard.cardName : player.Name).ToUpperInvariant(),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -1f), new Vector2(cardRect.sizeDelta.x - 16f, 14f),
                9f, new Color(0.10f, 0.08f, 0.06f));

            RectTransform portraitFrame = EnsureInvokerBadgeImage(cardRect, "PortraitFrame", "UI/frame-invoker",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-62f, 1f), new Vector2(Mathf.Lerp(50f, 58f, layoutT), Mathf.Lerp(50f, 58f, layoutT)),
                new Color(elemColor.r, elemColor.g, elemColor.b, activeTurn ? 0.92f : 0.60f), false);
            var portraitFrameOutline = portraitFrame.GetComponent<Outline>() ?? portraitFrame.gameObject.AddComponent<Outline>();
            portraitFrameOutline.effectColor = new Color(elemColor.r, elemColor.g, elemColor.b, 0.60f);
            portraitFrameOutline.effectDistance = new Vector2(2f, -2f);

            EnsureInvokerBadgeSprite(portraitFrame, "Portrait", invokerPortrait,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(Mathf.Lerp(38f, 44f, layoutT), Mathf.Lerp(38f, 44f, layoutT)),
                new Color(1f, 1f, 1f, invokerPortrait != null ? 1f : 0f));

            RectTransform core = EnsureInvokerBadgeImage(cardRect, "Core", "UI/frame-invoker",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 1f), new Vector2(Mathf.Lerp(66f, 76f, layoutT), Mathf.Lerp(56f, 64f, layoutT)),
                activeTurn ? new Color(0.12f, 0.055f, 0.060f, 0.96f) : new Color(0.075f, 0.070f, 0.095f, 0.94f), false);
            var coreOutline = core.GetComponent<Outline>() ?? core.gameObject.AddComponent<Outline>();
            coreOutline.effectColor = activeTurn
                ? new Color(elemColor.r, elemColor.g, elemColor.b, 0.40f)
                : new Color(0f, 0f, 0f, 0.28f);
            coreOutline.effectDistance = new Vector2(2f, -2f);

            EnsureInvokerBadgeText(core, "HealthValue", $"{player.Invoker.Hp}/{player.Invoker.MaxHp}",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 7f), new Vector2(68f, 26f),
                23f, new Color(1f, 0.96f, 0.72f));
            EnsureInvokerBadgeText(core, "HealthLabel", "HP",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -13f), new Vector2(44f, 11f),
                9f, new Color(0.98f, 0.88f, 0.62f), FontStyles.Bold, TextAlignmentOptions.Center);

            EnsureInvokerBadgeImage(cardRect, "StoredIcon", "UI/icon-mana",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(66f, 10f), new Vector2(14f, 14f),
                new Color(0.58f, 0.84f, 1f, 0.95f), false);
            EnsureInvokerBadgeText(cardRect, "StoredValue", player.Will.ToString(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(66f, -8f), new Vector2(26f, 14f),
                12f, new Color(0.88f, 0.96f, 1f));
            EnsureInvokerBadgeText(cardRect, "StoredLabel", "SE",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(66f, -23f), new Vector2(32f, 11f),
                7.5f, new Color(0.56f, 0.80f, 0.92f), FontStyles.Bold, TextAlignmentOptions.Center);

            EnsureInvokerBadgeImage(cardRect, "PillarIcon", "UI/icon-shield",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(91f, 10f), new Vector2(14f, 14f),
                new Color(0.96f, 0.84f, 0.56f, 0.95f), false);
            EnsureInvokerBadgeText(cardRect, "PillarValue", intactPillars.ToString(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(91f, -8f), new Vector2(26f, 14f),
                12f, new Color(0.97f, 0.95f, 0.88f));
            EnsureInvokerBadgeText(cardRect, "PillarLabel", "PILs",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(91f, -23f), new Vector2(36f, 11f),
                7.5f, new Color(0.88f, 0.80f, 0.62f), FontStyles.Bold, TextAlignmentOptions.Center);

            bool canUnleash = playerIndex == LocalPlayer
                && _battle != null
                && _battle.State != null
                && !_battle.State.GameOver
                && _battle.State.CurrentPlayer == LocalPlayer
                && _battle.State.Phase == GamePhase.Main
                && !player.InvokerAbilityUsedThisTurn;
            bool hasFusionResonance = player.Field
                .GroupBy(d => d.Card.element)
                .Any(g => g.Count() >= 2);

            string resonanceLabel = hasFusionResonance
                ? $"RESONANCE +{GameConstants.InvokerFusionResonanceBonus}"
                : string.Empty;
            EnsureInvokerBadgeText(cardRect, "Resonance", resonanceLabel,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(cardRect.sizeDelta.x - 28f, 8f),
                6.5f, hasFusionResonance ? new Color(0.92f, 0.98f, 1f) : new Color(0.82f, 0.84f, 0.88f, 0f),
                FontStyles.Bold, TextAlignmentOptions.Center);

            string footer = targetable
                ? "INVOKER EXPOSED"
                : canUnleash
                    ? $"UNLEASH ({GameConstants.InvokerUnleashCost} SE)"
                    : activeTurn
                        ? "YOUR INVOKER"
                        : string.Empty;
            EnsureInvokerBadgeText(cardRect, "Footer", footer,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 3f), new Vector2(cardRect.sizeDelta.x - 18f, 11f),
                7f, targetable
                    ? new Color(1f, 0.72f, 0.60f)
                    : canUnleash
                        ? new Color(1f, 0.88f, 0.54f)
                        : new Color(0.82f, 0.84f, 0.88f),
                FontStyles.Bold, TextAlignmentOptions.Center);

            var button = card.GetComponent<Button>();
            if (targetable)
            {
                button ??= card.AddComponent<Button>();
                button.enabled = true;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnOpponentInvokerClicked);
            }
            else if (canUnleash)
            {
                button ??= card.AddComponent<Button>();
                button.enabled = true;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnOwnInvokerClicked);
            }
            else if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.enabled = false;
            }
        }

        private void RefreshSideZones(GameState state)
        {
            RefreshDeckPile(FindRootRect("P1DeckPile"), "DECK", state.Players[LocalPlayer].Deck.Count);
            RefreshDeckPile(FindRootRect("P2DeckPile"), "DECK", state.Players[AIPlayerIndex].Deck.Count);
            RefreshVoidPile(FindRootRect("P1VoidZone"), "VOID", state.Players[LocalPlayer].AshePile);
            RefreshVoidPile(FindRootRect("P2VoidZone"), "VOID", state.Players[AIPlayerIndex].AshePile);
            RefreshSealPool(FindRootRect("P1SealZone"), "SEAL WARD", state.Players[LocalPlayer].SealZone.Count);
            RefreshSealPool(FindRootRect("P2SealZone"), "SEAL WARD", state.Players[AIPlayerIndex].SealZone.Count);
            RefreshDomainZone(FindRootRect("ActiveDomainZone"), state.ActiveDomain);
        }

        private void RefreshDomainZone(RectTransform zone, ActiveDomain activeDomain)
        {
            if (zone == null)
                return;

            RectTransform content = PreparePileContent(zone);
            ClearChildren(content);

            string label = string.Empty;
            if (activeDomain?.Card != null)
            {
                label = activeDomain.Card.cardName;
                if (activeDomain.TurnsRemaining > 0)
                    label += $"  ({activeDomain.TurnsRemaining}T)";
            }
            ConfigurePileLabels(zone, "DOMAIN", label);

            var bg = zone.GetComponent<Image>();
            if (bg == null) bg = zone.gameObject.AddComponent<Image>();
            bg.color = activeDomain?.Card != null
                ? new Color(0.055f, 0.082f, 0.120f, 0.84f)
                : new Color(0.046f, 0.060f, 0.086f, 0.72f);
            bg.raycastTarget = false;

            var ol = zone.GetComponent<Outline>();
            if (ol == null) ol = zone.gameObject.AddComponent<Outline>();
            ol.effectColor = activeDomain?.Card != null
                ? new Color(0.54f, 0.72f, 1f, 0.62f)
                : new Color(0.42f, 0.54f, 0.72f, 0.40f);
            ol.effectDistance = new Vector2(2f, -2f);
            EnsureSocketLabel(zone, "DomainStatus",
                activeDomain?.Card != null ? "WORLD EFFECT ACTIVE" : "NO ACTIVE DOMAIN",
                new Vector2(4f, 14f), new Vector2(-4f, 30f), 8.5f, FontStyles.Bold,
                new Color(0.72f, 0.84f, 1f, 0.86f), TextAlignmentOptions.Bottom);

            if (activeDomain?.Card == null)
            {
                CreatePilePlaceholder(content, string.Empty);
                return;
            }

            var card = Instantiate(cardPrefab, content);
            card.name = "ActiveDomainCard";
            ApplyMiniPileCard(card, Vector2.zero, false);
            var visual = card.GetComponent<CardVisual>();
            if (visual != null)
                visual.SetCard(activeDomain.Card);
        }

        private static float GetBattlefieldLayoutT()
        {
            return Mathf.InverseLerp(740f, 1180f, Screen.height);
        }

        private static float GetHandCardScale()
        {
            return Mathf.Lerp(0.80f, 0.92f, GetBattlefieldLayoutT());
        }

        private IEnumerator ReapplyHandCurveAfterDelay(Transform container, int cardCount, bool isOpponent, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);

            if (container == null || !isActiveAndEnabled)
                yield break;

            for (int i = 0; i < 3; i++)
            {
                ArrangeHandCurve(container, cardCount, isOpponent);
                yield return null;

                if (container == null || !isActiveAndEnabled)
                    yield break;
            }
        }

        private Dictionary<string, int> GetFieldSlotMap(int playerIndex)
        {
            return playerIndex == LocalPlayer ? _p1FieldSlotMap : _p2FieldSlotMap;
        }

        private void SyncFieldSlotMap(PlayerState player, int playerIndex)
        {
            var slotMap = GetFieldSlotMap(playerIndex);
            if (player == null)
            {
                slotMap.Clear();
                return;
            }

            var validIds = new HashSet<string>();
            foreach (var daemon in player.Field)
            {
                if (daemon != null && !string.IsNullOrEmpty(daemon.InstanceId))
                    validIds.Add(daemon.InstanceId);
            }

            foreach (string staleId in slotMap.Keys.Where(id => !validIds.Contains(id)).ToList())
                slotMap.Remove(staleId);

            var usedSlots = new HashSet<int>();
            foreach (var daemon in player.Field)
            {
                if (daemon == null || string.IsNullOrEmpty(daemon.InstanceId))
                    continue;

                if (slotMap.TryGetValue(daemon.InstanceId, out int mappedSlot)
                    && mappedSlot >= 0
                    && mappedSlot < GameConstants.MaxFieldDaemons
                    && usedSlots.Add(mappedSlot))
                {
                    continue;
                }

                int nextOpenSlot = FindFirstAvailableFieldSlot(usedSlots);
                if (nextOpenSlot < 0)
                    continue;

                slotMap[daemon.InstanceId] = nextOpenSlot;
                usedSlots.Add(nextOpenSlot);
            }
        }

        private void AssignNewestDaemonToSlot(PlayerState player, int playerIndex, int slotIndex)
        {
            if (player == null || player.Field.Count == 0)
                return;

            var newestDaemon = player.Field[player.Field.Count - 1];
            if (newestDaemon == null || string.IsNullOrEmpty(newestDaemon.InstanceId))
                return;

            var slotMap = GetFieldSlotMap(playerIndex);
            foreach (string occupiedId in slotMap.Where(entry => entry.Value == slotIndex && entry.Key != newestDaemon.InstanceId).Select(entry => entry.Key).ToList())
                slotMap.Remove(occupiedId);

            slotMap[newestDaemon.InstanceId] = Mathf.Clamp(slotIndex, 0, GameConstants.MaxFieldDaemons - 1);
            SyncFieldSlotMap(player, playerIndex);
        }

        private static int FindFirstAvailableFieldSlot(IEnumerable<int> usedSlots)
        {
            var used = usedSlots != null ? new HashSet<int>(usedSlots) : new HashSet<int>();
            for (int slotIndex = 0; slotIndex < GameConstants.MaxFieldDaemons; slotIndex++)
            {
                if (!used.Contains(slotIndex))
                    return slotIndex;
            }

            return -1;
        }

        private DeckData GetDeckForPlayer(int playerIndex)
        {
            return playerIndex == LocalPlayer ? player1Deck : player2Deck;
        }

        private InvokerCardData GetInvokerCardData(int playerIndex)
        {
            if (cardDatabase == null || cardDatabase.invokers == null || cardDatabase.invokers.Length == 0)
                return null;

            DeckData deck = GetDeckForPlayer(playerIndex);
            InvokerCardData fallback = null;
            for (int i = 0; i < cardDatabase.invokers.Length; i++)
            {
                InvokerCardData invoker = cardDatabase.invokers[i];
                if (invoker == null)
                    continue;

                fallback ??= invoker;
                if (deck != null && invoker.element == deck.element)
                    return invoker;
            }

            return fallback;
        }

        private static Sprite ResolveCardArtwork(CardData card)
        {
            if (card == null)
                return null;

            if (card.fullArt != null)
                return card.fullArt;
            if (card.artwork != null)
                return card.artwork;

            if (!string.IsNullOrEmpty(card.cardId))
            {
                Sprite loaded = Resources.Load<Sprite>($"CardArt/{card.cardId}");
                if (loaded != null)
                    return loaded;
            }

            return null;
        }

        private void BringForegroundZonesToFront()
        {
            RectTransform opponentInvoker = FindRootRect("P2InvokerZone");
            RectTransform playerInvoker = FindRootRect("P1InvokerZone");
            RectTransform p1SealZone = FindRootRect("P1SealZone");
            RectTransform p2SealZone = FindRootRect("P2SealZone");
            RectTransform activeDomainZone = FindRootRect("ActiveDomainZone");

            if (p2HandContainer != null)
                p2HandContainer.SetAsLastSibling();
            if (p1HandContainer != null)
                p1HandContainer.SetAsLastSibling();
            if (opponentInvoker != null)
                opponentInvoker.SetAsLastSibling();
            if (playerInvoker != null)
                playerInvoker.SetAsLastSibling();
            if (p2SealZone != null)
                p2SealZone.SetAsLastSibling();
            if (p1SealZone != null)
                p1SealZone.SetAsLastSibling();
            if (activeDomainZone != null)
                activeDomainZone.SetAsLastSibling();
            if (_cardPreviewOverlay != null)
                _cardPreviewOverlay.transform.SetAsLastSibling();
            if (_combatFxLayer != null)
                _combatFxLayer.SetAsLastSibling();
            if (_combatTextLayer != null)
                _combatTextLayer.SetAsLastSibling();
            if (_battleDialoguePanel != null && _battleDialoguePanel.activeSelf)
                _battleDialoguePanel.transform.SetAsLastSibling();
            if (gameOverPanel != null && gameOverPanel.activeSelf)
                gameOverPanel.transform.SetAsLastSibling();
        }

        private RectTransform CreateBoardSocket(Transform parent, string name, BoardSocketStyle style, string title, string subtitle, float alpha, out GameObject slot)
        {
            // Zone-based layout: transparent slot — cards lay directly on the stone board.
            slot = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(Outline));
            slot.transform.SetParent(parent, false);
            float layoutT = GetBattlefieldLayoutT();
            bool isDaemon = style == BoardSocketStyle.Daemon;

            var rect = slot.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;

            var image = slot.GetComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            image.raycastTarget = false;

            var outline = slot.GetComponent<Outline>();
            outline.effectColor = Color.clear;
            outline.effectDistance = new Vector2(1f, -1f);

            var layout = slot.GetComponent<LayoutElement>();
            switch (style)
            {
                case BoardSocketStyle.Daemon:
                    Vector2 daemonSlotSize = ResolveDaemonSlotSize(parent);
                    layout.preferredWidth = daemonSlotSize.x;
                    layout.preferredHeight = daemonSlotSize.y;
                    break;
                case BoardSocketStyle.Pillar:
                    Vector2 pillarSlotSize = new(PillarPileSlotW, PillarPileSlotH);
                    layout.preferredWidth = pillarSlotSize.x;
                    layout.preferredHeight = pillarSlotSize.y;
                    break;
                default:
                    layout.preferredWidth = Mathf.Lerp(78f, 86f, layoutT);
                    layout.preferredHeight = Mathf.Lerp(112f, 124f, layoutT);
                    break;
            }
            layout.minWidth = layout.preferredWidth;
            layout.minHeight = layout.preferredHeight;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
            rect.sizeDelta = new Vector2(layout.preferredWidth, layout.preferredHeight);

            // Minimal hierarchy: Seat → InnerSeat → Content (for compatibility with code that finds "Seat/InnerSeat")
            var seat = new GameObject("Seat", typeof(RectTransform));
            seat.transform.SetParent(slot.transform, false);
            var seatRect = seat.GetComponent<RectTransform>();
            seatRect.anchorMin = Vector2.zero;
            seatRect.anchorMax = Vector2.one;
            seatRect.offsetMin = Vector2.zero;
            seatRect.offsetMax = Vector2.zero;

            var innerSeat = new GameObject("InnerSeat", typeof(RectTransform));
            innerSeat.transform.SetParent(seat.transform, false);
            var innerRect = innerSeat.GetComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = Vector2.zero;
            innerRect.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(innerSeat.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            Color socketColor = GetSocketColor(style, alpha);
            Color frameColor = style == BoardSocketStyle.Daemon
                ? new Color(1f, 0.84f, 0.56f, Mathf.Lerp(0.045f, 0.13f, Mathf.Clamp01(alpha)))
                : new Color(0.76f, 0.88f, 1f, Mathf.Lerp(0.05f, 0.13f, Mathf.Clamp01(alpha)));
            Color haloColor = style == BoardSocketStyle.Daemon
                ? new Color(1f, 0.76f, 0.34f, Mathf.Lerp(0.025f, 0.08f, Mathf.Clamp01(alpha)))
                : new Color(0.54f, 0.76f, 1f, Mathf.Lerp(0.025f, 0.08f, Mathf.Clamp01(alpha)));

            Transform haloExisting = innerSeat.transform.Find("SeatHalo");
            GameObject halo = haloExisting != null ? haloExisting.gameObject : new GameObject("SeatHalo", typeof(RectTransform), typeof(Image));
            if (haloExisting == null)
                halo.transform.SetParent(innerSeat.transform, false);
            halo.transform.SetSiblingIndex(0);
            var haloRect = halo.GetComponent<RectTransform>();
            haloRect.anchorMin = new Vector2(-0.08f, -0.08f);
            haloRect.anchorMax = new Vector2(1.08f, 1.08f);
            haloRect.offsetMin = Vector2.zero;
            haloRect.offsetMax = Vector2.zero;
            var haloImage = halo.GetComponent<Image>();
            haloImage.sprite = GetGeneratedGlowSprite();
            haloImage.type = Image.Type.Simple;
            haloImage.color = haloColor;
            haloImage.raycastTarget = false;

            Transform plateExisting = innerSeat.transform.Find("SeatPlate");
            GameObject plate = plateExisting != null ? plateExisting.gameObject : new GameObject("SeatPlate", typeof(RectTransform), typeof(Image));
            if (plateExisting == null)
                plate.transform.SetParent(innerSeat.transform, false);
            plate.transform.SetSiblingIndex(1);
            var plateRect = plate.GetComponent<RectTransform>();
            plateRect.anchorMin = new Vector2(0.02f, 0.02f);
            plateRect.anchorMax = new Vector2(0.98f, 0.98f);
            plateRect.offsetMin = Vector2.zero;
            plateRect.offsetMax = Vector2.zero;
            var plateImage = plate.GetComponent<Image>();
            plateImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            plateImage.type = plateImage.sprite != null && plateImage.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            plateImage.color = socketColor;
            plateImage.raycastTarget = false;

            Transform frameExisting = innerSeat.transform.Find("SeatFrame");
            GameObject frame = frameExisting != null ? frameExisting.gameObject : new GameObject("SeatFrame", typeof(RectTransform), typeof(Image));
            if (frameExisting == null)
                frame.transform.SetParent(innerSeat.transform, false);
            frame.transform.SetSiblingIndex(2);
            var frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.04f, 0.04f);
            frameRect.anchorMax = new Vector2(0.96f, 0.96f);
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = Vector2.zero;
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            frameImage.type = frameImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            frameImage.color = style == BoardSocketStyle.Daemon
                ? new Color(frameColor.r, frameColor.g, frameColor.b, frameColor.a * 0.42f)
                : frameColor;
            frameImage.raycastTarget = false;

            AddSocketCornerBrackets(frameRect, style, alpha);

            return contentRect;
        }

        private void AddSocketCornerBrackets(RectTransform parent, BoardSocketStyle style, float alpha)
        {
            if (parent == null)
                return;

            bool daemon = style == BoardSocketStyle.Daemon;
            Color color = daemon
                ? new Color(1f, 0.82f, 0.44f, Mathf.Lerp(0.34f, 0.78f, Mathf.Clamp01(alpha)))
                : new Color(0.62f, 0.88f, 1f, Mathf.Lerp(0.28f, 0.62f, Mathf.Clamp01(alpha)));
            float longEdge = daemon ? 0.24f : 0.20f;
            float shortEdge = daemon ? 0.050f : 0.045f;
            float inset = 0.030f;

            AddSocketLine(parent, "CornerTL_H", new Vector2(inset, 1f - inset - shortEdge), new Vector2(inset + longEdge, 1f - inset), color);
            AddSocketLine(parent, "CornerTL_V", new Vector2(inset, 1f - inset - longEdge), new Vector2(inset + shortEdge, 1f - inset), color);
            AddSocketLine(parent, "CornerTR_H", new Vector2(1f - inset - longEdge, 1f - inset - shortEdge), new Vector2(1f - inset, 1f - inset), color);
            AddSocketLine(parent, "CornerTR_V", new Vector2(1f - inset - shortEdge, 1f - inset - longEdge), new Vector2(1f - inset, 1f - inset), color);
            AddSocketLine(parent, "CornerBL_H", new Vector2(inset, inset), new Vector2(inset + longEdge, inset + shortEdge), color);
            AddSocketLine(parent, "CornerBL_V", new Vector2(inset, inset), new Vector2(inset + shortEdge, inset + longEdge), color);
            AddSocketLine(parent, "CornerBR_H", new Vector2(1f - inset - longEdge, inset), new Vector2(1f - inset, inset + shortEdge), color);
            AddSocketLine(parent, "CornerBR_V", new Vector2(1f - inset - shortEdge, inset), new Vector2(1f - inset, inset + longEdge), color);
        }

        private void AddSocketLine(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject line = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            if (existing == null)
                line.transform.SetParent(parent, false);

            var rt = line.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = line.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private Color GetSocketColor(BoardSocketStyle style, float alpha)
        {
            Color baseColor = style switch
            {
                BoardSocketStyle.Daemon => new Color(0.08f, 0.055f, 0.040f, 1f),
                BoardSocketStyle.Pillar => new Color(0.06f, 0.055f, 0.070f, 1f),
                _ => new Color(0.10f, 0.10f, 0.12f, 1f),
            };

            baseColor.a = style == BoardSocketStyle.Daemon
                ? Mathf.Lerp(0.035f, 0.12f, Mathf.Clamp01(alpha))
                : Mathf.Lerp(0.16f, 0.30f, Mathf.Clamp01(alpha));
            return baseColor;
        }

        private void SetSocketCaption(RectTransform slot, string title, string subtitle, Color titleColor)
        {
            if (slot == null)
                return;

            EnsureSocketLabel(slot, "Title", title, new Vector2(8f, -8f), new Vector2(-8f, -24f), 11, FontStyles.Bold, titleColor, TextAlignmentOptions.TopLeft);
            EnsureSocketLabel(slot, "Subtitle", subtitle, new Vector2(8f, 26f), new Vector2(-8f, 8f), 9, FontStyles.Normal, new Color(0.64f, 0.68f, 0.76f, 0.9f), TextAlignmentOptions.BottomLeft);
        }

        private void EnsureSocketLabel(RectTransform parent, string name, string text, Vector2 offsetMin, Vector2 offsetMax,
            float size, FontStyles style, Color color, TextAlignmentOptions alignment)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
            if (existing == null)
                go.transform.SetParent(parent, false);

            bool shouldShow = !string.IsNullOrWhiteSpace(text);
            if (go.activeSelf != shouldShow)
                go.SetActive(shouldShow);
            if (!shouldShow)
                return;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = Mathf.Max(8f, size - 2f);
            tmp.fontSizeMax = size;
            tmp.raycastTarget = false;
        }

        private void ApplySocketCardPresentation(GameObject cardGO, CardZoneStyle zoneStyle, bool hidden)
        {
            if (cardGO == null)
                return;

            var rt = cardGO.GetComponent<RectTransform>();
            if (rt == null)
                return;
            float layoutT = GetBattlefieldLayoutT();

            var layout = cardGO.GetComponent<LayoutElement>();
            if (layout != null)
                layout.ignoreLayout = true;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 2f);
            Vector2 visibleHandSize = GetHandCardSize(false, layoutT);

            switch (zoneStyle)
            {
                case CardZoneStyle.Field:
                    rt.sizeDelta = new Vector2(BoardCardInnerW, BoardCardInnerH);
                    rt.localScale = Vector3.one;
                    break;
                case CardZoneStyle.Pillar:
                    rt.sizeDelta = new Vector2(PillarPileInnerW, PillarPileInnerH);
                    rt.localScale = Vector3.one * 0.78f;
                    break;
            }

            rt.localRotation = Quaternion.identity;
            var outline = cardGO.GetComponent<Outline>() ?? cardGO.AddComponent<Outline>();
            outline.effectColor = hidden
                ? new Color(0.96f, 0.80f, 0.42f, 0.46f)
                : new Color(0.82f, 0.94f, 1f, 0.42f);
            outline.effectDistance = new Vector2(2f, -2f);
            CardShadowUtility.EnsureShadow(cardGO);
        }

        private RectTransform EnsureInvokerBadgeImage(RectTransform parent, string name, string spritePath,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta,
            Color color, bool sliced = true)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            if (existing == null)
                go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;

            var image = go.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>(spritePath);
            image.type = image.sprite != null
                ? (sliced ? Image.Type.Sliced : Image.Type.Simple)
                : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return rt;
        }

        private RectTransform EnsureInvokerBadgeSprite(RectTransform parent, string name, Sprite sprite,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta,
            Color color, bool preserveAspect = true)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            if (existing == null)
                go.transform.SetParent(parent, false);

            bool shouldShow = sprite != null && color.a > 0.001f;
            if (go.activeSelf != shouldShow)
                go.SetActive(shouldShow);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;

            if (!shouldShow)
                return rt;

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.color = color;
            image.preserveAspect = preserveAspect;
            image.raycastTarget = false;
            return rt;
        }

        private void EnsureInvokerBadgeText(RectTransform parent, string name, string text,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta,
            float fontSize, Color color, FontStyles style = FontStyles.Bold, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
            if (existing == null)
                go.transform.SetParent(parent, false);

            bool shouldShow = !string.IsNullOrWhiteSpace(text);
            if (go.activeSelf != shouldShow)
                go.SetActive(shouldShow);
            if (!shouldShow)
                return;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;

            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = Mathf.Max(6f, fontSize - 2f);
            tmp.fontSizeMax = fontSize;
            tmp.raycastTarget = false;
        }

        private void RefreshDeckPile(RectTransform pile, string title, int count)
        {
            if (pile == null)
                return;

            RectTransform content = PreparePileContent(pile);
            ClearChildren(content);
            ConfigurePileLabels(pile, title, count > 0 ? count.ToString() : string.Empty);

            if (count <= 0)
            {
                CreatePilePlaceholder(content, string.Empty);
                return;
            }

            int layers = Mathf.Clamp(Mathf.Min(count, 5), 3, 5);
            for (int i = 0; i < layers; i++)
            {
                float progress = layers <= 1 ? 0.5f : i / (layers - 1f);
                CreateDeckPileCard(content,
                    $"CardBack_{i}",
                    new Vector2(i * 1.8f, -i * 2.2f),
                    Mathf.Lerp(-2f, 2f, progress),
                    Mathf.Lerp(0.38f, 0.94f, progress));
            }
        }

        private void RefreshVoidPile(RectTransform pile, string title, List<CardInstance> ashePile)
        {
            if (pile == null)
                return;

            // Ensure void pile has a defined visual background
            var pileBg = pile.GetComponent<Image>();
            if (pileBg == null) pileBg = pile.gameObject.AddComponent<Image>();
            pileBg.color = new Color(0.060f, 0.046f, 0.090f, 0.78f);
            pileBg.raycastTarget = false;
            var pileOutline = pile.GetComponent<Outline>();
            if (pileOutline == null) pileOutline = pile.gameObject.AddComponent<Outline>();
            pileOutline.effectColor = new Color(0.72f, 0.62f, 0.88f, 0.34f);
            pileOutline.effectDistance = new Vector2(1.5f, -1.5f);

            RectTransform content = PreparePileContent(pile);
            ClearChildren(content);
            int count = ashePile?.Count ?? 0;
            ConfigurePileLabels(pile, title, count > 0 ? count.ToString() : string.Empty);

            if (count > 0)
            {
                int layers = Mathf.Clamp(Mathf.Min(count, 3), 1, 3);
                for (int i = 0; i < layers - 1; i++)
                {
                    var shadowCard = Instantiate(cardPrefab, content);
                    shadowCard.name = $"VoidStack_{i}";
                    ApplyMiniPileCard(shadowCard, new Vector2(-i * 2.5f, i * 3f), false);
                    var shadowVisual = shadowCard.GetComponent<CardVisual>();
                    if (shadowVisual != null)
                        shadowVisual.SetCard(ashePile[Mathf.Max(0, count - 1 - i)].Card);

                    CanvasGroup shadowCanvas = shadowCard.GetComponent<CanvasGroup>();
                    if (shadowCanvas == null)
                    {
                        try
                        {
                            shadowCanvas = shadowCard.AddComponent<CanvasGroup>();
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[RefreshVoidPile] Failed to add CanvasGroup to {shadowCard.name}: {ex}");
                            continue;
                        }
                    }
                    if (shadowCanvas != null)
                        shadowCanvas.alpha = Mathf.Clamp01(0.46f - i * 0.08f);
                }

                var go = Instantiate(cardPrefab, content);
                go.name = "VoidTopCard";
                ApplyMiniPileCard(go, Vector2.zero, false);
                var visual = go.GetComponent<CardVisual>();
                if (visual != null)
                    visual.SetCard(ashePile[count - 1].Card);

                // Tap pile to browse all discarded cards
                var btn = pile.GetComponent<Button>();
                if (btn == null) btn = pile.gameObject.AddComponent<Button>();
                btn.onClick.RemoveAllListeners();
                var cards = new List<CardInstance>(ashePile); // snapshot
                btn.onClick.AddListener(() => ShowVoidPileViewer(title, cards));
            }
            else
            {
                CreatePilePlaceholder(content, string.Empty);
            }
        }

        private void ShowVoidPileViewer(string title, List<CardInstance> cards)
        {
            if (cards == null || cards.Count == 0) return;

            // Find canvas root
            var canvas = GetComponentInParent<Canvas>();
            Transform parent = canvas != null ? canvas.transform : transform;

            // Background dim
            var root = new GameObject("VoidViewer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(parent, false);
            var rootCanvas = root.GetComponent<Canvas>();
            rootCanvas.overrideSorting = true;
            rootCanvas.sortingOrder = 90;
            var rootRT = root.GetComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero; rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero; rootRT.offsetMax = Vector2.zero;

            // Dim
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(root.transform, false);
            var dimRT = dim.GetComponent<RectTransform>();
            dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
            dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);
            var dimBtn = dim.AddComponent<Button>();
            dimBtn.onClick.AddListener(() => Destroy(root));

            // Title
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(root.transform, false);
            var titleRT = titleGo.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.1f, 0.88f); titleRT.anchorMax = new Vector2(0.9f, 0.96f);
            titleRT.offsetMin = Vector2.zero; titleRT.offsetMax = Vector2.zero;
            var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
            titleTmp.text = $"{title}  ({cards.Count} cards)";
            titleTmp.fontSize = 24;
            titleTmp.color = new Color(0.78f, 0.66f, 0.42f);
            titleTmp.alignment = TextAlignmentOptions.Center;

            // Scroll area
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(root.transform, false);
            var scrollRT = scrollGo.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.05f, 0.05f);
            scrollRT.anchorMax = new Vector2(0.95f, 0.86f);
            scrollRT.offsetMin = Vector2.zero; scrollRT.offsetMax = Vector2.zero;

            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRT = vpGo.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRT = contentGo.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1); contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.offsetMin = Vector2.zero; contentRT.offsetMax = Vector2.zero;
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(118, 166);
            grid.spacing = new Vector2(8, 8);
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            grid.childAlignment = TextAnchor.UpperCenter;
            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = vpRT;
            scroll.content = contentRT;
            scroll.horizontal = false;

            // Populate cards (newest first)
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                var go = Instantiate(cardPrefab, contentGo.transform);
                var visual = go.GetComponent<CardVisual>();
                if (visual != null) visual.SetCard(cards[i].Card);
                var rt = go.GetComponent<RectTransform>();
                if (rt != null) rt.localScale = Vector3.one * 0.9f;
            }
        }

        private void RefreshSealPool(RectTransform pile, string title, int sealCount)
        {
            if (pile == null)
                return;

            var bg = pile.GetComponent<Image>();
            if (bg == null) bg = pile.gameObject.AddComponent<Image>();
            bg.color = new Color(0.064f, 0.050f, 0.095f, 0.82f);
            bg.raycastTarget = false;

            var outline = pile.GetComponent<Outline>();
            if (outline == null) outline = pile.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.74f, 0.64f, 0.94f, 0.32f);
            outline.effectDistance = new Vector2(2f, -2f);

            RectTransform content = PreparePileContent(pile);
            ClearChildren(content);
            ConfigureSealPileLabels(pile, title, $"{sealCount}/{GameConstants.MaxSeals}");
            EnsureSocketLabel(pile, "SealStatus",
                sealCount > 0 ? "TRAPS ARMED" : "NO SEALS SET",
                new Vector2(4f, 14f), new Vector2(-4f, 30f), 8.5f, FontStyles.Bold,
                new Color(0.92f, 0.80f, 1f, 0.90f), TextAlignmentOptions.Bottom);

            int layers = sealCount > 0
                ? Mathf.Clamp(sealCount + 1, 2, GameConstants.MaxSeals + 1)
                : 1;
            float width = content.rect.width > 1f ? content.rect.width : 92f;
            float height = content.rect.height > 1f ? content.rect.height : 112f;
            float lateralSpread = Mathf.Clamp(width * 0.11f, 4f, 10f);
            float verticalDrop = Mathf.Clamp(height * 0.14f, 10f, 18f);

            for (int i = 0; i < layers; i++)
            {
                float progress = layers <= 1 ? 0.5f : i / (layers - 1f);
                Vector2 offset = new Vector2(
                    Mathf.Lerp(-lateralSpread, lateralSpread, progress),
                    Mathf.Lerp(6f, -verticalDrop, progress));

                var card = CreateFaceDownPileCard(content,
                    sealCount > 0 ? $"SealBack_{i}" : "SealBack_Empty",
                    offset,
                    Mathf.Lerp(-10f, 10f, progress),
                    sealCount > 0
                    ? Mathf.Lerp(0.36f, 1f, progress)
                    : 0.28f);

                if (sealCount <= 0)
                {
                    var rect = card.GetComponent<RectTransform>();
                    if (rect != null)
                        rect.localScale = Vector3.one * 0.94f;
                    break;
                }
            }
        }

        private RectTransform PreparePileContent(RectTransform pile)
        {
            Transform existing = pile.Find("RuntimePileContent");
            GameObject content = existing != null ? existing.gameObject : new GameObject("RuntimePileContent", typeof(RectTransform));
            if (existing == null)
                content.transform.SetParent(pile, false);

            var rect = content.GetComponent<RectTransform>();
            bool isDeckPile = pile != null && (pile.name == "P1DeckPile" || pile.name == "P2DeckPile");
            if (isDeckPile)
            {
                rect.anchorMin = new Vector2(0.02f, 0.02f);
                rect.anchorMax = new Vector2(0.98f, 0.98f);
            }
            else
            {
                rect.anchorMin = new Vector2(0.10f, 0.18f);
                rect.anchorMax = new Vector2(0.90f, 0.78f);
            }
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void ConfigurePileLabels(RectTransform pile, string title, string subtitle)
        {
            EnsureSocketLabel(pile, "PileTitle", title, new Vector2(4f, -6f), new Vector2(-4f, -22f), 9.5f, FontStyles.Bold,
                new Color(0.96f, 0.90f, 0.74f, 0.84f), TextAlignmentOptions.Top);
            EnsureSocketLabel(pile, "PileSubtitle", subtitle, new Vector2(4f, 4f), new Vector2(-4f, 20f), 9.5f, FontStyles.Bold,
                new Color(0.88f, 0.94f, 1f, 0.84f), TextAlignmentOptions.Bottom);
        }

        private void ConfigureSealPileLabels(RectTransform pile, string title, string subtitle)
        {
            EnsureSocketLabel(pile, "PileTitle", title, new Vector2(8f, -6f), new Vector2(-8f, -24f), 8.3f, FontStyles.Bold,
                new Color(0.96f, 0.90f, 0.74f, 0.90f), TextAlignmentOptions.Top);
            EnsureSocketLabel(pile, "PileSubtitle", subtitle, new Vector2(8f, 6f), new Vector2(-8f, 22f), 8.5f, FontStyles.Bold,
                new Color(0.88f, 0.94f, 1f, 0.88f), TextAlignmentOptions.Bottom);
        }

        private void ApplyMiniPileCard(GameObject cardGO, Vector2 anchoredPosition, bool faceDown)
        {
            var rt = cardGO.GetComponent<RectTransform>();
            if (rt == null)
                return;

            var layout = cardGO.GetComponent<LayoutElement>();
            if (layout != null)
                layout.ignoreLayout = true;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = ResolveMiniPileCardSize(rt.parent as RectTransform) * (faceDown ? 0.72f : 1f);
            rt.localScale = Vector3.one * (faceDown ? 0.86f : 0.94f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(anchoredPosition.x * -0.22f, -10f, 10f));

            var visual = cardGO.GetComponent<CardVisual>();
            if (visual != null && faceDown)
                visual.SetFaceDown();

            CardShadowUtility.EnsureShadow(cardGO);
        }

        private Vector2 ResolveMiniPileCardSize(RectTransform parent)
        {
            const float cardAspect = 72f / 102f;
            float targetWidth = 72f;
            float targetHeight = 102f;

            if (parent != null && parent.rect.width > 1f && parent.rect.height > 1f)
            {
                float maxWidth = Mathf.Clamp(parent.rect.width * 0.58f, 44f, 62f);
                float maxHeight = Mathf.Clamp(parent.rect.height * 0.76f, 70f, 92f);
                targetWidth = Mathf.Min(maxWidth, maxHeight * cardAspect);
                targetHeight = targetWidth / cardAspect;
            }

            return new Vector2(targetWidth, targetHeight);
        }

        private static Vector2 ResolveDeckPileCardSize(RectTransform parent)
        {
            return new Vector2(DeckPileCardW, DeckPileCardH);
        }

        private GameObject CreateDeckPileCard(RectTransform parent, string name, Vector2 anchoredPosition, float rotationZ, float alpha)
        {
            var card = CreateFaceDownPileCard(parent, name, anchoredPosition, rotationZ, alpha);
            var rect = card.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = ResolveDeckPileCardSize(parent);
                rect.localScale = Vector3.one;
            }

            return card;
        }

        private GameObject CreateFaceDownPileCard(RectTransform parent, string name, Vector2 anchoredPosition, float rotationZ, float alpha)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = ResolveMiniPileCardSize(parent);
            rect.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
            rect.localScale = Vector3.one * 0.92f;

            var image = go.GetComponent<Image>();
            image.sprite = BattlePresentationRuntime.ResolveCardBack() ?? Resources.Load<Sprite>("UI/duelcraft-card-back") ?? Resources.Load<Sprite>("UI/panel-dark");
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = new Color(1f, 1f, 1f, alpha);
            image.raycastTarget = false;
            image.preserveAspect = true;

            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0.12f, 0.06f, 0.18f, alpha * 0.72f);
            outline.effectDistance = new Vector2(1f, -1f);

            var canvasGroup = go.GetComponent<CanvasGroup>();
            canvasGroup.alpha = alpha;
            canvasGroup.blocksRaycasts = false;

            return go;
        }

        private void CreatePilePlaceholder(RectTransform parent, string text)
        {
            var go = new GameObject("Placeholder", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 10f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(0.72f, 0.74f, 0.82f, 0.6f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        private bool IsAsheTargetSelectionActive(PlayerState player)
        {
            if (player == null || _battle?.State == null) return false;
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.Phase != GamePhase.Main) return false;
            if (_pendingAsheHandIndex < 0 || _pendingAsheHandIndex >= player.Hand.Count) return false;
            if (player.Field.Count <= 0) return false;
            var card = player.Hand[_pendingAsheHandIndex].Card;
            if (card is not AsheCardData ashe) return false;
            return player.Will >= card.GetWillCost() && player.Field.Any(d => ashe.Matches(d.Card));
        }

        private void ConfirmAshePlayTarget(int targetDaemonIndex)
        {
            if (_battle == null || _battle.State == null) return;
            var player = _battle.State.Players[LocalPlayer];
            if (_pendingAsheHandIndex < 0 || _pendingAsheHandIndex >= player.Hand.Count)
            {
                _pendingAsheHandIndex = -1;
                RefreshUI(_battle.State);
                return;
            }
            bool played = SubmitPlayerAction(new PlayAsheCardAction
            {
                HandIndex = _pendingAsheHandIndex,
                TargetDaemonFieldIndex = targetDaemonIndex,
            });
            _pendingAsheHandIndex = -1;
            if (played)
                OnStateChanged(_battle.State);
            else
                RefreshUI(_battle.State);
        }

        private void AddAsheTrayBadge(GameObject daemonGO, DaemonInstance daemon, List<AsheCardInstance> asheCards)
        {
            if (asheCards == null) return;
            var assigned = asheCards.Where(a => a.AssignedDaemonInstanceId == daemon.InstanceId).ToList();
            if (assigned.Count == 0) return;
            var tray = new GameObject("AsheTray", typeof(RectTransform), typeof(Image));
            tray.transform.SetParent(daemonGO.transform, false);
            var trayRT = tray.GetComponent<RectTransform>();
            trayRT.anchorMin = new Vector2(0.08f, -0.22f);
            trayRT.anchorMax = new Vector2(0.92f, 0.08f);
            trayRT.offsetMin = Vector2.zero;
            trayRT.offsetMax = Vector2.zero;

            var trayImage = tray.GetComponent<Image>();
            trayImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            trayImage.type = trayImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            trayImage.color = new Color(0.08f, 0.06f, 0.04f, 0.62f);
            trayImage.raycastTarget = false;

            int visibleCount = Mathf.Min(assigned.Count, 3);
            for (int i = 0; i < visibleCount; i++)
            {
                AsheCardInstance ashe = assigned[i];
                var cardGO = Instantiate(cardPrefab, tray.transform);
                cardGO.name = $"AsheAttachment_{i}";
                ApplyAsheAttachmentCard(cardGO,
                    visibleCount <= 1 ? 0f : Mathf.Lerp(-18f, 18f, i / (float)(visibleCount - 1)),
                    Mathf.Lerp(-8f, 8f, visibleCount <= 1 ? 0.5f : i / (float)(visibleCount - 1)));

                var visual = cardGO.GetComponent<CardVisual>();
                if (visual != null)
                {
                    visual.SetCard(ashe.Card);
                    visual.SetRuntimeMainStat(GetAsheRuntimeBadgeValue(ashe));
                }

                var button = cardGO.GetComponent<Button>() ?? cardGO.AddComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ShowCardInspectReadOnly(ashe.Card));
            }

            EnsureSocketLabel(trayRT, "AshePulse", BuildAsheTraySummary(assigned),
                new Vector2(6f, 4f), new Vector2(-6f, 18f), 8f, FontStyles.Bold,
                new Color(0.96f, 0.84f, 0.52f, 0.92f), TextAlignmentOptions.Bottom);

            if (assigned.Count > visibleCount)
            {
                EnsureSocketLabel(trayRT, "AsheOverflow", $"+{assigned.Count - visibleCount}",
                    new Vector2(6f, -6f), new Vector2(-6f, -22f), 8.5f, FontStyles.Bold,
                    new Color(0.92f, 0.92f, 1f, 0.92f), TextAlignmentOptions.TopRight);
            }
            else
            {
                Transform overflow = trayRT.Find("AsheOverflow");
                if (overflow != null)
                    overflow.gameObject.SetActive(false);
            }
        }

        private static int GetAsheRuntimeBadgeValue(AsheCardInstance ashe)
        {
            if (ashe?.Card == null)
                return 0;

            return ashe.Card.GetAffinityType() switch
            {
                CreatureType.Artificial => Mathf.Max(0, ashe.ShieldRemaining > 0 ? ashe.ShieldRemaining : ashe.Card.shieldAmount),
                CreatureType.Spirit => Mathf.Max(0, ashe.BuffTurnsRemaining > 0 ? ashe.BuffTurnsRemaining : ashe.Card.buffTurns),
                CreatureType.Undead => Mathf.Max(0, ashe.Card.resurrectHp),
                _ => Mathf.Max(0, ashe.Card.sePerTurn),
            };
        }

        private static string BuildAsheTraySummary(List<AsheCardInstance> assigned)
        {
            if (assigned == null || assigned.Count == 0)
                return string.Empty;

            return string.Join("   ", assigned.Take(2).Select(GetAsheTrayShortStatus));
        }

        private static string GetAsheTrayShortStatus(AsheCardInstance ashe)
        {
            if (ashe?.Card == null)
                return string.Empty;

            return ashe.Card.GetAffinityType() switch
            {
                CreatureType.Elemental => "0 SE",
                CreatureType.Machine => $"SIPHON {ashe.Card.sePerTurn}",
                CreatureType.Artificial => $"FIELD {Mathf.Max(0, ashe.ShieldRemaining > 0 ? ashe.ShieldRemaining : ashe.Card.shieldAmount)}",
                CreatureType.Spirit => $"BOND {Mathf.Max(0, ashe.BuffTurnsRemaining > 0 ? ashe.BuffTurnsRemaining : ashe.Card.buffTurns)}T",
                CreatureType.Undead => $"RITE {Mathf.Max(0, ashe.Card.resurrectHp)}",
                _ => ashe.Card.cardName.ToUpperInvariant(),
            };
        }

        private void ApplyAsheAttachmentCard(GameObject cardGO, float anchoredX, float rotationZ)
        {
            if (cardGO == null)
                return;

            var rt = cardGO.GetComponent<RectTransform>();
            if (rt == null)
                return;

            var layout = cardGO.GetComponent<LayoutElement>();
            if (layout != null)
                layout.ignoreLayout = true;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(anchoredX, -2f);
            rt.sizeDelta = new Vector2(42f, 60f);
            rt.localScale = Vector3.one * 0.68f;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotationZ);

            var canvasGroup = cardGO.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = cardGO.AddComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = true;
            canvasGroup.alpha = 1f;

            CardShadowUtility.EnsureShadow(cardGO);
        }

        private bool IsMaskTargetSelectionActive(PlayerState player)
        {
            if (player == null || _battle?.State == null)
                return false;
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.Phase != GamePhase.Main)
                return false;
            if (_pendingMaskHandIndex < 0 || _pendingMaskHandIndex >= player.Hand.Count)
                return false;
            if (player.Field.Count <= 0)
                return false;

            var card = player.Hand[_pendingMaskHandIndex].Card;
            if (card is not MaskCardData)
                return false;

            return player.Will >= card.GetWillCost() && CanPlayCard(player, card);
        }

        private void ConfirmMaskPlayTarget(int targetDaemonIndex)
        {
            if (_battle == null || _battle.State == null)
                return;

            var player = _battle.State.Players[LocalPlayer];
            if (_pendingMaskHandIndex < 0 || _pendingMaskHandIndex >= player.Hand.Count)
            {
                _pendingMaskHandIndex = -1;
                RefreshUI(_battle.State);
                return;
            }

            bool played = SubmitPlayerAction(new PlayMaskAction
            {
                HandIndex = _pendingMaskHandIndex,
                TargetDaemonIndex = targetDaemonIndex,
            });

            _pendingMaskHandIndex = -1;
            if (played)
                OnStateChanged(_battle.State);
            else
                RefreshUI(_battle.State);
        }

        // ─── Helpers ─────────────────────────────────────
        private bool TryGetSelectedSummonHandIndex(PlayerState player, out int handIndex)
        {
            handIndex = -1;
#pragma warning disable CS0162
            if (!UseSeatSelectionForDaemonPlay)
                return false;

            if (player == null || _selectedCardForPlay < 0 || _selectedCardForPlay >= player.Hand.Count)
                return false;

            var card = player.Hand[_selectedCardForPlay].Card;
            if (card is not DaemonCardData)
                return false;
            if (player.Will < card.GetWillCost() || !CanPlayCard(player, card))
                return false;

            handIndex = _selectedCardForPlay;
            return true;
#pragma warning restore CS0162
        }

        private bool CanPlayCard(PlayerState player, CardData card)
        {
            return card switch
            {
                DaemonCardData => player.Field.Count < GameConstants.MaxFieldDaemons,
                MaskCardData => player.Field.Count > 0,
                SealCardData => player.SealZone.Count < GameConstants.MaxSeals,
                DomainCardData => true,
                DispelCardData => true,
                AsheCardData ashe => player.Field.Any(d => ashe.Matches(d.Card)),
                _ => false,
            };
        }

        private int FindStrongestDaemon(PlayerState player)
        {
            int best = -1;
            int bestAtk = -1;
            for (int i = 0; i < player.Field.Count; i++)
            {
                if (player.Field[i].Attack > bestAtk)
                {
                    bestAtk = player.Field[i].Attack;
                    best = i;
                }
            }
            return best;
        }

        private void AddHighlightOverlay(GameObject cardGO, Color color)
        {
            var overlay = new GameObject("Highlight");
            overlay.transform.SetParent(cardGO.transform, false);
            var rt = overlay.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = overlay.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        private void AddPlayableCardHighlight(GameObject cardGO)
        {
            if (cardGO == null)
                return;

            if (cardGO.transform.Find("PlayableHighlight") != null)
                return;

            var overlay = new GameObject("PlayableHighlight", typeof(RectTransform), typeof(Image), typeof(Outline));
            overlay.transform.SetParent(cardGO.transform, false);
            overlay.transform.SetSiblingIndex(0);

            var rt = overlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-3f, -3f);
            rt.offsetMax = new Vector2(3f, 3f);

            var img = overlay.GetComponent<Image>();
            img.sprite = Resources.Load<Sprite>("UI/panel-dark");
            img.type = img.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.color = new Color(1f, 0.88f, 0.58f, 0.06f);
            img.raycastTarget = false;

            var outline = overlay.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 0.86f, 0.40f, 0.40f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        private static void RemoveNamedChild(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrEmpty(childName))
                return;

            Transform child = parent.Find(childName);
            if (child != null)
                Destroy(child.gameObject);
        }

        /// <summary>Overlay that pulses alpha between minA and maxA for visual emphasis.</summary>
        private void AddPulsingHighlightOverlay(GameObject cardGO, Color color, float minA = 0.12f, float maxA = 0.55f, float speed = 3.5f)
        {
            var overlay = new GameObject("PulseHighlight");
            overlay.transform.SetParent(cardGO.transform, false);
            var rt = overlay.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-3, -3);
            rt.offsetMax = new Vector2(3, 3);
            var img = overlay.AddComponent<Image>();
            img.color = new Color(color.r, color.g, color.b, maxA);
            img.raycastTarget = false;
            var pulser = overlay.AddComponent<HighlightPulse>();
            pulser.Init(img, color, minA, maxA, speed);
        }

        /// <summary>
        /// Adds a coloured type-effectiveness badge at the top of a target daemon card
        /// during attack targeting. Green/orange = super effective, gray/blue = not effective,
        /// no badge for neutral.
        /// </summary>
        private void AddEffectivenessLabel(RectTransform parentRT, DaemonInstance targetDaemon)
        {
            if (parentRT == null || targetDaemon?.Card == null) return;
            if (_selectedAttackerIndex < 0) return;

            var localField = _battle?.State?.Players[LocalPlayer]?.Field;
            if (localField == null || _selectedAttackerIndex >= localField.Count) return;

            var attackerDaemon = localField[_selectedAttackerIndex];
            if (attackerDaemon?.Card is not DaemonCardData attackerCard) return;
            if (targetDaemon.Card is not DaemonCardData defenderCard) return;

            float mult = Core.ElementSystem.GetElementMatchup(attackerCard.element, defenderCard.element);

            // Only show badge when it is NOT neutral
            if (Mathf.Approximately(mult, Core.GameConstants.NeutralMult)) return;

            bool superEffective = mult >= Core.GameConstants.SuperEffectiveMult;

            var badgeGO  = new GameObject("EffectBadge");
            badgeGO.transform.SetParent(parentRT, false);
            var badgeRT  = badgeGO.AddComponent<RectTransform>();
            badgeRT.anchorMin = new Vector2(0.04f, 0.78f);
            badgeRT.anchorMax = new Vector2(0.96f, 0.96f);
            badgeRT.offsetMin = Vector2.zero;
            badgeRT.offsetMax = Vector2.zero;

            var badgeBG  = badgeGO.AddComponent<Image>();
            badgeBG.color = superEffective
                ? new Color(0.90f, 0.52f, 0.08f, 0.88f)   // orange — super effective
                : new Color(0.22f, 0.28f, 0.40f, 0.82f);  // steel blue — resisted
            badgeBG.raycastTarget = false;

            var lblGO = new GameObject("EffectLbl");
            lblGO.transform.SetParent(badgeGO.transform, false);
            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;
            var lbl = lblGO.AddComponent<TextMeshProUGUI>();
            lbl.text = superEffective ? "SUPER EFFECTIVE" : "NOT EFFECTIVE";
            lbl.fontSize = 8;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color = Color.white;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
        }


        private void AddSelectTargetLabel(GameObject cardGO)
        {
            var label = new GameObject("SelectTargetLabel");
            label.transform.SetParent(cardGO.transform, false);
            var rt = label.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 0.40f);
            rt.anchorMax = new Vector2(0.95f, 0.60f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = label.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.65f);
            bg.raycastTarget = false;
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(label.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "SELECT TARGET";
            tmp.fontSize = 9;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(1f, 0.85f, 0.4f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        private void AddSummoningSicknessOverlay(GameObject cardGO)
        {
            // Semi-transparent blue tint to indicate summoning sickness
            AddHighlightOverlay(cardGO, new Color(0.2f, 0.3f, 0.6f, 0.3f));

            // "ZZZ" text label
            var label = new GameObject("SickLabel");
            label.transform.SetParent(cardGO.transform, false);
            var lRT = label.AddComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0.1f, 0.7f);
            lRT.anchorMax = new Vector2(0.9f, 0.95f);
            lRT.offsetMin = Vector2.zero;
            lRT.offsetMax = Vector2.zero;
            var tmp = label.AddComponent<TextMeshProUGUI>();
            tmp.text = "SUMMONED";
            tmp.fontSize = 10;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.5f, 0.7f, 1f, 0.9f);
            tmp.raycastTarget = false;
        }

        private void UpdateActionButtons(GameState state, bool isPlayerTurn)
        {
            // Hide NEXT PHASE button — we repurpose endTurnButton for phase transitions
            if (nextPhaseButton) nextPhaseButton.gameObject.SetActive(false);

            bool canAct = isPlayerTurn && (state.Phase == GamePhase.Main || state.Phase == GamePhase.Combat);
            string label;
            if (state.Phase == GamePhase.Main)
                label = isPlayerTurn
                    ? (HasCommittedMainActionThisTurn(state, LocalPlayer) ? "BATTLE" : "PREPARE")
                    : "WAIT";
            else if (state.Phase == GamePhase.Combat && isPlayerTurn)
                label = "END TURN";
            else if (state.Phase == GamePhase.Draw && isPlayerTurn)
                label = "DRAWING...";
            else if (!isPlayerTurn)
                label = "OPPONENT";
            else
                label = "WAIT";
            SetActionButtonState(endTurnButton, canAct, label);
        }

        private void EnsureStoryBattleIntroBanner(RectTransform canvasRoot)
        {
            if (_storyBattleBannerPanel != null || canvasRoot == null)
                return;

            _storyBattleBannerPanel = new GameObject("StoryBattleBanner", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _storyBattleBannerPanel.transform.SetParent(canvasRoot, false);
            var rect = _storyBattleBannerPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.72f);
            rect.anchorMax = new Vector2(0.5f, 0.72f);
            rect.sizeDelta = new Vector2(700f, 140f);
            rect.anchoredPosition = Vector2.zero;

            var bg = _storyBattleBannerPanel.GetComponent<Image>();
            bg.sprite = Resources.Load<Sprite>("UI/panel-dark");
            bg.type = bg.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            bg.color = new Color(0.05f, 0.06f, 0.10f, 0f);
            bg.raycastTarget = false;

            _storyBattleBannerPanel.GetComponent<CanvasGroup>().alpha = 0f;

            var accentGo = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accentGo.transform.SetParent(_storyBattleBannerPanel.transform, false);
            var accentRect = accentGo.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0.03f, 1f);
            accentRect.offsetMin = Vector2.zero;
            accentRect.offsetMax = Vector2.zero;
            _storyBattleBannerAccentImage = accentGo.GetComponent<Image>();
            _storyBattleBannerAccentImage.color = new Color(0.95f, 0.82f, 0.38f, 0f);
            _storyBattleBannerAccentImage.raycastTarget = false;

            _storyBattleBannerTitleText = CreateStoryTutorialLabel(
                "BannerTitle",
                _storyBattleBannerPanel.transform,
                new Vector2(0.08f, 0.48f),
                new Vector2(0.94f, 0.84f),
                30,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Color(0.98f, 0.95f, 0.88f));

            _storyBattleBannerSubtitleText = CreateStoryTutorialLabel(
                "BannerSubtitle",
                _storyBattleBannerPanel.transform,
                new Vector2(0.08f, 0.14f),
                new Vector2(0.94f, 0.40f),
                19,
                FontStyles.Normal,
                TextAlignmentOptions.Center,
                new Color(0.84f, 0.89f, 0.96f));
            _storyBattleBannerSubtitleText.textWrappingMode = TextWrappingModes.Normal;

            _storyBattleBannerPanel.SetActive(false);
        }

        private IEnumerator PlayStoryBattleIntroBanner()
        {
            if (_storyBattleConfig == null || _storyBattleBannerPanel == null)
            {
                yield return new WaitForSeconds(0.25f);
                yield break;
            }

            CanvasGroup group = _storyBattleBannerPanel.GetComponent<CanvasGroup>();
            Image bg = _storyBattleBannerPanel.GetComponent<Image>();
            if (group == null || bg == null)
            {
                yield return new WaitForSeconds(0.25f);
                yield break;
            }

            Color accent = CardVisual.GetElementColor(_storyBattleConfig.opponentElement);
            _storyBattleBannerTitleText.text = string.IsNullOrWhiteSpace(_storyBattleConfig.battleTitle)
                ? "SANCTIONED DUEL"
                : _storyBattleConfig.battleTitle.ToUpperInvariant();
            _storyBattleBannerSubtitleText.text = string.IsNullOrWhiteSpace(_storyBattleConfig.battleSubtitle)
                ? "The academy hall will guide the first trial."
                : _storyBattleConfig.battleSubtitle;

            _storyBattleBannerPanel.SetActive(true);
            group.alpha = 0f;
            bg.color = new Color(0.05f, 0.06f, 0.10f, 0f);
            _storyBattleBannerAccentImage.color = new Color(accent.r, accent.g, accent.b, 0f);

            const float fadeIn = 0.22f;
            const float hold = 1.10f;
            const float fadeOut = 0.24f;

            for (float t = 0f; t < fadeIn; t += Time.deltaTime)
            {
                float alpha = Mathf.Clamp01(t / fadeIn);
                group.alpha = alpha;
                bg.color = new Color(0.05f, 0.06f, 0.10f, alpha * 0.95f);
                _storyBattleBannerAccentImage.color = new Color(accent.r, accent.g, accent.b, alpha * 0.95f);
                yield return null;
            }

            group.alpha = 1f;
            bg.color = new Color(0.05f, 0.06f, 0.10f, 0.95f);
            _storyBattleBannerAccentImage.color = new Color(accent.r, accent.g, accent.b, 0.95f);
            yield return new WaitForSeconds(hold);

            for (float t = 0f; t < fadeOut; t += Time.deltaTime)
            {
                float alpha = 1f - Mathf.Clamp01(t / fadeOut);
                group.alpha = alpha;
                bg.color = new Color(0.05f, 0.06f, 0.10f, alpha * 0.95f);
                _storyBattleBannerAccentImage.color = new Color(accent.r, accent.g, accent.b, alpha * 0.95f);
                yield return null;
            }

            group.alpha = 0f;
            _storyBattleBannerPanel.SetActive(false);
        }

        private void EnsureStoryTutorialOverlay(RectTransform canvasRoot)
        {
            if (_storyTutorialPanel != null || canvasRoot == null)
                return;

            _storyTutorialPanel = new GameObject("StoryTutorialPanel", typeof(RectTransform), typeof(Image));
            _storyTutorialPanel.transform.SetParent(canvasRoot, false);
            var rect = _storyTutorialPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.020f, 0.70f);
            rect.anchorMax = new Vector2(0.285f, 0.95f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var bg = _storyTutorialPanel.GetComponent<Image>();
            bg.sprite = Resources.Load<Sprite>("UI/panel-dark");
            bg.type = bg.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            bg.color = new Color(0.05f, 0.07f, 0.11f, 0.92f);
            bg.raycastTarget = false;

            var accentGo = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accentGo.transform.SetParent(_storyTutorialPanel.transform, false);
            var accentRect = accentGo.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0.045f, 1f);
            accentRect.offsetMin = Vector2.zero;
            accentRect.offsetMax = Vector2.zero;
            _storyTutorialAccentImage = accentGo.GetComponent<Image>();
            _storyTutorialAccentImage.color = new Color(0.95f, 0.82f, 0.38f, 0.96f);
            _storyTutorialAccentImage.raycastTarget = false;

            _storyTutorialTitleText = CreateStoryTutorialLabel(
                "Title",
                _storyTutorialPanel.transform,
                new Vector2(0.09f, 0.72f),
                new Vector2(0.95f, 0.94f),
                22,
                FontStyles.Bold,
                TextAlignmentOptions.TopLeft,
                new Color(0.98f, 0.95f, 0.86f));

            _storyTutorialBodyText = CreateStoryTutorialLabel(
                "Body",
                _storyTutorialPanel.transform,
                new Vector2(0.09f, 0.22f),
                new Vector2(0.95f, 0.70f),
                18,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                new Color(0.90f, 0.93f, 0.98f));
            _storyTutorialBodyText.textWrappingMode = TextWrappingModes.Normal;

            _storyTutorialFooterText = CreateStoryTutorialLabel(
                "Footer",
                _storyTutorialPanel.transform,
                new Vector2(0.09f, 0.06f),
                new Vector2(0.95f, 0.18f),
                14,
                FontStyles.Bold,
                TextAlignmentOptions.BottomLeft,
                new Color(0.78f, 0.84f, 0.92f));

            _storyTutorialPanel.SetActive(false);
        }

        private static TextMeshProUGUI CreateStoryTutorialLabel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            int fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private void UpdateStoryTutorial(GameState state, bool isPlayerTurn)
        {
            bool tutorialActive = _storyBattleConfig != null && _storyBattleConfig.enableNoviceTutorial;
            if (!tutorialActive || _storyTutorialPanel == null)
            {
                if (_storyTutorialPanel != null && _storyTutorialPanel.activeSelf)
                    _storyTutorialPanel.SetActive(false);
                return;
            }

            PlayerState local = state.Players[LocalPlayer];
            bool hasFieldDaemon = local.Field.Count > 0;
            bool readyAttacker = CountReadyAttackers(local) > 0;
            if (!isPlayerTurn)
                _storyTutorialEnemyTurnSeen = true;

            string title;
            string body;
            string footer;
            Color accent;

            if (state.GameOver)
            {
                bool won = state.Winner == LocalPlayer;
                title = won ? "NOVICE TRIAL CLEARED" : "NOVICE TRIAL FAILED";
                body = won
                    ? "You completed the first sanctioned duel. The full loop is yours now: summon, prepare, battle, and protect your invoker."
                    : "The lesson still stands. Summon early, keep an eye on stored SE, and attack once your daemon is ready on the following turn.";
                footer = won
                    ? "Return to Story to continue the academy arc."
                    : "Use REMATCH to retry the tutorial duel.";
                accent = won ? new Color(0.42f, 0.94f, 0.58f, 1f) : new Color(1f, 0.54f, 0.42f, 1f);
            }
            else if (!hasFieldDaemon)
            {
                title = "SUMMON YOUR FIRST DAEMON";
                body = "Click a daemon in your hand, then play it onto the field. Summoning costs stored SE, so keep an eye on the resource total.";
                footer = $"Stored SE: {local.Will}  •  {_storyBattleConfig.battleTitle}";
                accent = new Color(0.95f, 0.82f, 0.38f, 1f);
            }
            else if (!isPlayerTurn)
            {
                title = "WATCH THE PROCTOR";
                body = "Now the academy proctor takes a turn. Watch how they draw, spend SE, and build pressure before control comes back to you.";
                footer = _storyBattleConfig.battleSubtitle;
                accent = new Color(0.54f, 0.73f, 0.98f, 1f);
            }
            else if (state.Phase == GamePhase.Draw)
            {
                title = "DRAW PHASE";
                body = "Each turn starts with a draw. Your new card is added automatically here, then you return to the main phase to spend SE and set up attacks.";
                footer = $"Hand: {local.Hand.Count} cards  •  Stored SE: {local.Will}";
                accent = new Color(0.54f, 0.73f, 0.98f, 1f);
            }
            else if (state.Phase == GamePhase.Main)
            {
                if (readyAttacker)
                {
                    title = "GO TO COMBAT";
                    body = "Your daemon is ready this turn. Press the glowing BATTLE button to enter combat, then select a ready daemon to begin an attack.";
                    footer = "BATTLE takes you from the main phase into combat.";
                    accent = new Color(1f, 0.58f, 0.34f, 1f);
                }
                else
                {
                    title = "SET YOUR BOARD";
                    body = _storyTutorialEnemyTurnSeen
                        ? "Fresh summons usually need a turn before attacking. You can play more cards now, or move ahead to combat when you're ready."
                        : "Fresh summons usually cannot attack right away. Press BATTLE to move on, then END TURN so your daemon is ready on the next turn.";
                    footer = $"Field: {local.Field.Count} daemon{(local.Field.Count == 1 ? "" : "s")}  •  Stored SE: {local.Will}";
                    accent = new Color(0.95f, 0.82f, 0.38f, 1f);
                }
            }
            else if (state.Phase == GamePhase.Combat)
            {
                if (_waitingForTarget && _selectedAttackerIndex >= 0)
                {
                    title = "CHOOSE A TARGET";
                    body = "Pick an enemy daemon. The Invoker opens only after enemy daemons are gone; pillars intercept automatically.";
                    footer = "Highlighted enemy daemons are valid attack destinations.";
                    accent = new Color(1f, 0.42f, 0.30f, 1f);
                }
                else if (readyAttacker)
                {
                    title = "SELECT A READY DAEMON";
                    body = "You are in combat now. Click one of your daemons with an attack highlight to start the strike, then choose what it hits.";
                    footer = "Combat is where your fielded daemons apply pressure.";
                    accent = new Color(1f, 0.58f, 0.34f, 1f);
                }
                else if (_storyTutorialAttackSeen)
                {
                    title = "FIRST ATTACK LANDED";
                    body = "Nice. You've seen the core duel rhythm in action. Finish any remaining combat choices, then press END TURN to pass back to the enemy.";
                    footer = "From here on out, trust the board and manage your SE.";
                    accent = new Color(0.42f, 0.94f, 0.58f, 1f);
                }
                else
                {
                    title = "END THE TURN";
                    body = "No ready attackers remain in this combat step. Press END TURN to pass and let the turn cycle bring your daemon online next round.";
                    footer = "Fresh summons become threatening on the following turn.";
                    accent = new Color(0.55f, 0.90f, 0.62f, 1f);
                }
            }
            else
            {
                title = "NOVICE TRIAL";
                body = "Follow the glow, keep your field active, and protect your invoker while you learn the duel flow.";
                footer = _storyBattleConfig.battleSubtitle;
                accent = new Color(0.74f, 0.82f, 0.92f, 1f);
            }

            if (!_storyTutorialPanel.activeSelf)
                _storyTutorialPanel.SetActive(true);

            _storyTutorialAccentImage.color = accent;
            _storyTutorialTitleText.text = title;
            _storyTutorialBodyText.text = body;
            _storyTutorialFooterText.text = footer;
        }

        private void SetActionButtonState(Button button, bool interactable, string label)
        {
            if (button == null)
                return;

            button.interactable = interactable;

            Color faceColor = ResolveActionButtonFace(label, interactable);
            Color labelColor = ResolveActionButtonLabelColor(label, interactable);
            Color glowColor = ResolveActionButtonGlow(label, interactable);

            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = faceColor;

            var text = button.transform.Find("Label")?.GetComponent<TextMeshProUGUI>()
                ?? button.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = label;
                text.color = labelColor;
            }

            EnsureActionButtonGlow(button, glowColor, interactable);
            EnsureActionButtonTag(button, ResolveActionButtonTag(label), interactable);
        }

        private static Color ResolveActionButtonFace(string label, bool interactable)
        {
            if (!interactable)
                return new Color(0.42f, 0.40f, 0.44f, 1f);

            return label switch
            {
                "PREPARE" => new Color(1f, 0.97f, 0.92f, 1f),
                "BATTLE" => new Color(1f, 0.89f, 0.80f, 1f),
                "END TURN" => new Color(0.90f, 0.98f, 0.90f, 1f),
                _ => Color.white,
            };
        }

        private static Color ResolveActionButtonLabelColor(string label, bool interactable)
        {
            if (!interactable)
                return new Color(0.72f, 0.70f, 0.68f, 1f);

            return label switch
            {
                "BATTLE" => new Color(0.34f, 0.08f, 0.04f, 1f),
                "END TURN" => new Color(0.10f, 0.24f, 0.08f, 1f),
                _ => new Color(0.18f, 0.12f, 0.06f, 1f),
            };
        }

        private static Color ResolveActionButtonGlow(string label, bool interactable)
        {
            if (!interactable)
                return Color.clear;

            return label switch
            {
                "PREPARE" => new Color(1f, 0.86f, 0.34f, 1f),
                "BATTLE" => new Color(1f, 0.42f, 0.20f, 1f),
                "END TURN" => new Color(0.46f, 0.92f, 0.56f, 1f),
                _ => new Color(0.84f, 0.86f, 0.92f, 1f),
            };
        }

        private static string ResolveActionButtonTag(string label) => label switch
        {
            "PREPARE" => "MAIN PHASE",
            "BATTLE" => "GO TO COMBAT",
            "END TURN" => "FINISH TURN",
            "WAIT" => "LOCKED",
            "OPPONENT" => "ENEMY TURN",
            "DRAWING..." => "DRAW PHASE",
            _ => string.Empty,
        };

        private void EnsureActionButtonGlow(Button button, Color glowColor, bool active)
        {
            if (button == null)
                return;

            Transform existing = button.transform.Find("ActionGlow");
            GameObject glow = existing != null ? existing.gameObject : new GameObject("ActionGlow", typeof(RectTransform), typeof(Image));
            if (existing == null)
                glow.transform.SetParent(button.transform, false);
            glow.transform.SetSiblingIndex(0);

            var rt = glow.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(-0.08f, -0.24f);
            rt.anchorMax = new Vector2(1.08f, 1.24f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = glow.GetComponent<Image>();
            img.sprite = GetGeneratedGlowSprite();
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            img.color = active ? new Color(glowColor.r, glowColor.g, glowColor.b, 0.22f) : Color.clear;

            var pulse = glow.GetComponent<HighlightPulse>();
            if (active)
            {
                pulse ??= glow.AddComponent<HighlightPulse>();
                pulse.Init(img, glowColor, 0.10f, 0.28f, 1.9f);
            }
            else if (pulse != null)
            {
                Destroy(pulse);
            }
        }

        private void EnsureActionButtonTag(Button button, string tagText, bool active)
        {
            if (button == null)
                return;

            Transform existing = button.transform.Find("ActionTag");
            GameObject tag = existing != null ? existing.gameObject : new GameObject("ActionTag", typeof(RectTransform), typeof(Image));
            if (existing == null)
                tag.transform.SetParent(button.transform, false);

            bool visible = active && !string.IsNullOrWhiteSpace(tagText);
            if (tag.activeSelf != visible)
                tag.SetActive(visible);
            if (!visible)
                return;

            var rt = tag.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.16f, 0.92f);
            rt.anchorMax = new Vector2(0.84f, 1.18f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = tag.GetComponent<Image>();
            img.sprite = Resources.Load<Sprite>("UI/panel-dark");
            img.type = img.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.color = new Color(0.06f, 0.07f, 0.10f, 0.86f);
            img.raycastTarget = false;

            Transform labelExisting = tag.transform.Find("Label");
            GameObject labelGo = labelExisting != null ? labelExisting.gameObject : new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            if (labelExisting == null)
                labelGo.transform.SetParent(tag.transform, false);

            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = tagText;
            tmp.fontSize = 8f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.96f, 0.94f, 0.88f, 0.94f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 6f;
            tmp.fontSizeMax = 8f;
            tmp.raycastTarget = false;
        }

        private void UpdateBattlefieldGuidance(GameState state, bool isPlayerTurn, bool canPlayCards, bool canAttack, bool canFuse)
        {
            var opponent = state.Players[AIPlayerIndex];
            var localPlayer = state.Players[LocalPlayer];
            bool waitingForTarget = _waitingForTarget && isPlayerTurn && canAttack;
            bool waitingForSummonSeat = canPlayCards && TryGetSelectedSummonHandIndex(localPlayer, out _);
            int intactOpponentPillars = CountIntactPillars(opponent);

            string playerHandSubtitle = waitingForSummonSeat ? "Daemon selected" : "Play from hand";
            string playerFieldSubtitle = waitingForSummonSeat
                ? "Click a glowing seat"
                : waitingForTarget
                    ? "Choose a target"
                    : canFuse
                        ? (_selectedFusionPrimaryIndex >= 0 ? "Choose matching daemon" : "Select twins + a seal")
                        : "Attack and summon here";
            string playerPillarSubtitle = "Active relic row";
            string opponentFieldSubtitle = waitingForTarget
                ? "Select a foe"
                : "Enemy daemon row";
            string opponentPillarSubtitle = waitingForTarget
                ? "Relics are inert"
                : "Shielding relics";
            bool invokerUnleashReady = isPlayerTurn
                && state.Phase == GamePhase.Main
                && !localPlayer.InvokerAbilityUsedThisTurn;
            bool playerFusionResonance = localPlayer.Field
                .GroupBy(d => d.Card.element)
                .Any(g => g.Count() >= 2);

            string playerInvokerSubtitle = invokerUnleashReady
                ? playerFusionResonance
                    ? $"Unleash+Resonance ({GameConstants.InvokerUnleashCost} SE)"
                    : $"Unleash ready ({GameConstants.InvokerUnleashCost} SE)"
                : "Your invoker";
            string opponentInvokerSubtitle = waitingForTarget && !opponent.Field.Any(d => !d.Stealthed)
                ? "Invoker exposed"
                : "Enemy invoker";

            UpdateLanePlate(p1HandContainer, "Player Hand", HandTint, "HAND", playerHandSubtitle, canPlayCards);
            UpdateLanePlate(p1FieldContainer, "Player Field", FieldTint, "YOUR FIELD", playerFieldSubtitle, waitingForSummonSeat || canAttack || canFuse);
            UpdateLanePlate(p1PillarContainer, "Player Pillars", PillarTint, "YOUR PILLARS", playerPillarSubtitle, false);
            UpdateLanePlate(p2HandContainer, "Opponent Hand", HandTint, "ENEMY HAND", "Hidden cards", false);
            UpdateLanePlate(p2FieldContainer, "Opponent Field", FieldTint, "ENEMY FIELD", opponentFieldSubtitle, waitingForTarget);
            UpdateLanePlate(p2PillarContainer, "Opponent Pillars", PillarTint, "ENEMY PILLARS", opponentPillarSubtitle, waitingForTarget);
            UpdateLanePlate(FindRootRect("P1InvokerZone"), "P1InvokerPlate", InvokerTint, "YOUR INVOKER", playerInvokerSubtitle, false);
            UpdateLanePlate(FindRootRect("P2InvokerZone"), "P2InvokerPlate", InvokerTint, "ENEMY INVOKER", opponentInvokerSubtitle, waitingForTarget && intactOpponentPillars == 0);
        }

        private void UpdateLanePlate(Transform container, string plateName, Color baseTint, string title, string subtitle, bool emphasized)
        {
            if (container == null || container.parent == null)
                return;

            var plate = container.parent.Find(plateName) as RectTransform;
            if (plate == null)
                return;

            bool isHandPlate = plateName.Contains("Hand");
            bool isInvokerPlate = plateName.Contains("Invoker");
            bool isFieldPlate = plateName.Contains("Field");
            bool isPillarPlate = plateName.Contains("Pillar");
            var plateImage = plate.GetComponent<Image>();
            if (plateImage != null)
            {
                bool showText = !string.IsNullOrWhiteSpace(subtitle);
                float baseAlpha = isInvokerPlate ? 0.16f : isHandPlate ? 0.34f : isFieldPlate ? 0.22f : 0.14f;
                float emphasisAlpha = isInvokerPlate ? 0.32f : isHandPlate ? 0.48f : isFieldPlate ? 0.38f : 0.28f;

                float targetAlpha = emphasized || showText ? emphasisAlpha : baseAlpha;
                plateImage.color = LiftColor(baseTint, emphasized ? 0.14f : 0.08f, targetAlpha);
            }

            Color titleColor = emphasized
                ? new Color(1f, 0.86f, 0.46f, 0.84f)
                : new Color(0.92f, 0.86f, 0.72f, isFieldPlate || isPillarPlate ? 0.60f : 0.52f);
            Color subtitleColor = emphasized
                ? new Color(0.95f, 0.90f, 0.78f, 0.78f)
                : new Color(0.70f, 0.78f, 0.88f, 0.64f);

            EnsurePlateText(plate, "Title", title, new Vector2(12f, -10f), isFieldPlate || isPillarPlate ? 11f : 9.5f, FontStyles.Bold,
                titleColor);
            EnsurePlateText(plate, "Subtitle", subtitle, new Vector2(12f, -22f), 8.5f, FontStyles.Normal,
                subtitleColor);
        }

        private void EnsurePlateText(RectTransform plate, string childName, string content, Vector2 anchoredPosition, float fontSize, FontStyles style, Color color)
        {
            Transform existing = plate.Find(childName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(childName, typeof(RectTransform));
            if (existing == null)
                go.transform.SetParent(plate, false);

            bool shouldShow = !string.IsNullOrWhiteSpace(content);
            if (go.activeSelf != shouldShow)
                go.SetActive(shouldShow);
            if (!shouldShow)
                return;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(anchoredPosition.x, anchoredPosition.y - 18f);
            rt.offsetMax = new Vector2(-14f, anchoredPosition.y + 18f);

            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = Mathf.Max(8f, fontSize - 2f);
            tmp.fontSizeMax = fontSize;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color LiftColor(Color color, float lift, float alpha)
        {
            return new Color(
                Mathf.Lerp(color.r, 1f, lift),
                Mathf.Lerp(color.g, 1f, lift),
                Mathf.Lerp(color.b, 1f, lift),
                alpha);
        }

        private static bool HasCommittedMainActionThisTurn(GameState state, int playerIndex)
        {
            if (state == null || state.Log == null || state.Log.Count == 0)
                return false;

            for (int i = state.Log.Count - 1; i >= 0; i--)
            {
                LogEntry entry = state.Log[i];
                if (entry == null)
                    continue;

                if (entry.Turn != state.TurnNumber)
                    break;

                if (entry.Player != playerIndex || entry.Type != LogEntryType.Action)
                    continue;

                string msg = (entry.Message ?? string.Empty).ToLowerInvariant();
                if (msg.Contains("summoned") || msg.Contains("played") || msg.Contains("cast")
                    || msg.Contains("equipped") || msg.Contains("mask equipped")
                    || msg.Contains("set a seal") || msg.Contains("activated"))
                    return true;
            }

            return false;
        }

        private static string PhaseDisplayName(GamePhase phase) => phase switch
        {
            GamePhase.Setup => "SETUP",
            GamePhase.RollSE => "SE ROLL",
            GamePhase.Draw => "DRAW",
            GamePhase.Main => "MAIN PHASE",
            GamePhase.Combat => "COMBAT",
            GamePhase.End => "END PHASE",
            _ => phase.ToString().ToUpper(),
        };

        private static Color PhaseAccentColor(GamePhase phase, bool isPlayerTurn) => phase switch
        {
            GamePhase.Setup => new Color(0.52f, 0.78f, 0.98f),
            GamePhase.Draw => new Color(0.52f, 0.78f, 0.98f),
            GamePhase.Main => isPlayerTurn ? new Color(0.95f, 0.88f, 0.62f) : new Color(0.76f, 0.78f, 0.86f),
            GamePhase.Combat => isPlayerTurn ? new Color(0.95f, 0.55f, 0.45f) : new Color(0.76f, 0.78f, 0.86f),
            _ => isPlayerTurn ? new Color(0.95f, 0.88f, 0.62f) : new Color(0.76f, 0.78f, 0.86f),
        };

        private static GamePhase NextPhase(GamePhase phase) => phase switch
        {
            GamePhase.Setup => GamePhase.Draw,
            GamePhase.RollSE => GamePhase.Draw,
            GamePhase.Draw => GamePhase.Main,
            GamePhase.Main => GamePhase.Combat,
            GamePhase.Combat => GamePhase.End,
            _ => GamePhase.End,
        };

        private string BuildTurnStatus(GameState state, bool isPlayerTurn)
        {
            if (state.GameOver)
                return $"Turn {state.TurnNumber} \u2022 Duel Complete";
            if (state.Phase == GamePhase.Setup)
                return "Setting Up...";
            if (!isPlayerTurn)
                return $"Turn {state.TurnNumber} \u2022 Opponent Resolving";
            if (state.Phase == GamePhase.Draw)
                return $"Turn {state.TurnNumber} \u2022 Drawing...";
            if (_waitingForTarget)
                return $"Turn {state.TurnNumber} \u2022 Choose Attack Target";
            if (state.Phase == GamePhase.Main)
            {
                string stage = HasCommittedMainActionThisTurn(state, LocalPlayer) ? "Battle" : "Prepare";
                return $"Turn {state.TurnNumber} \u2022 {stage} \u2022 Stored SE:{state.Players[LocalPlayer].Will}";
            }
            return $"Turn {state.TurnNumber} \u2022 Stored SE:{state.Players[LocalPlayer].Will}";
        }

        private static int CountReadyAttackers(PlayerState player)
        {
            int ready = 0;
            for (int i = 0; i < player.Field.Count; i++)
            {
                var daemon = player.Field[i];
                if (daemon.CanAttack && !daemon.HasAttacked && !daemon.Frozen && !daemon.Entangled)
                    ready++;
            }

            return ready;
        }

        private static int CountIntactPillars(PlayerState player)
        {
            int intact = 0;
            for (int i = 0; i < player.Pillars.Count; i++)
            {
                if (!player.Pillars[i].Destroyed)
                    intact++;
            }

            return intact;
        }

        private void AttachCardHover(GameObject cardGO)
        {
            if (cardGO == null)
                return;

            var trigger = cardGO.GetComponent<EventTrigger>() ?? cardGO.AddComponent<EventTrigger>();
            trigger.triggers ??= new List<EventTrigger.Entry>();
            if (trigger.triggers.Count > 0)
                return;

            var visual = cardGO.GetComponent<CardVisual>();
            if (visual == null)
                return;

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => visual.OnPointerEnter());
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => visual.OnPointerExit());
            trigger.triggers.Add(exit);
        }

        private void ApplyCardPresentation(GameObject cardGO, CardZoneStyle zoneStyle, int index, int count, bool hidden)
        {
            if (cardGO == null)
                return;

            var layout = cardGO.GetComponent<LayoutElement>() ?? cardGO.AddComponent<LayoutElement>();
            var rt = cardGO.GetComponent<RectTransform>();
            if (rt == null)
                return;
            float layoutT = GetBattlefieldLayoutT();

            switch (zoneStyle)
            {
                case CardZoneStyle.Hand:
                    Vector2 handSize = GetHandCardSize(hidden, layoutT);
                    float handWidth = handSize.x;
                    float handHeight = handSize.y;
                    layout.ignoreLayout = true;
                    layout.preferredWidth = handWidth;
                    layout.preferredHeight = handHeight;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.sizeDelta = new Vector2(handWidth, handHeight);
                    rt.localScale = Vector3.one * GetHandCardScale() * (hidden ? 0.78f : 0.98f);
                    // Rotation and position handled by ArrangeHandCurve
                    break;

                case CardZoneStyle.Field:
                    layout.ignoreLayout = true;
                    layout.preferredWidth = Mathf.Lerp(128f, 142f, layoutT);
                    layout.preferredHeight = Mathf.Lerp(178f, 198f, layoutT);
                    rt.sizeDelta = new Vector2(layout.preferredWidth, layout.preferredHeight);
                    rt.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.02f, layoutT);
                    rt.localRotation = Quaternion.identity;
                    break;

                case CardZoneStyle.Pillar:
                    layout.ignoreLayout = true;
                    Vector2 pillarSize = GetHandCardSize(false, layoutT);
                    layout.preferredWidth = pillarSize.x;
                    layout.preferredHeight = pillarSize.y;
                    rt.sizeDelta = pillarSize;
                    rt.localScale = Vector3.one * GetHandCardScale() * 0.92f;
                    rt.localRotation = Quaternion.identity;
                    break;
            }

            CardShadowUtility.EnsureShadow(cardGO);
        }

        private static Vector2 GetHandCardSize(bool hidden, float layoutT)
        {
            float width = hidden
                ? Mathf.Lerp(96f, 108f, layoutT)
                : Mathf.Lerp(136f, 150f, layoutT);
            float height = hidden
                ? Mathf.Lerp(140f, 156f, layoutT)
                : Mathf.Lerp(196f, 214f, layoutT);
            return new Vector2(width, height);
        }

        private static float GetFanAngle(int index, int count, float maxAngle)
        {
            if (count <= 1)
                return 0f;

            float midpoint = (count - 1) * 0.5f;
            float normalized = (index - midpoint) / midpoint;
            return -normalized * maxAngle;
        }

        private static float GetFanLift(int index, int count, float maxLift)
        {
            if (count <= 1)
                return 0f;

            float midpoint = (count - 1) * 0.5f;
            float distance = Mathf.Abs(index - midpoint);
            float normalized = midpoint <= 0f ? 0f : distance / midpoint;
            return (1f - normalized) * maxLift;
        }

        // ─── Bezier curve card hand ─────────────────────────
        private static void DisableHLG(Transform container)
        {
            if (container == null) return;
            var hlg = container.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) hlg.enabled = false;
        }

        private RectTransform CreateFreeBoardSeat(Transform parent, string name, BoardSocketStyle style, int index, int total, bool occupied, out GameObject slot)
        {
            float layoutT = GetBattlefieldLayoutT();
            bool isDaemon = style == BoardSocketStyle.Daemon;
            Vector2 seatSize = isDaemon
                ? ResolveDaemonSlotSize(parent)
                : new Vector2(PillarPileSlotW, PillarPileSlotH);

            float socketAlpha = occupied
                ? (isDaemon ? 0.88f : 0.80f)
                : (isDaemon ? 0.34f : 0.28f);
            RectTransform contentRect = CreateBoardSocket(parent, name, style, string.Empty, string.Empty, socketAlpha, out slot);

            var rt = slot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = seatSize;
            rt.anchoredPosition = GetBoardSeatPosition(parent as RectTransform, style, index, total, seatSize);
            rt.localScale = Vector3.one;

            var image = slot.GetComponent<Image>();
            if (image != null)
            {
                image.color = Color.clear;
                image.raycastTarget = false;
            }

            var outline = slot.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = occupied
                    ? new Color(0f, 0f, 0f, 0.12f)
                    : isDaemon
                        ? new Color(0.98f, 0.82f, 0.48f, 0.18f)
                        : new Color(0.80f, 0.90f, 1f, 0.14f);
                outline.effectDistance = new Vector2(1f, -1f);
            }

            var innerSeat = slot.transform.Find("Seat/InnerSeat") as RectTransform;
            if (innerSeat == null)
                return contentRect;

            Transform ghostTransform = innerSeat.Find("GhostCard");
            GameObject ghostCard = ghostTransform != null ? ghostTransform.gameObject : new GameObject("GhostCard", typeof(RectTransform), typeof(Image));
            if (ghostTransform == null)
                ghostCard.transform.SetParent(innerSeat, false);
            ghostCard.transform.SetSiblingIndex(1);
            var ghostRect = ghostCard.GetComponent<RectTransform>();
            ghostRect.anchorMin = new Vector2(0.5f, 0.5f);
            ghostRect.anchorMax = new Vector2(0.5f, 0.5f);
            ghostRect.pivot = new Vector2(0.5f, 0.5f);
            ghostRect.sizeDelta = isDaemon
                ? new Vector2(BoardCardInnerW, BoardCardInnerH)
                : new Vector2(PillarPileInnerW, PillarPileInnerH);
            ghostRect.anchoredPosition = isDaemon ? new Vector2(0f, 1f) : new Vector2(0f, -1f);
            var ghostImage = ghostCard.GetComponent<Image>();
            ghostImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            ghostImage.type = ghostImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            ghostImage.preserveAspect = false;
            ghostImage.raycastTarget = false;
            bool showSeatGhost = !occupied && isDaemon;
            bool showGhostLabel = showSeatGhost && UseSeatSelectionForDaemonPlay;
            ghostImage.color = showSeatGhost
                ? new Color(1f, 0.95f, 0.84f, UseSeatSelectionForDaemonPlay ? 0.06f : 0.018f)
                : new Color(1f, 1f, 1f, 0f);

            EnsureSocketLabel(innerSeat, "GhostLabel",
                showGhostLabel ? "SUMMON" : string.Empty,
                new Vector2(8f, 12f), new Vector2(-8f, 20f), isDaemon ? 8f : 8f, FontStyles.Bold,
                showGhostLabel
                    ? new Color(0.99f, 0.92f, 0.78f, UseSeatSelectionForDaemonPlay ? 0.30f : 0.14f)
                    : Color.clear,
                TextAlignmentOptions.Bottom);

            return contentRect;
        }

        private Vector2 GetBoardSeatPosition(RectTransform container, BoardSocketStyle style, int index, int total, Vector2 seatSize)
        {
            float width = container != null && container.rect.width > 1f ? container.rect.width : 900f;
            float height = container != null && container.rect.height > 1f ? container.rect.height : 260f;
            int safeTotal = Mathf.Max(1, total);
            float availableGap = safeTotal > 1
                ? Mathf.Max(18f, (width - seatSize.x * safeTotal) / (safeTotal - 1f))
                : BoardCardSlotGap;
            float gap = Mathf.Min(BoardCardSlotGap, availableGap);
            float stride = seatSize.x + gap;
            float midpoint = (safeTotal - 1) * 0.5f;
            float x = (Mathf.Clamp(index, 0, safeTotal - 1) - midpoint) * stride;
            float y = style == BoardSocketStyle.Daemon ? 0f : -height * 0.02f;
            return new Vector2(x, y);
        }

        private Vector2 ResolveDaemonSlotSize(Transform parent)
        {
            bool enemyField = parent != null && p2FieldContainer != null && parent == p2FieldContainer;
            return new Vector2(BoardCardSlotW, enemyField ? EnemyBoardCardSlotH : BoardCardSlotH);
        }

        private void ArrangeHandCurve(Transform container, int cardCount, bool isOpponent)
        {
            if (container == null || cardCount == 0) return;
            var containerRT = container as RectTransform;
            if (containerRT == null) return;

            Canvas.ForceUpdateCanvases();
            float containerWidth = containerRT.rect.width;
            if (containerWidth <= 0f) containerWidth = 800f;

            float layoutT = GetBattlefieldLayoutT();
            float cardW = (isOpponent
                ? Mathf.Lerp(82f, 92f, layoutT) * GetHandCardScale() * 0.76f
                : Mathf.Lerp(136f, 148f, layoutT) * GetHandCardScale() * 0.98f);
            float minSpacing = cardW * (isOpponent ? 0.96f : 0.90f);
            float maxSpacing = cardW * (isOpponent ? 1.24f : 1.10f);
            float desiredSpacing = (containerWidth - (isOpponent ? 56f : 88f))
                / Mathf.Max(1f, cardCount - (isOpponent ? 0.10f : 0.85f));
            float spacing = Mathf.Clamp(desiredSpacing, minSpacing, maxSpacing);
            float totalSpan = spacing * Mathf.Max(0, cardCount - 1);

            float arcHeight = isOpponent
                ? Mathf.Clamp(cardCount * 0.22f, 0f, 1.6f)
                : Mathf.Clamp(cardCount * 0.72f, 1.5f, 6f);
            float maxAngle = isOpponent
                ? Mathf.Lerp(1.2f, 2.4f, Mathf.InverseLerp(2f, 8f, cardCount))
                : Mathf.Lerp(3.6f, 8.6f, Mathf.InverseLerp(2f, 8f, cardCount));
            float maxLift = isOpponent
                ? Mathf.Lerp(0f, 1.0f, Mathf.InverseLerp(2f, 8f, cardCount))
                : Mathf.Lerp(0.5f, 2.5f, Mathf.InverseLerp(2f, 8f, cardCount));

            float halfSpan = totalSpan * 0.5f;
            Vector2 p0 = new Vector2(-halfSpan, 0f);
            Vector2 p2 = new Vector2(halfSpan, 0f);
            Vector2 p1 = new Vector2(0f, arcHeight);

            for (int i = 0; i < container.childCount && i < cardCount; i++)
            {
                var child = container.GetChild(i);
                var rt = child.GetComponent<RectTransform>();
                if (rt == null) continue;

                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);

                float t = cardCount == 1 ? 0.5f : (float)i / (cardCount - 1);
                Vector2 position = QuadBezier(p0, p1, p2, t);
                position.y += GetFanLift(i, cardCount, maxLift);
                position.y += isOpponent ? 0f : 4f;
                rt.anchoredPosition = position;

                float angle = GetFanAngle(i, cardCount, maxAngle);
                rt.localRotation = Quaternion.Euler(0f, 0f, isOpponent ? -angle * 0.15f : angle * 0.42f);
            }
        }

        private static Vector2 QuadBezier(Vector2 a, Vector2 b, Vector2 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        private void AnimateStatReadout(int playerIndex, int hp, int will, TextMeshProUGUI hpText, TextMeshProUGUI willText)
        {
            int prevHp = playerIndex == LocalPlayer ? _prevP1Hp : _prevP2Hp;
            int prevWill = playerIndex == LocalPlayer ? _prevP1Will : _prevP2Will;

            if (prevHp >= 0 && hpText != null && hp != prevHp)
            {
                StartCoroutine(UIAnimUtils.PopScale(hpText.transform, 0.18f, hp < prevHp ? 1.18f : 1.1f));
                hpText.color = hp < prevHp ? new Color(1f, 0.55f, 0.55f) : new Color(0.75f, 1f, 0.8f);
            }

            if (prevWill >= 0 && willText != null && will != prevWill)
            {
                StartCoroutine(UIAnimUtils.PopScale(willText.transform, 0.16f, 1.1f));
                willText.color = will > prevWill ? new Color(0.68f, 0.86f, 1f) : new Color(0.98f, 0.95f, 0.90f);
            }

            if (playerIndex == LocalPlayer)
            {
                _prevP1Hp = hp;
                _prevP1Will = will;
            }
            else
            {
                _prevP2Hp = hp;
                _prevP2Will = will;
            }
        }

        private void AddStatOverlay(GameObject cardGO, DaemonInstance daemon, int ownerStoredSe, List<AsheCardInstance> ownerAsheCards)
        {
            var overlay = new GameObject("Stats");
            overlay.transform.SetParent(cardGO.transform, false);
            var rt = overlay.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0.01f);
            rt.anchorMax = new Vector2(0.96f, 0.22f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = overlay.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.58f);
            bg.raycastTarget = false;

            string statusIcons = "";
            if (daemon.Frozen) statusIcons += " FRZ";
            if (daemon.Entangled) statusIcons += " ENT";
            if (daemon.Stealthed) statusIcons += " STL";
            if (daemon.Poisoned) statusIcons += " PSN";
            if (daemon.Burning) statusIcons += " BRN";
            if (daemon.HasTaunt) statusIcons += " TNT";
            if (GetDisplayedKillCooldownTurns(daemon) > 0) statusIcons += $" CD{GetDisplayedKillCooldownTurns(daemon)}";
            if (daemon.ShieldAmount > 0) statusIcons += $" SH{daemon.ShieldAmount}";
            if (daemon.NextAttackDouble) statusIcons += " x2";
            if (daemon.Masks.Count > 0) statusIcons += $" MSK{daemon.Masks.Count}";
            int attackCost = ResolveDisplayedAttackCost(daemon, ownerAsheCards);

            var mainTextGo = new GameObject("MainStat");
            mainTextGo.transform.SetParent(overlay.transform, false);
            var mainRt = mainTextGo.AddComponent<RectTransform>();
            mainRt.anchorMin = new Vector2(0f, 0f);
            mainRt.anchorMax = new Vector2(0.54f, 1f);
            mainRt.offsetMin = new Vector2(6f, 0f);
            mainRt.offsetMax = new Vector2(0f, 0f);
            var mainTmp = mainTextGo.AddComponent<TextMeshProUGUI>();
            mainTmp.text = $"{daemon.CurrentAshe}/{daemon.MaxAshe} LIFE";
            mainTmp.fontSize = 10f;
            mainTmp.color = new Color(0.92f, 0.96f, 1f, 0.96f);
            mainTmp.alignment = TextAlignmentOptions.MidlineLeft;
            mainTmp.enableAutoSizing = true;
            mainTmp.fontSizeMin = 7f;
            mainTmp.fontSizeMax = 10f;
            mainTmp.raycastTarget = false;

            var costTextGo = new GameObject("CostStat");
            costTextGo.transform.SetParent(overlay.transform, false);
            var costRt = costTextGo.AddComponent<RectTransform>();
            costRt.anchorMin = new Vector2(0.32f, 0f);
            costRt.anchorMax = new Vector2(0.76f, 1f);
            costRt.offsetMin = Vector2.zero;
            costRt.offsetMax = Vector2.zero;
            var costTmp = costTextGo.AddComponent<TextMeshProUGUI>();
            costTmp.text = $"SE {attackCost}";
            costTmp.fontSize = 8f;
            costTmp.color = attackCost <= 0
                ? new Color(0.74f, 1f, 0.82f, 0.96f)
                : ownerStoredSe < attackCost
                    ? new Color(1f, 0.70f, 0.62f, 0.96f)
                    : new Color(0.80f, 0.88f, 1f, 0.96f);
            costTmp.alignment = TextAlignmentOptions.Midline;
            costTmp.enableAutoSizing = true;
            costTmp.fontSizeMin = 6f;
            costTmp.fontSizeMax = 8f;
            costTmp.raycastTarget = false;

            if (!string.IsNullOrWhiteSpace(statusIcons))
            {
                var statusGO = new GameObject("StatusText");
                statusGO.transform.SetParent(cardGO.transform, false);
                var statusRt = statusGO.AddComponent<RectTransform>();
                statusRt.anchorMin = new Vector2(0.08f, 0.18f);
                statusRt.anchorMax = new Vector2(0.92f, 0.30f);
                statusRt.offsetMin = Vector2.zero;
                statusRt.offsetMax = Vector2.zero;
                var statusTmp = statusGO.AddComponent<TextMeshProUGUI>();
                statusTmp.text = statusIcons.Trim();
                statusTmp.fontSize = 6f;
                statusTmp.color = new Color(1f, 0.96f, 0.90f, 0.92f);
                statusTmp.alignment = TextAlignmentOptions.Center;
                statusTmp.enableAutoSizing = true;
                statusTmp.fontSizeMin = 4f;
                statusTmp.fontSizeMax = 6f;
                statusTmp.raycastTarget = false;
            }

            var atkBadge = new GameObject("AttackBadge");
            atkBadge.transform.SetParent(cardGO.transform, false);
            var badgeRt = atkBadge.AddComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0.76f, 0.01f);
            badgeRt.anchorMax = new Vector2(0.99f, 0.18f);
            badgeRt.offsetMin = Vector2.zero;
            badgeRt.offsetMax = Vector2.zero;
            var badgeBg = atkBadge.AddComponent<Image>();
            badgeBg.color = new Color(0.58f, 0.12f, 0.10f, 0.92f);
            badgeBg.raycastTarget = false;

            var elemBadge = new GameObject("ElementBadge");
            elemBadge.transform.SetParent(atkBadge.transform, false);
            var elemRt = elemBadge.AddComponent<RectTransform>();
            elemRt.anchorMin = new Vector2(0.02f, 0.08f);
            elemRt.anchorMax = new Vector2(0.34f, 0.92f);
            elemRt.offsetMin = Vector2.zero;
            elemRt.offsetMax = Vector2.zero;
            var elemBg = elemBadge.AddComponent<Image>();
            Color elemColor = CardVisual.GetElementColor(daemon.Card.element);
            elemBg.color = new Color(elemColor.r, elemColor.g, elemColor.b, 0.95f);
            elemBg.raycastTarget = false;

            var elemTextGo = new GameObject("ElementText");
            elemTextGo.transform.SetParent(elemBadge.transform, false);
            var elemTextRt = elemTextGo.AddComponent<RectTransform>();
            elemTextRt.anchorMin = Vector2.zero;
            elemTextRt.anchorMax = Vector2.one;
            elemTextRt.offsetMin = Vector2.zero;
            elemTextRt.offsetMax = Vector2.zero;
            var elemTmp = elemTextGo.AddComponent<TextMeshProUGUI>();
            elemTmp.text = daemon.Card.element.ToString().Substring(0, 1).ToUpperInvariant();
            elemTmp.fontSize = 9f;
            elemTmp.fontStyle = FontStyles.Bold;
            elemTmp.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            elemTmp.alignment = TextAlignmentOptions.Center;
            elemTmp.enableAutoSizing = true;
            elemTmp.fontSizeMin = 6f;
            elemTmp.fontSizeMax = 9f;
            elemTmp.raycastTarget = false;

            var atkTextGo = new GameObject("AttackText");
            atkTextGo.transform.SetParent(atkBadge.transform, false);
            var atkRt = atkTextGo.AddComponent<RectTransform>();
            atkRt.anchorMin = new Vector2(0.30f, 0f);
            atkRt.anchorMax = Vector2.one;
            atkRt.offsetMin = Vector2.zero;
            atkRt.offsetMax = Vector2.zero;
            var atkTmp = atkTextGo.AddComponent<TextMeshProUGUI>();
            atkTmp.text = daemon.Attack.ToString();
            atkTmp.fontSize = 14f;
            atkTmp.fontStyle = FontStyles.Bold;
            atkTmp.color = Color.white;
            atkTmp.alignment = TextAlignmentOptions.Center;
            atkTmp.enableAutoSizing = true;
            atkTmp.fontSizeMin = 9f;
            atkTmp.fontSizeMax = 14f;
            atkTmp.raycastTarget = false;
        }

        private static int ResolveDisplayedAttackCost(DaemonInstance daemon, List<AsheCardInstance> ownerAsheCards)
        {
            if (daemon == null)
                return 0;

            bool hasElementalAshe = ownerAsheCards != null
                && ownerAsheCards.Any(ashe =>
                    ashe != null
                    && ashe.AssignedDaemonInstanceId == daemon.InstanceId
                    && ashe.Card != null
                    && ashe.Card.GetAffinityType() == CreatureType.Elemental);

            return hasElementalAshe ? 0 : Mathf.Max(0, daemon.AsheCost);
        }

        private static int GetDisplayedKillCooldownTurns(DaemonInstance daemon)
        {
            if (daemon == null)
                return 0;

            return daemon.HasKilledThisTurn
                ? Mathf.Max(1, daemon.KillCooldownTurnsRemaining)
                : Mathf.Max(0, daemon.KillCooldownTurnsRemaining);
        }

        private void AddMaskBadge(GameObject cardGO, DaemonInstance daemon)
        {
            var badge = new GameObject("MaskBadge");
            badge.transform.SetParent(cardGO.transform, false);
            var rt = badge.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.72f, 0.82f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = badge.AddComponent<Image>();
            bg.color = new Color(0.52f, 0.24f, 0.68f, 0.90f);
            bg.raycastTarget = false;

            var iconGo = new GameObject("MaskIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(badge.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.78f);
            iconRt.anchorMax = new Vector2(0.5f, 0.78f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(14f, 14f);
            iconRt.anchoredPosition = Vector2.zero;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = Resources.Load<Sprite>("UI/icon-skull") ?? Resources.Load<Sprite>("UI/icon-shield");
            iconImg.color = new Color(1f, 1f, 1f, 0.95f);
            iconImg.raycastTarget = false;

            var labelGO = new GameObject("MaskLabel");
            labelGO.transform.SetParent(badge.transform, false);
            var lrt = labelGO.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(0f, 2f);
            lrt.offsetMax = new Vector2(0f, -1f);
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "MASK\nEQUIPPED";
            tmp.fontSize = 6;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Bottom;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 4;
            tmp.fontSizeMax = 7;
            tmp.raycastTarget = false;
        }

        private static void AddKillCooldownBadge(GameObject cardGO, DaemonInstance daemon)
        {
            int turns = GetDisplayedKillCooldownTurns(daemon);
            if (turns <= 0)
                return;

            var badge = new GameObject("KillCooldownBadge");
            badge.transform.SetParent(cardGO.transform, false);
            var rt = badge.AddComponent<RectTransform>();
            bool offsetForFusion = daemon != null && daemon.IsFusionApex;
            rt.anchorMin = offsetForFusion ? new Vector2(0.04f, 0.66f) : new Vector2(0.04f, 0.82f);
            rt.anchorMax = offsetForFusion ? new Vector2(0.54f, 0.80f) : new Vector2(0.54f, 0.96f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var bg = badge.AddComponent<Image>();
            bg.color = new Color(0.76f, 0.54f, 0.14f, 0.92f);
            bg.raycastTarget = false;

            var labelGO = new GameObject("CooldownLabel");
            labelGO.transform.SetParent(badge.transform, false);
            var lrt = labelGO.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(2f, 1f);
            lrt.offsetMax = new Vector2(-2f, -1f);
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = $"RECOVER\n{turns} TURN";
            tmp.fontSize = 7f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(0.13f, 0.08f, 0.02f, 0.98f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 5f;
            tmp.fontSizeMax = 8f;
            tmp.raycastTarget = false;
        }

        private static void AddFusionApexBadge(GameObject cardGO, DaemonInstance daemon)
        {
            string label = daemon.FusionTurnsRemaining > 0
                ? $"FUSED\nx{daemon.FusionTurnsRemaining}"
                : "FUSED\nSEALED";

            var badge = new GameObject("FusionApexBadge");
            badge.transform.SetParent(cardGO.transform, false);
            var rt = badge.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.82f);
            rt.anchorMax = new Vector2(0.38f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = badge.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.68f, 0.84f, 0.90f);
            bg.raycastTarget = false;

            var labelGO = new GameObject("FusionLabel");
            labelGO.transform.SetParent(badge.transform, false);
            var lrt = labelGO.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(2f, 1f);
            lrt.offsetMax = new Vector2(-1f, -1f);
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 7;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 5;
            tmp.fontSizeMax = 8;
            tmp.raycastTarget = false;
        }

        private void AddInvokerTarget(Transform parent, PlayerState player)
        {
            var go = new GameObject("InvokerTarget");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(134, 64);
            var bg = go.AddComponent<Image>();
            bg.sprite = Resources.Load<Sprite>("UI/btn-red");
            bg.type = bg.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            bg.color = Color.white;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(OnOpponentInvokerClicked);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = $"STRIKE INVOKER\n{player.Invoker.Hp} HP";
            tmp.fontSize = 14;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(0.98f, 0.95f, 0.92f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        // ─── Log + Effect Announcements ───
        private enum EffectScope
        {
            Localized,
            BoardWide,
        }

        private void AddLogEntry(LogEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Message))
                return;

            if (ShowPersistentGameLog && logContainer != null)
                AppendLogVisual(entry);

            DispatchReactiveSfx(entry);
            DispatchReactiveVfx(entry);
            QueueBattleDialogue(entry);

            if (!ShouldAnnounceEffect(entry))
                return;

            _effectAnnouncementQueue.Enqueue(entry);
            if (_effectAnnouncementRoutine == null)
                _effectAnnouncementRoutine = StartCoroutine(RunEffectAnnouncementQueue());
        }

        private void DispatchReactiveVfx(LogEntry entry)
        {
            if (_combatFxLayer == null)
                return;

            string lower = (entry.Message ?? string.Empty).ToLowerInvariant();
            Vector3 center = ResolveReactiveVfxCenter();

            if (entry.Type == LogEntryType.Effect && lower.Contains("ward"))
            {
                SpawnKeywordSheetVfx(center, BattleEffectAssetKind.Radiant, "WARD", new Color(1f, 0.96f, 0.62f), 1.08f);
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.SealTrigger, 0.36f, 1.08f);
                return;
            }

            if (entry.Type == LogEntryType.Effect && lower.Contains("echo"))
            {
                SpawnKeywordSheetVfx(center, BattleEffectAssetKind.Storm, lower.Contains("resolves") ? "ECHO RESOLVES" : "ECHO", new Color(0.54f, 0.9f, 1f), 0.98f);
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.DomainPulse, 0.30f, 1.12f);
                return;
            }

            if (entry.Type == LogEntryType.Effect && lower.Contains("devours"))
            {
                SpawnKeywordSheetVfx(center, BattleEffectAssetKind.Shadow, "DEVOUR", new Color(0.84f, 0.46f, 1f), 1.12f);
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.Destroy, 0.34f, 0.92f);
                return;
            }

            if (entry.Type == LogEntryType.Effect && lower.Contains("resonates with"))
            {
                SpawnKeywordSheetVfx(center, BattleEffectAssetKind.Bind, "RESONANCE", new Color(0.66f, 0.96f, 1f), 1.02f);
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.Fusion, 0.34f, 1.04f);
                return;
            }

            if (entry.Type == LogEntryType.Effect && lower.Contains("overchannels"))
            {
                SpawnKeywordSheetVfx(center, BattleEffectAssetKind.Flame, "OVERCHANNEL", new Color(1f, 0.42f, 0.22f), 1.14f);
                if (_combatFlashOverlay != null)
                    StartCoroutine(UIAnimUtils.ScreenFlash(_combatFlashOverlay, new Color(1f, 0.22f, 0.08f, 0.34f), 0.14f));
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.SpellPlay, 0.40f, 0.88f);
                return;
            }

            if (entry.Type == LogEntryType.Effect && lower.Contains("attuned to"))
            {
                Element element = TryResolveElementFromMessage(lower, out var parsedElement) ? parsedElement : Element.Light;
                SpawnKeywordSheetVfx(center, ResolveAetherEffectKind(element), "ATTUNED", ResolveElementTint(element), 1.04f);
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.Ready, 0.34f, 1.08f);
                return;
            }

            if (entry.Type == LogEntryType.Action && lower.Contains("fuses with"))
            {
                // Determine which field this belongs to.
                int actingPlayer = _battle?.State?.CurrentPlayer ?? LocalPlayer;
                Transform fieldContainer = actingPlayer == LocalPlayer ? p1FieldContainer : p2FieldContainer;
                center = fieldContainer != null
                    ? GetRectWorldPoint(fieldContainer as RectTransform)
                    : GetRectWorldPoint(_combatFxLayer);

                // Large cyan burst.
                SpawnImpactBurst(center, new Color(0.18f, 0.82f, 1f, 1f), 2.2f);
                // Second softer ring for depth.
                StartCoroutine(DelayedAction(0.06f, () =>
                    SpawnImpactBurst(center, new Color(0.46f, 0.94f, 1f, 0.65f), 1.6f)));
                // Floating label.
                SpawnFloatingCombatText(center, "FUSION APEX", new Color(0.46f, 0.96f, 1f, 1f), 1.15f);
                return;
            }

            if (entry.Type == LogEntryType.Effect && lower.Contains("fusion apex fades"))
            {
                int actingPlayer = _battle?.State?.CurrentPlayer ?? LocalPlayer;
                Transform fieldContainer = actingPlayer == LocalPlayer ? p1FieldContainer : p2FieldContainer;
                center = fieldContainer != null
                    ? GetRectWorldPoint(fieldContainer as RectTransform)
                    : GetRectWorldPoint(_combatFxLayer);

                SpawnImpactBurst(center, new Color(0.70f, 0.80f, 0.85f, 0.80f), 1.3f);
                SpawnFloatingCombatText(center, "FUSED FADES", new Color(0.72f, 0.82f, 0.88f, 0.90f), 0.85f);
                return;
            }

            TryDispatchGeneralMoveVfx(entry, lower, center);
        }

        private Vector3 ResolveReactiveVfxCenter()
        {
            int actingPlayer = _battle?.State?.CurrentPlayer ?? LocalPlayer;
            Transform fieldContainer = actingPlayer == LocalPlayer ? p1FieldContainer : p2FieldContainer;
            if (fieldContainer is RectTransform fieldRect)
                return GetRectWorldPoint(fieldRect);

            return GetRectWorldPoint(_combatFxLayer);
        }

        private void SpawnKeywordSheetVfx(Vector3 worldPos, BattleEffectAssetKind kind, string label, Color tint, float scale)
        {
            SpawnImpactBurst(worldPos, tint, 1.5f * scale);
            SpawnAetherSheetImpactFX(worldPos, kind, tint, scale);
            SpawnFloatingCombatText(worldPos + Vector3.up * 34f, label, new Color(tint.r, tint.g, tint.b, 0.96f), 0.78f);
        }

        private bool TryDispatchGeneralMoveVfx(LogEntry entry, string lower, Vector3 center)
        {
            if (entry == null)
                return false;

            if (entry.Type == LogEntryType.Action)
            {
                if (lower.Contains("summoned"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Verdant, "SUMMON", new Color(0.58f, 1f, 0.52f), 0.9f, Audio.SfxCue.Summon);

                if (lower.Contains("set a seal"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Radiant, "SEAL", new Color(1f, 0.92f, 0.5f), 0.86f, Audio.SfxCue.SealTrigger);

                if (lower.Contains("equipped") || lower.Contains("mask"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Bind, "MASK", new Color(0.74f, 0.82f, 1f), 0.86f, Audio.SfxCue.SpellPlay);

                if (lower.Contains("dispel") || lower.Contains("cast "))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Gale, lower.Contains("dispel") ? "DISPEL" : "CAST", new Color(0.72f, 0.94f, 1f), 0.88f, Audio.SfxCue.SpellPlay);

                if (lower.Contains("domain"))
                {
                    BattleEffectAssetKind kind = TryResolveElementFromMessage(lower, out var element)
                        ? ResolveAetherEffectKind(element)
                        : BattleEffectAssetKind.Storm;
                    Color tint = TryResolveElementFromMessage(lower, out element)
                        ? ResolveElementTint(element)
                        : new Color(0.52f, 0.86f, 1f);
                    return SpawnMoveVfx(center, kind, "DOMAIN", tint, 0.95f, Audio.SfxCue.DomainPulse);
                }

                if (lower.Contains("activated"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Stone, "ACTIVATE", new Color(0.92f, 0.76f, 0.44f), 0.88f, Audio.SfxCue.Ready);

                if (lower.Contains("unleashes invoker power"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Radiant, "INVOKER", new Color(0.96f, 0.98f, 1f), 1.18f, Audio.SfxCue.Fusion);
            }

            if (entry.Type == LogEntryType.Effect)
            {
                if (lower.Contains("negated") || lower.Contains("counter-seal"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Bind, "COUNTER", new Color(0.86f, 0.9f, 1f), 0.96f, Audio.SfxCue.Counter);

                if (lower.Contains("dispelled"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Gale, "DISPEL", new Color(0.72f, 0.94f, 1f), 0.9f, Audio.SfxCue.Discard);

                if (lower.Contains("pillar") || lower.Contains("anchor") || lower.Contains("shatter"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Stone, "PILLAR", new Color(0.96f, 0.74f, 0.36f), 0.9f, Audio.SfxCue.DomainPulse);

                if (lower.Contains("draw") || lower.Contains("archive"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Gale, "DRAW", new Color(0.74f, 0.96f, 1f), 0.82f, Audio.SfxCue.Draw);

                if (lower.Contains("heal") || lower.Contains("protect") || lower.Contains("shield") || lower.Contains("sanctuary"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Radiant, "PROTECT", new Color(1f, 0.94f, 0.62f), 0.86f, Audio.SfxCue.SealTrigger);

                if (lower.Contains("burn") || lower.Contains("surge deals"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Flame, "DAMAGE", new Color(1f, 0.42f, 0.2f), 0.9f, Audio.SfxCue.Hit);

                if (lower.Contains("freeze") || lower.Contains("frozen"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Frost, "FREEZE", new Color(0.62f, 0.9f, 1f), 0.86f, Audio.SfxCue.SpellPlay);

                if (lower.Contains("poison") || lower.Contains("entangle") || lower.Contains("cultivates"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Verdant, "BIND", new Color(0.42f, 0.92f, 0.38f), 0.86f, Audio.SfxCue.SpellPlay);

                if (lower.Contains("drain") || lower.Contains("siphon") || lower.Contains("decays"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Shadow, "DRAIN", new Color(0.72f, 0.42f, 1f), 0.9f, Audio.SfxCue.Hit);

                if (lower.Contains("empowers"))
                    return SpawnMoveVfx(center, BattleEffectAssetKind.Storm, "POWER", new Color(0.54f, 0.9f, 1f), 0.84f, Audio.SfxCue.Ready);
            }

            if (entry.Type == LogEntryType.System && lower.Contains("ritual chain"))
                return SpawnMoveVfx(center, BattleEffectAssetKind.Bind, "CHAIN", new Color(0.68f, 0.78f, 1f), 0.76f, Audio.SfxCue.SpellPlay);

            return false;
        }

        private bool SpawnMoveVfx(Vector3 center, BattleEffectAssetKind kind, string label, Color tint, float scale, Audio.SfxCue cue)
        {
            SpawnCompactMoveSheetVfx(center, kind, tint, scale);
            SpawnFloatingCombatText(center + Vector3.up * 24f, label, new Color(tint.r, tint.g, tint.b, 0.9f), 0.56f);
            Audio.SfxManager.EnsureInstance().Play(cue, 0.20f, 1f);
            return true;
        }

        private void SpawnCompactMoveSheetVfx(Vector3 worldPos, BattleEffectAssetKind kind, Color tint, float scale)
        {
            float compactScale = Mathf.Clamp(scale * 0.72f, 0.5f, 0.9f);
            SpawnImpactBurst(worldPos, new Color(tint.r, tint.g, tint.b, tint.a * 0.72f), 0.95f * compactScale);
            SpawnAetherSheetImpactFX(worldPos, kind, tint, compactScale);
        }

        private static bool TryResolveElementFromMessage(string lower, out Element element)
        {
            foreach (Element candidate in Enum.GetValues(typeof(Element)))
            {
                if (lower.Contains(candidate.ToString().ToLowerInvariant()))
                {
                    element = candidate;
                    return true;
                }
            }

            element = Element.Light;
            return false;
        }

        private static Color ResolveElementTint(Element element)
        {
            return element switch
            {
                Element.Flame => new Color(1f, 0.42f, 0.18f),
                Element.Ice => new Color(0.62f, 0.9f, 1f),
                Element.Water => new Color(0.24f, 0.66f, 1f),
                Element.Earth => new Color(0.82f, 0.62f, 0.32f),
                Element.Air => new Color(0.72f, 0.94f, 1f),
                Element.Nature => new Color(0.36f, 0.86f, 0.34f),
                Element.Dark => new Color(0.66f, 0.34f, 0.92f),
                _ => new Color(1f, 0.96f, 0.62f),
            };
        }

        private IEnumerator DelayedAction(float seconds, System.Action callback)
        {
            yield return new WaitForSeconds(seconds);
            callback?.Invoke();
        }

        private static void DispatchReactiveSfx(LogEntry entry)
        {
            string lower = (entry.Message ?? string.Empty).ToLowerInvariant();
            var sfx = Audio.SfxManager.EnsureInstance();

            // Seal negation — play immediately so the counter-cue lands before the banner
            if (lower.Contains("negated by a seal") || lower.Contains("counter-seal"))
            {
                sfx.Play(Audio.SfxCue.Counter, 0.42f, 1f);
                return;
            }
            // Dispel resolution
            if (lower.Contains("dispelled"))
            {
                sfx.Play(Audio.SfxCue.Discard, 0.38f, 1f);
                return;
            }
            // New high-impact rituals
            if (entry.Type == LogEntryType.Action && lower.Contains("fuses with"))
            {
                sfx.Play(Audio.SfxCue.Fusion, 0.46f, 0.94f);
                return;
            }
            if (entry.Type == LogEntryType.Action && lower.Contains("unleashes invoker power"))
            {
                sfx.Play(Audio.SfxCue.SpellPlay, 0.52f, 0.90f);
                return;
            }
            // Domain / mask / dispel cast
            if (entry.Type == LogEntryType.Action &&
                (lower.Contains("played") || lower.Contains("equipped") || lower.Contains("cast")))
            {
                sfx.Play(Audio.SfxCue.SpellPlay, 0.40f, 0.98f);
                return;
            }
            // Turn transition chime
            if (entry.Type == LogEntryType.System && lower.Contains("'s turn"))
            {
                sfx.Play(Audio.SfxCue.TurnStart, 0.32f, 1f);
            }
        }

        private void AppendLogVisual(LogEntry entry)
        {
            GameObject row = null;
            if (logEntryPrefab != null)
                row = Instantiate(logEntryPrefab, logContainer);

            if (row == null)
            {
                row = new GameObject("LogRow", typeof(RectTransform), typeof(TextMeshProUGUI));
                row.transform.SetParent(logContainer, false);
            }

            var text = row.GetComponentInChildren<TextMeshProUGUI>();
            text ??= row.GetComponent<TextMeshProUGUI>();
            if (text == null)
                text = row.AddComponent<TextMeshProUGUI>();

            text.text = entry.Message;
            text.fontSize = 14f;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = entry.Type switch
            {
                LogEntryType.Effect => new Color(0.95f, 0.80f, 0.45f, 0.96f),
                LogEntryType.Combat => new Color(1f, 0.56f, 0.42f, 0.96f),
                LogEntryType.System => new Color(0.80f, 0.88f, 1f, 0.95f),
                _ => new Color(0.90f, 0.90f, 0.92f, 0.92f),
            };
            text.raycastTarget = false;

            if (logContainer != null && logContainer.childCount > MaxVisibleLogEntries)
                Destroy(logContainer.GetChild(0).gameObject);

            if (logScrollRect != null)
                StartCoroutine(SnapLogToBottom());
        }

        private IEnumerator SnapLogToBottom()
        {
            yield return null;
            if (logScrollRect != null)
                logScrollRect.verticalNormalizedPosition = 0f;
        }

        private static bool ShouldAnnounceEffect(LogEntry entry)
        {
            if (entry == null || entry.Type != LogEntryType.Effect)
                return false;

            return IsSpecialEffectMessage(entry.Message);
        }

        private static bool IsSpecialEffectMessage(string message)
        {
            string lower = (message ?? string.Empty).ToLowerInvariant();
            return lower.StartsWith("domain:")
                || lower.StartsWith("mask:")
                || lower.StartsWith("pillar:")
                || lower.Contains("negated by a seal")
                || lower.Contains("counter-seal")
                || lower.Contains("dispelled")
                || lower.Contains("counter effect");
        }

        private IEnumerator RunEffectAnnouncementQueue()
        {
            while (_effectAnnouncementQueue.Count > 0)
            {
                LogEntry entry = _effectAnnouncementQueue.Dequeue();
                yield return PlayStylizedEffectAnnouncement(entry);
            }

            _effectAnnouncementRoutine = null;
        }

        private IEnumerator PlayStylizedEffectAnnouncement(LogEntry entry)
        {
            if (_combatFxLayer == null)
                yield break;

            string line = entry.Message.Trim();
            EffectScope scope = ResolveEffectScope(line);
            Color tint = ResolveEffectTint(line, entry.Type);
            Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.EffectReveal, 0.36f, 1f);

            var banner = new GameObject("EffectBanner", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            banner.transform.SetParent(_combatFxLayer, false);
            var bannerRect = banner.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.anchoredPosition = Vector2.zero;
            bannerRect.sizeDelta = new Vector2(620f, 106f);

            var bannerImage = banner.GetComponent<Image>();
            bannerImage.sprite = Resources.Load<Sprite>("UI/panel-dark");
            bannerImage.type = bannerImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            bannerImage.color = new Color(0.08f, 0.07f, 0.04f, 0.90f);
            bannerImage.raycastTarget = false;

            var labelGo = new GameObject("EffectLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(banner.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(20f, 12f);
            labelRect.offsetMax = new Vector2(-20f, -12f);

            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = line.ToUpperInvariant();
            label.fontStyle = FontStyles.Bold;
            label.fontSize = 38f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = tint;
            label.outlineColor = new Color(0f, 0f, 0f, 0.85f);
            label.outlineWidth = 0.22f;
            label.raycastTarget = false;

            var cg = banner.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            bannerRect.localScale = Vector3.one * 0.80f;

            float reveal = 0f;
            const float revealDuration = 0.34f;
            while (reveal < revealDuration)
            {
                reveal += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(reveal / revealDuration);
                cg.alpha = p;
                bannerRect.localScale = Vector3.Lerp(Vector3.one * 0.80f, Vector3.one, UIAnimUtils.EaseOutBack(p));
                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.62f);
            Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.BurnAway, 0.34f, 0.94f);

            float burn = 0f;
            const float burnDuration = 0.52f;
            Color startColor = label.color;
            while (burn < burnDuration)
            {
                burn += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(burn / burnDuration);
                label.color = Color.Lerp(startColor, new Color(1f, 0.26f, 0.08f, 0f), p);
                cg.alpha = Mathf.Lerp(1f, 0f, p);
                bannerRect.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 1.18f, p);
                bannerRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 6f, p));
                yield return null;
            }

            Destroy(banner);
            yield return PlayEffectAreaReaction(scope, tint);
        }

        private IEnumerator PlayEffectAreaReaction(EffectScope scope, Color tint)
        {
            if (scope == EffectScope.BoardWide)
            {
                if (_combatFlashOverlay != null)
                    yield return UIAnimUtils.ScreenFlash(_combatFlashOverlay, new Color(tint.r, tint.g, tint.b, 0.86f), 0.24f);

                StartCoroutine(UIAnimUtils.ScreenShake(transform, 5f, 0.22f));
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.DomainPulse, 0.36f, 0.96f);
                HopFieldCards(p1FieldContainer, 12f);
                HopFieldCards(p2FieldContainer, 12f);
                yield return null;
                yield break;
            }

            Vector3 centerWorld = GetRectWorldPoint(_combatFxLayer);
            SpawnImpactBurst(centerWorld, tint, 1.25f);
            Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.SealTrigger, 0.34f, 1f);
            HopFieldCards(p2FieldContainer, 8f);
            yield return null;
        }

        private void HopFieldCards(Transform fieldContainer, float height)
        {
            if (fieldContainer == null)
                return;

            for (int i = 0; i < fieldContainer.childCount; i++)
            {
                Transform slot = fieldContainer.GetChild(i);
                RectTransform cardRect = FindSlotCard(slot) as RectTransform;
                if (cardRect == null)
                    continue;

                StartCoroutine(UIAnimUtils.HopRect(cardRect, height, 0.20f));
            }
        }

        private static EffectScope ResolveEffectScope(string msg)
        {
            if (string.IsNullOrEmpty(msg))
                return EffectScope.Localized;

            string lower = msg.ToLowerInvariant();
            if (lower.Contains("all ")
                || lower.Contains("domain")
                || lower.Contains("weather")
                || lower.Contains("all enemies")
                || lower.Contains("all daemons"))
            {
                return EffectScope.BoardWide;
            }

            return EffectScope.Localized;
        }

        private static Color ResolveEffectTint(string msg, LogEntryType type)
        {
            if (type == LogEntryType.Combat)
                return new Color(1f, 0.42f, 0.32f, 0.96f);
            string lower = msg?.ToLowerInvariant() ?? string.Empty;
            if (lower.Contains("seal") || lower.Contains("negated"))
                return new Color(0.78f, 0.48f, 1f, 0.96f);
            if (lower.Contains("domain") || lower.Contains("weather"))
                return new Color(0.45f, 0.78f, 1f, 0.96f);
            if (lower.Contains("burn") || lower.Contains("flame"))
                return new Color(1f, 0.54f, 0.26f, 0.96f);
            if (lower.Contains("freeze") || lower.Contains("frost") || lower.Contains("ice"))
                return new Color(0.64f, 0.88f, 1f, 0.96f);

            return new Color(0.96f, 0.90f, 0.70f, 0.96f);
        }

        private void AnimateDaemonHealingFx(GameState state)
        {
            if (state?.Players == null || state.Players.Length < 2)
                return;

            var currentAsheById = new Dictionary<string, int>();
            for (int playerIndex = 0; playerIndex < state.Players.Length; playerIndex++)
            {
                var player = state.Players[playerIndex];
                if (player?.Field == null)
                    continue;

                for (int i = 0; i < player.Field.Count; i++)
                {
                    var daemon = player.Field[i];
                    if (daemon == null || string.IsNullOrEmpty(daemon.InstanceId))
                        continue;

                    currentAsheById[daemon.InstanceId] = daemon.CurrentAshe;

                    if (!_daemonAsheSnapshotReady)
                        continue;

                    if (!_lastDaemonAsheById.TryGetValue(daemon.InstanceId, out int priorAshe))
                        continue;

                    int delta = daemon.CurrentAshe - priorAshe;
                    if (delta <= 0)
                        continue;

                    RectTransform daemonRect = GetDaemonCardRect(playerIndex, daemon.InstanceId);
                    if (daemonRect == null)
                        continue;

                    SpawnFloatingCombatText(GetRectWorldPoint(daemonRect) + Vector3.up * 18f,
                        $"+{delta}", new Color(0.30f, 1f, 0.44f, 0.95f), 0.92f);
                }
            }

            _lastDaemonAsheById.Clear();
            foreach (var pair in currentAsheById)
                _lastDaemonAsheById[pair.Key] = pair.Value;
            _daemonAsheSnapshotReady = true;
        }

        private RectTransform GetDaemonCardRect(int playerIndex, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
                return null;

            var map = playerIndex == LocalPlayer ? _p1DaemonCardRects : _p2DaemonCardRects;
            return map.TryGetValue(instanceId, out RectTransform rect) ? rect : null;
        }

        // ─── Game Over ───────────────────────────────────
        private void HandleGameOver(int winner, string reason)
        {
            Debug.Log($"[Battle] HandleGameOver: winner={winner}, reason={reason}");
            _aiTurnRunning = false;
            StopAllCoroutines();
            if (_forfeitButton) _forfeitButton.gameObject.SetActive(false);

            if (_storyBattleConfig != null && winner == LocalPlayer)
            {
                var profile = ProfileManager.Load();
                if (_storyBattleConfig.storyWinChapter > 0)
                    profile.storyChapter = Mathf.Max(profile.storyChapter, _storyBattleConfig.storyWinChapter);
                else
                    profile.storyChapter = Mathf.Max(profile.storyChapter, 2);

                if (_storyBattleConfig.marksTrialComplete)
                    profile.storyTrialComplete = true;

                if (!string.IsNullOrWhiteSpace(_storyBattleConfig.defeatedInvokerId)
                    && !profile.defeatedInvokerIds.Contains(_storyBattleConfig.defeatedInvokerId))
                {
                    profile.defeatedInvokerIds.Add(_storyBattleConfig.defeatedInvokerId);
                }

                if (_storyBattleConfig.isWildDaemonEncounter && _storyBattleConfig.bindWildDaemonOnWin)
                    BindWildDaemonReward(profile);

                ProfileManager.Save(profile);
            }

            var sfx = Audio.SfxManager.EnsureInstance();
            if (winner == LocalPlayer)
                sfx.Play(Audio.SfxCue.Victory, 0.92f, 1f);
            else
                sfx.Play(Audio.SfxCue.Defeat, 0.88f, 0.95f);

            if (gameOverPanel)
            {
                gameOverPanel.SetActive(true);
                gameOverPanel.transform.SetAsLastSibling(); // Render on top of everything
                // Animate game over panel in
                StartCoroutine(UIAnimUtils.PopScale(gameOverPanel.transform, 0.4f, 1.1f));
                var cg = gameOverPanel.GetComponent<CanvasGroup>();
                if (cg == null) cg = gameOverPanel.AddComponent<CanvasGroup>();
                StartCoroutine(UIAnimUtils.FadeIn(cg, 0.4f));
                EnsureGameOverButtons(gameOverPanel.transform);
            }
            if (gameOverText)
            {
                string wildBindLine = _storyBattleConfig != null
                    && _storyBattleConfig.isWildDaemonEncounter
                    && winner == LocalPlayer
                    && _storyBattleConfig.bindWildDaemonOnWin
                    ? "\nDaemon captured and added to your Grimware."
                    : string.Empty;
                gameOverText.text = $"{_battle.State.Players[winner].Name} Wins!\n{reason}{wildBindLine}";
            }
        }

        private void BindWildDaemonReward(PlayerProfile profile)
        {
            if (profile == null || cardDatabase == null || string.IsNullOrWhiteSpace(_storyBattleConfig?.wildDaemonCardId))
                return;

            string cardId = _storyBattleConfig.wildDaemonCardId;
            if (string.IsNullOrWhiteSpace(profile.storyStarterSavedDeckId))
                return;

            SavedDeck grimware = profile.customDecks?.Find(deck => deck.id == profile.storyStarterSavedDeckId);
            if (grimware == null)
                return;

            StoryCaptureHelper.ApplyCapturedDaemon(profile, grimware, cardId, cardDatabase);
        }

        // ─── Forfeit / Game Over Buttons ─────────────────
        private void CreateForfeitButton()
        {
            var canvasRoot = transform as RectTransform;
            Transform parent = canvasRoot != null ? (Transform)canvasRoot : transform;

            var go = new GameObject("ForfeitButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.878f, 0.032f);
            rt.anchorMax = new Vector2(0.976f, 0.064f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.sprite = Resources.Load<Sprite>("UI/panel-dark");
            img.type = img.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.color = new Color(0.22f, 0.11f, 0.11f, 0.92f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "FORFEIT";
            tmp.fontSize = 13;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(1f, 0.7f, 0.7f);
            tmp.alignment = TextAlignmentOptions.Center;

            _forfeitButton = go.GetComponent<Button>();
            _forfeitButton.onClick.AddListener(OnForfeitClicked);
            go.transform.SetAsLastSibling(); // Render above DiceArea
            RefreshBattlefieldLayout(transform as RectTransform);
        }

        private void OnForfeitClicked()
        {
            if (_storyEncounterMode)
            {
                HandleStoryEncounterEnd(false, "Player forfeited.", false);
                return;
            }

            if (_battle == null || _battle.State.GameOver) return;
            _battle.State.GameOver = true;
            HandleGameOver(AIPlayerIndex, "Player forfeited.");
        }

        private void EnsureGameOverButtons(Transform panel)
        {
            if (panel.Find("MenuButton") != null) return;

            var menuGo = new GameObject("MenuButton", typeof(RectTransform), typeof(Image), typeof(Button));
            menuGo.transform.SetParent(panel, false);
            var mrt = menuGo.GetComponent<RectTransform>();
            mrt.anchorMin = new Vector2(0.25f, 0.08f); mrt.anchorMax = new Vector2(0.50f, 0.2f);
            mrt.offsetMin = new Vector2(8, 0); mrt.offsetMax = new Vector2(-4, 0);
            menuGo.GetComponent<Image>().color = new Color(0.18f, 0.14f, 0.22f, 0.95f);
            if (_storyBattleConfig != null)
                menuGo.GetComponent<Button>().onClick.AddListener(() => RuntimeAssetLocator.TryLoadScene("Story", this));
            else
                menuGo.GetComponent<Button>().onClick.AddListener(() =>
                {
                    DeckConverter.ClearStoryBattleConfig();
                    RuntimeAssetLocator.TryLoadScene("MainMenu", this);
                });
            AddButtonLabel(menuGo.transform, _storyBattleConfig != null ? "RETURN TO STORY" : "MAIN MENU", 18, new Color(0.9f, 0.85f, 0.75f));

            var rematchGo = new GameObject("RematchButton", typeof(RectTransform), typeof(Image), typeof(Button));
            rematchGo.transform.SetParent(panel, false);
            var rrt = rematchGo.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0.50f, 0.08f); rrt.anchorMax = new Vector2(0.75f, 0.2f);
            rrt.offsetMin = new Vector2(4, 0); rrt.offsetMax = new Vector2(-8, 0);
            rematchGo.GetComponent<Image>().color = new Color(0.14f, 0.28f, 0.14f, 0.95f);
            rematchGo.GetComponent<Button>().onClick.AddListener(() => RuntimeAssetLocator.TryLoadScene("Battle", this));
            AddButtonLabel(rematchGo.transform, "REMATCH", 18, new Color(0.8f, 1f, 0.8f));
        }

        private static void AddButtonLabel(Transform parent, string text, int size, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
        }

        // ─── Utilities ───────────────────────────────────
        private void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        /// <summary>Visual dice roll when SE is rolled at the start of a turn.</summary>
        private void HandleSERolled(string playerName, int rollAmount, int storedTotal)
        {
            if (diceRoller == null || _suppressSERolledVisual)
                return;

            int displayValue = Mathf.Clamp(rollAmount, 1, GameConstants.SEDiceSides);
            ActivateDiceArea();
            Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.Dice, 0.70f, 1f);
            diceRoller.RollToValue(displayValue, () =>
            {
                Audio.SfxManager.EnsureInstance().Play(Audio.SfxCue.Collect, 0.55f, 1.1f);
                StartCoroutine(HideDiceAfterDelay(1.2f));
            });
        }

        /// <summary>Ensures the DiceArea parent is active so the dice can be seen.</summary>
        private void ActivateDiceArea()
        {
            if (diceRoller == null) return;
            // Re-activate the named DiceArea container if it was hidden during layout.
            // NEVER use diceRoller.transform.parent — if diceRoller is parented to the
            // canvas root, blindly deactivating the parent blacks out the whole screen.
            ResolveDiceAreaContainer();
            if (_diceAreaContainer != null && !_diceAreaContainer.gameObject.activeSelf)
                _diceAreaContainer.gameObject.SetActive(true);
            diceRoller.gameObject.SetActive(true);
        }

        private IEnumerator HideDiceAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            DeactivateDiceArea();
        }

        public void RollDice(int successMin, string successText, string failText, System.Action<int, bool> onComplete = null)
        {
            if (diceRoller == null) { onComplete?.Invoke(0, false); return; }
            diceRoller.RollWithThreshold(successMin, successText, failText, onComplete);
        }

        public void RollDice(System.Action<int> onComplete = null)
        {
            if (diceRoller == null) { onComplete?.Invoke(0); return; }
            diceRoller.Roll(onComplete);
        }

        public void HideDice()
        {
            DeactivateDiceArea();
        }

        private void DeactivateDiceArea()
        {
            if (diceRoller != null)
            {
                diceRoller.Hide();
                // Only deactivate the named DiceArea container, not the raw parent.
                ResolveDiceAreaContainer();
                if (_diceAreaContainer != null)
                    _diceAreaContainer.gameObject.SetActive(false);
            }
        }

        /// <summary>Finds and caches the named "DiceArea" container. Safe to call repeatedly.</summary>
        private void ResolveDiceAreaContainer()
        {
            if (_diceAreaContainer != null) return;
            // Check if the diceRoller's immediate parent is named "DiceArea"
            if (diceRoller != null)
            {
                var p = diceRoller.transform.parent;
                if (p != null && p.name == "DiceArea")
                {
                    _diceAreaContainer = p;
                    return;
                }
            }
            // Walk up to canvas and search for "DiceArea" child
            var canvas = diceRoller != null
                ? diceRoller.GetComponentInParent<Canvas>()
                : GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var found = canvas.transform.Find("DiceArea");
                if (found != null)
                    _diceAreaContainer = found;
            }
            // If "DiceArea" doesn't exist in the hierarchy, _diceAreaContainer stays null
            // and we simply skip parent deactivation — the diceRoller.Hide() is enough.
        }

        private void OnDestroy()
        {
            if (_battle != null)
            {
                _battle.OnStateChanged -= OnStateChanged;
                _battle.OnLogEntry -= AddLogEntry;
                _battle.OnGameOver -= HandleGameOver;
                _battle.OnCombatResolved -= HandleCombatResolved;
                _battle.OnSERolled -= HandleSERolled;
            }

            if (_relayHost != null)
                _relayHost.OnLocalMessage -= HandleRelayHostMessage;

            if (_relayClient != null)
            {
                _relayClient.OnGameStateReceived -= HandleRelayClientSnapshot;
                _relayClient.OnActionConfirmed -= HandleRelayClientActionConfirmed;
                _relayClient.OnActionRejected -= HandleRelayClientActionRejected;
                _relayClient.OnGameOverReceived -= HandleRelayClientGameOver;
                _relayClient.OnHostDisconnected -= HandleRelayDisconnect;
            }
        }
    }

    internal enum CardZoneStyle
    {
        Hand,
        Field,
        Pillar,
    }

    internal enum BoardSocketStyle
    {
        Daemon,
        Pillar,
    }
}
