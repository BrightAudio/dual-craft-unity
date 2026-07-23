// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Multiplayer Menu (In-Game UI)
//
//  Built-in multiplayer lobby that feels part of the game.
//  Shows: Host Game / Join Game / Back
//  Host flow: creates relay → shows join code → waits → starts
//  Join flow: enter code → connect → waits → starts
//  All UI built programmatically in the game's dark/gold theme.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DualCraft.Networking
{
    using Cards;
    using Core;
    using Data;

    public class MultiplayerMenu : MonoBehaviour
    {
        // ── Inspector (assigned by ProjectSetup or manually) ──
        [Header("Card Database")]
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private DeckData defaultDeck;

        // ── Theme Colors ────────────────────────────────
        private static readonly Color DarkBg      = new(0.04f, 0.04f, 0.06f);
        private static readonly Color PanelBg     = new(0.08f, 0.07f, 0.10f, 0.95f);
        private static readonly Color Gold        = new(0.78f, 0.66f, 0.42f);
        private static readonly Color GoldDim     = new(0.55f, 0.45f, 0.28f);
        private static readonly Color Cream       = new(0.96f, 0.94f, 0.91f);
        private static readonly Color ErrorRed    = new(0.85f, 0.25f, 0.25f);
        private static readonly Color SuccessGreen= new(0.25f, 0.78f, 0.45f);

        // ── State ───────────────────────────────────────
        private enum MenuPage { Main, HostWaiting, JoinInput, Connecting }
        private MenuPage _page;

        // ── UI References (created at runtime) ──────────
        private Canvas _canvas;
        private GameObject _mainPanel;
        private GameObject _hostPanel;
        private GameObject _joinPanel;
        private GameObject _connectingPanel;

        // Main panel refs
        private TMP_InputField _playerNameInput;
        private TMP_Text _deckChoiceText;
        private TMP_Text _deckHelpText;
        private TMP_Text _statusText;
        private Button _hostButton;
        private Button _joinButton;
        private Button _backButton;
        private Button _prevDeckButton;
        private Button _nextDeckButton;

        // Host panel refs
        private TMP_Text _joinCodeText;
        private TMP_Text _hostStatusText;
        private Button _copyCodeButton;

        // Join panel refs
        private TMP_InputField _codeInput;
        private TMP_Text _joinStatusText;
        private Button _connectButton;
        private Button _cancelJoinButton;

        // Connecting panel refs
        private TMP_Text _connectingText;

        // ── Components ──────────────────────────────────
        private RelayManager _relay;
        private RelayGameHost _host;
        private RelayGameClient _client;
        private PlayerProfile _profile;
        private readonly List<DeckChoice> _deckChoices = new();
        private int _selectedDeckIndex;
        private bool _loadingBattleScene;

        // ═════════════════════════════════════════════════
        //  LIFECYCLE
        // ═════════════════════════════════════════════════

        private async void Start()
        {
            cardDatabase = RuntimeAssetLocator.LoadCardDatabase(cardDatabase, this);
            defaultDeck = RuntimeAssetLocator.LoadDefaultDeck(defaultDeck, this);
            if (cardDatabase == null || defaultDeck == null)
                return;

            cardDatabase.Initialize();
            _profile = ProfileManager.Load();
            BuildDeckChoices();
            EnsureEventSystem();
            BuildUI();
            RefreshDeckChoice();
            ShowPage(MenuPage.Main);

            // Ensure relay manager exists
            if (RelayManager.Instance == null)
            {
                var go = new GameObject("RelayManager");
                go.AddComponent<RelayManager>();
            }
            _relay = RelayManager.Instance;
            if (_relay != null)
                _relay.OnError += HandleRelayError;

            // Initialize Unity Gaming Services
            _connectingPanel.SetActive(true);
            _connectingText.text = "INITIALIZING...";
            await _relay.InitializeServices();
            _connectingPanel.SetActive(false);
            ShowPage(MenuPage.Main);
            if (!string.IsNullOrWhiteSpace(_relay.LastError) && _statusText != null)
                _statusText.text = _relay.LastError;

            string[] args = Environment.GetCommandLineArgs();
            int joinArg = Array.FindIndex(args, arg => string.Equals(arg, "-relay-smoke-join", StringComparison.OrdinalIgnoreCase));
            if (joinArg >= 0 && joinArg + 1 < args.Length)
            {
                OnJoinClicked();
                _codeInput.text = args[joinArg + 1].Trim().ToUpperInvariant();
                OnConnectClicked();
            }
            else if (args.Any(arg => string.Equals(arg, "-relay-smoke-host", StringComparison.OrdinalIgnoreCase)))
            {
                OnHostClicked();
            }
        }

        // ═════════════════════════════════════════════════
        //  BUILD UI — programmatic, matches game theme
        // ═════════════════════════════════════════════════

        private void BuildUI()
        {
            // Canvas
            var canvasGo = new GameObject("MultiplayerCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // Full-screen dark background
            var bg = CreatePanel(canvasGo.transform, "Background", DarkBg);
            bg.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            bg.GetComponent<RectTransform>().anchorMax = Vector2.one;
            bg.GetComponent<RectTransform>().sizeDelta = Vector2.zero;

            // Title
            var title = CreateText(bg.transform, "Title", "MULTIPLAYER", 52, Gold);
            SetAnchored(title, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(600, 70));

            var subtitle = CreateText(bg.transform, "Subtitle", "Host gets a code. Friend joins. Duel starts.", 22, GoldDim);
            SetAnchored(subtitle, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(600, 40));

            // ── Main Page ───────────────────────────────
            _mainPanel = CreatePanel(bg.transform, "MainPanel", Color.clear);
            SetAnchored(_mainPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 470));

            var nameLabel = CreateText(_mainPanel.transform, "NameLabel", "PLAYER NAME", 16, GoldDim);
            SetAnchored(nameLabel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 175), new Vector2(420, 28));

            _playerNameInput = CreateInputField(_mainPanel.transform, "PlayerNameInput", "Invoker", 28, 18);
            _playerNameInput.text = string.IsNullOrWhiteSpace(_profile?.playerName) ? "Invoker" : _profile.playerName;
            SetAnchored(_playerNameInput.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 132), new Vector2(380, 56));

            var deckLabel = CreateText(_mainPanel.transform, "DeckLabel", "SELECT DECK", 16, GoldDim);
            SetAnchored(deckLabel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 84), new Vector2(420, 28));

            var prevDeckBtn = CreateButton(_mainPanel.transform, "PrevDeckButton", "<", GoldDim, DarkBg, SelectPreviousDeck);
            _prevDeckButton = prevDeckBtn.GetComponent<Button>();
            SetAnchored(prevDeckBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-230, 40), new Vector2(52, 52));

            var deckPanel = CreatePanel(_mainPanel.transform, "DeckChoicePanel", new Color(0.12f, 0.10f, 0.15f, 0.96f));
            SetAnchored(deckPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(380, 56));
            _deckChoiceText = CreateText(deckPanel.transform, "DeckChoiceText", "", 20, Cream).GetComponent<TMP_Text>();
            _deckChoiceText.fontStyle = FontStyles.Bold;
            SetAnchored(_deckChoiceText.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var nextDeckBtn = CreateButton(_mainPanel.transform, "NextDeckButton", ">", GoldDim, DarkBg, SelectNextDeck);
            _nextDeckButton = nextDeckBtn.GetComponent<Button>();
            SetAnchored(nextDeckBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(230, 40), new Vector2(52, 52));

            _deckHelpText = CreateText(_mainPanel.transform, "DeckHelp", "", 15, GoldDim).GetComponent<TMP_Text>();
            SetAnchored(_deckHelpText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(520, 34));

            _statusText = CreateText(_mainPanel.transform, "StatusText", "Pick a deck, then Host or Join.", 16, GoldDim).GetComponent<TMP_Text>();
            _statusText.textWrappingMode = TextWrappingModes.Normal;
            SetAnchored(_statusText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -108), new Vector2(520, 42));

            var hostBtn = CreateButton(_mainPanel.transform, "HostButton", "HOST FRIEND GAME", Gold, DarkBg, OnHostClicked);
            _hostButton = hostBtn.GetComponent<Button>();
            SetAnchored(hostBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -72), new Vector2(340, 60));

            var joinBtn = CreateButton(_mainPanel.transform, "JoinButton", "JOIN WITH CODE", Gold, DarkBg, OnJoinClicked);
            _joinButton = joinBtn.GetComponent<Button>();
            SetAnchored(joinBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -148), new Vector2(340, 60));

            var backBtn = CreateButton(_mainPanel.transform, "BackButton", "BACK", GoldDim, DarkBg, OnBackClicked);
            _backButton = backBtn.GetComponent<Button>();
            SetAnchored(backBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -224), new Vector2(200, 50));

            // ── Host Waiting Page ───────────────────────
            _hostPanel = CreatePanel(bg.transform, "HostPanel", Color.clear);
            SetAnchored(_hostPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 350));

            var codeLabel = CreateText(_hostPanel.transform, "CodeLabel", "SHARE THIS CODE WITH YOUR FRIEND", 20, GoldDim);
            SetAnchored(codeLabel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(500, 30));

            // Join code display — big, gold, monospace
            var codeBg = CreatePanel(_hostPanel.transform, "CodeBg", new Color(0.12f, 0.10f, 0.15f));
            SetAnchored(codeBg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(360, 80));

            _joinCodeText = CreateText(codeBg.transform, "JoinCode", "------", 48, Gold).GetComponent<TMP_Text>();
            _joinCodeText.fontStyle = FontStyles.Bold;
            _joinCodeText.characterSpacing = 12;
            SetAnchored(_joinCodeText.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            _copyCodeButton = CreateButton(_hostPanel.transform, "CopyButton", "COPY CODE", GoldDim, DarkBg, OnCopyCode).GetComponent<Button>();
            SetAnchored(_copyCodeButton.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(200, 44));

            _hostStatusText = CreateText(_hostPanel.transform, "HostStatus", "Waiting for friend to join...", 20, Cream).GetComponent<TMP_Text>();
            SetAnchored(_hostStatusText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(400, 30));

            var cancelHostBtn = CreateButton(_hostPanel.transform, "CancelHost", "CANCEL", ErrorRed, DarkBg, OnCancelHost);
            SetAnchored(cancelHostBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -140), new Vector2(160, 44));

            // ── Join Input Page ─────────────────────────
            _joinPanel = CreatePanel(bg.transform, "JoinPanel", Color.clear);
            SetAnchored(_joinPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 300));

            var enterLabel = CreateText(_joinPanel.transform, "EnterLabel", "ENTER YOUR FRIEND'S CODE", 20, GoldDim);
            SetAnchored(enterLabel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(500, 30));

            // Input field
            var inputGo = new GameObject("CodeInput");
            inputGo.transform.SetParent(_joinPanel.transform, false);
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.12f, 0.10f, 0.15f);
            SetAnchored(inputGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(360, 60));

            // Input text area
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(inputGo.transform, false);
            var textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.sizeDelta = new Vector2(-20, 0);

            var inputTextField = new GameObject("Text");
            inputTextField.transform.SetParent(textArea.transform, false);
            var inputTextRT = inputTextField.AddComponent<RectTransform>();
            inputTextRT.anchorMin = Vector2.zero;
            inputTextRT.anchorMax = Vector2.one;
            inputTextRT.sizeDelta = Vector2.zero;
            var inputTMPText = inputTextField.AddComponent<TextMeshProUGUI>();
            inputTMPText.fontSize = 32;
            inputTMPText.color = Gold;
            inputTMPText.alignment = TextAlignmentOptions.Center;
            inputTMPText.characterSpacing = 8;

            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(textArea.transform, false);
            var placeholderRT = placeholderGo.AddComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.sizeDelta = Vector2.zero;
            var placeholderText = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholderText.text = "Enter code...";
            placeholderText.fontSize = 28;
            placeholderText.color = new Color(0.4f, 0.35f, 0.3f);
            placeholderText.alignment = TextAlignmentOptions.Center;
            placeholderText.fontStyle = FontStyles.Italic;

            _codeInput = inputGo.AddComponent<TMP_InputField>();
            _codeInput.textViewport = textAreaRT;
            _codeInput.textComponent = inputTMPText;
            _codeInput.placeholder = placeholderText;
            _codeInput.characterLimit = 6;
            _codeInput.contentType = TMP_InputField.ContentType.Alphanumeric;

            var connectBtn = CreateButton(_joinPanel.transform, "ConnectButton", "CONNECT", SuccessGreen, DarkBg, OnConnectClicked);
            _connectButton = connectBtn.GetComponent<Button>();
            SetAnchored(connectBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(200, 50));

            _joinStatusText = CreateText(_joinPanel.transform, "JoinStatus", "", 18, ErrorRed).GetComponent<TMP_Text>();
            SetAnchored(_joinStatusText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(400, 30));

            var cancelJoinBtn = CreateButton(_joinPanel.transform, "CancelJoin", "BACK", GoldDim, DarkBg, () => ShowPage(MenuPage.Main));
            _cancelJoinButton = cancelJoinBtn.GetComponent<Button>();
            SetAnchored(cancelJoinBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -130), new Vector2(160, 44));

            // ── Connecting Overlay ──────────────────────
            _connectingPanel = CreatePanel(bg.transform, "ConnectingPanel", new Color(0, 0, 0, 0.8f));
            SetAnchored(_connectingPanel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            _connectingText = CreateText(_connectingPanel.transform, "ConnectingText", "CONNECTING...", 28, Gold).GetComponent<TMP_Text>();
            SetAnchored(_connectingText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 50));
        }

        // ═════════════════════════════════════════════════
        //  NAVIGATION
        // ═════════════════════════════════════════════════

        private void ShowPage(MenuPage page)
        {
            _page = page;
            _mainPanel.SetActive(page == MenuPage.Main);
            _hostPanel.SetActive(page == MenuPage.HostWaiting);
            _joinPanel.SetActive(page == MenuPage.JoinInput);
            _connectingPanel.SetActive(page == MenuPage.Connecting);
            SelectDefaultForPage(page);
        }

        private void Update()
        {
            if (WasCancelPressed())
            {
                if (_page == MenuPage.JoinInput)
                {
                    ShowPage(MenuPage.Main);
                    return;
                }

                if (_page == MenuPage.HostWaiting)
                {
                    OnCancelHost();
                    return;
                }
            }

            if (_page == MenuPage.Main)
            {
                if (WasPreviousPressed())
                    SelectPreviousDeck();
                else if (WasNextPressed())
                    SelectNextDeck();
            }

            if (_page == MenuPage.JoinInput
                && WasSubmitPressed()
                && _codeInput != null
                && !string.IsNullOrWhiteSpace(_codeInput.text))
            {
                OnConnectClicked();
            }
        }

        // ═════════════════════════════════════════════════
        //  BUTTON HANDLERS
        // ═════════════════════════════════════════════════

        private async void OnHostClicked()
        {
            ShowPage(MenuPage.Connecting);
            _connectingText.text = "CREATING ROOM...";

            string code = await _relay.StartHost();
            if (string.IsNullOrEmpty(code))
            {
                if (_statusText != null)
                    _statusText.text = string.IsNullOrWhiteSpace(_relay.LastError)
                        ? "Unable to create room. Check your network connection."
                        : _relay.LastError;
                ShowPage(MenuPage.Main);
                return;
            }

            SavePlayerName();
            DeckConverter.SelectedDeckId = GetSelectedDeckSelectionId();

            // Set up the game host
            if (_host == null)
            {
                _host = _relay.GetComponent<RelayGameHost>() ?? _relay.gameObject.AddComponent<RelayGameHost>();
            }
            _host.Initialize(cardDatabase, GetSelectedDeckData(), GetPlayerName(), GetNetworkPlayerId());
            _host.OnGuestJoined += () =>
            {
                _hostStatusText.text = "<color=#40C96E>Opponent connected!</color>";
            };
            _host.OnGameStarted += () =>
            {
                if (_loadingBattleScene) return;
                _loadingBattleScene = true;
                Debug.Log("[MultiplayerMenu] Game started! Loading battle...");
                RuntimeAssetLocator.TryLoadScene("Battle", this);
            };

            _joinCodeText.text = code.ToUpper();
            _hostStatusText.text = "Waiting for opponent...";
            ShowPage(MenuPage.HostWaiting);
        }

        private void OnJoinClicked()
        {
            _joinStatusText.text = "";
            _codeInput.text = "";
            ShowPage(MenuPage.JoinInput);
            _codeInput.ActivateInputField();
        }

        private void OnConnectClicked()
        {
            string code = _codeInput.text.Trim().ToUpper();
            if (code.Length < 4)
            {
                _joinStatusText.text = "Code must be at least 4 characters.";
                return;
            }

            ShowPage(MenuPage.Connecting);
            _connectingText.text = "JOINING GAME...";

            if (_client == null)
            {
                _client = _relay.GetComponent<RelayGameClient>() ?? _relay.gameObject.AddComponent<RelayGameClient>();
            }
            _client.OnConnectedToHost += () =>
            {
                _connectingText.text = "CONNECTED! WAITING FOR HOST...";
            };
            _client.OnGameStateReceived += _ =>
            {
                if (_loadingBattleScene) return;
                _loadingBattleScene = true;
                Debug.Log("[MultiplayerMenu] Game state received, loading battle...");
                RuntimeAssetLocator.TryLoadScene("Battle", this);
            };
            _client.OnError += msg =>
            {
                _joinStatusText.text = msg;
                ShowPage(MenuPage.JoinInput);
            };

            SavePlayerName();
            DeckConverter.SelectedDeckId = GetSelectedDeckSelectionId();
            _client.Connect(code, GetPlayerName(), GetSelectedDeckSelectionId(), GetSelectedDeckData());
        }

        private void OnCopyCode()
        {
            GUIUtility.systemCopyBuffer = _relay.JoinCode;
            _copyCodeButton.GetComponentInChildren<TMP_Text>().text = "COPIED!";
            Invoke(nameof(ResetCopyButton), 2f);
        }

        private void ResetCopyButton()
        {
            if (_copyCodeButton != null)
                _copyCodeButton.GetComponentInChildren<TMP_Text>().text = "COPY CODE";
        }

        private void OnCancelHost()
        {
            _relay.Shutdown();
            if (_host != null) { Destroy(_host); _host = null; }
            ShowPage(MenuPage.Main);
        }

        private void OnBackClicked()
        {
            _relay?.Shutdown();
            RuntimeAssetLocator.TryLoadScene("MainMenu", this);
        }

        private void HandleRelayError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                message = "An unknown network error occurred.";

            if (_page == MenuPage.HostWaiting)
            {
                _hostStatusText.text = $"ERROR: {message}";
                return;
            }

            if (_page == MenuPage.JoinInput || _page == MenuPage.Connecting)
            {
                _joinStatusText.text = message;
                ShowPage(MenuPage.JoinInput);
                return;
            }

            if (_statusText != null)
                _statusText.text = message;

            ShowPage(MenuPage.Main);
        }

        private void OnDestroy()
        {
            if (_relay != null)
                _relay.OnError -= HandleRelayError;
        }

        // ═════════════════════════════════════════════════
        //  PLAYER / DECK SELECTION
        // ═════════════════════════════════════════════════

        private void BuildDeckChoices()
        {
            _deckChoices.Clear();

            var profile = ProfileManager.Load();
            foreach (var saved in profile.customDecks ?? new List<SavedDeck>())
            {
                var runtime = DeckConverter.ToRuntimeDeck(saved, cardDatabase);
                if (runtime != null && runtime.IsValid)
                    _deckChoices.Add(new DeckChoice(saved.name, saved.id, runtime, true));
            }

            var prebuiltDecks = Resources.LoadAll<DeckData>("CardData/Decks")
                .Where(deck => deck != null)
                .Select(deck => deck.CreatePlayableRuntimeCopy())
                .Where(deck => deck != null && deck.IsValid)
                .OrderBy(deck => deck.primaryCreatureType)
                .ThenBy(deck => deck.element)
                .ThenBy(deck => deck.deckName);

            foreach (var deck in prebuiltDecks)
                _deckChoices.Add(new DeckChoice(deck.deckName, "prebuilt:" + deck.deckName, deck, false));

            if (_deckChoices.Count == 0 && defaultDeck != null)
                _deckChoices.Add(new DeckChoice(defaultDeck.deckName, "prebuilt:" + defaultDeck.deckName, defaultDeck, false));

            _selectedDeckIndex = Mathf.Clamp(_selectedDeckIndex, 0, Mathf.Max(0, _deckChoices.Count - 1));
        }

        private void SelectPreviousDeck()
        {
            if (_deckChoices.Count == 0) return;
            _selectedDeckIndex = (_selectedDeckIndex - 1 + _deckChoices.Count) % _deckChoices.Count;
            RefreshDeckChoice();
        }

        private void SelectNextDeck()
        {
            if (_deckChoices.Count == 0) return;
            _selectedDeckIndex = (_selectedDeckIndex + 1) % _deckChoices.Count;
            RefreshDeckChoice();
        }

        private void RefreshDeckChoice()
        {
            if (_deckChoiceText == null) return;

            if (_deckChoices.Count == 0)
            {
                _deckChoiceText.text = "No valid decks";
                if (_deckHelpText != null)
                    _deckHelpText.text = $"Build a {GameConstants.DeckSize}-card deck before playing online.";
                return;
            }

            var choice = _deckChoices[_selectedDeckIndex];
            _deckChoiceText.text = choice.Label;
            if (_deckHelpText != null)
            {
                string source = choice.IsCustom ? "Custom" : "Prebuilt";
                _deckHelpText.text = $"{source} · {choice.Deck.element} · {choice.Deck.primaryCreatureType} · {choice.Deck.TotalMainCards} cards";
            }
        }

        private void SelectDefaultForPage(MenuPage page)
        {
            if (EventSystem.current == null)
                return;

            GameObject selected = page switch
            {
                MenuPage.Main => _hostButton != null ? _hostButton.gameObject : null,
                MenuPage.HostWaiting => _copyCodeButton != null ? _copyCodeButton.gameObject : null,
                MenuPage.JoinInput => _codeInput != null ? _codeInput.gameObject : _connectButton != null ? _connectButton.gameObject : null,
                _ => null,
            };

            if (selected != null && selected.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(selected);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(go);
        }

        private static bool WasSubmitPressed()
        {
            bool pressed = Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.JoystickButton0);
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            pressed |= keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame
                    || keyboard.numpadEnterKey.wasPressedThisFrame
                    || keyboard.spaceKey.wasPressedThisFrame);
            pressed |= gamepad != null
                && (gamepad.buttonSouth.wasPressedThisFrame
                    || gamepad.startButton.wasPressedThisFrame);
#endif
            return pressed;
        }

        private static bool WasCancelPressed()
        {
            bool pressed = Input.GetKeyDown(KeyCode.Escape)
                || Input.GetKeyDown(KeyCode.Backspace)
                || Input.GetKeyDown(KeyCode.JoystickButton1);
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            pressed |= keyboard != null
                && (keyboard.escapeKey.wasPressedThisFrame
                    || keyboard.backspaceKey.wasPressedThisFrame);
            pressed |= gamepad != null
                && (gamepad.buttonEast.wasPressedThisFrame
                    || gamepad.selectButton.wasPressedThisFrame);
#endif
            return pressed;
        }

        private static bool WasPreviousPressed()
        {
            bool pressed = Input.GetKeyDown(KeyCode.Q)
                || Input.GetKeyDown(KeyCode.LeftBracket)
                || Input.GetKeyDown(KeyCode.JoystickButton4);
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            pressed |= keyboard != null
                && (keyboard.qKey.wasPressedThisFrame
                    || keyboard.leftBracketKey.wasPressedThisFrame);
            pressed |= gamepad != null
                && (gamepad.leftShoulder.wasPressedThisFrame
                    || gamepad.dpad.left.wasPressedThisFrame);
#endif
            return pressed;
        }

        private static bool WasNextPressed()
        {
            bool pressed = Input.GetKeyDown(KeyCode.E)
                || Input.GetKeyDown(KeyCode.RightBracket)
                || Input.GetKeyDown(KeyCode.JoystickButton5);
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            pressed |= keyboard != null
                && (keyboard.eKey.wasPressedThisFrame
                    || keyboard.rightBracketKey.wasPressedThisFrame);
            pressed |= gamepad != null
                && (gamepad.rightShoulder.wasPressedThisFrame
                    || gamepad.dpad.right.wasPressedThisFrame);
#endif
            return pressed;
        }

        private string GetPlayerName()
        {
            string value = _playerNameInput != null ? _playerNameInput.text : _profile?.playerName;
            return string.IsNullOrWhiteSpace(value) ? "Invoker" : value.Trim();
        }

        private static string GetNetworkPlayerId()
        {
            try
            {
                var auth = Unity.Services.Authentication.AuthenticationService.Instance;
                if (auth != null && auth.IsSignedIn && !string.IsNullOrWhiteSpace(auth.PlayerId))
                    return auth.PlayerId;
            }
            catch
            {
                // Anonymous auth may be unavailable in local/offline testing.
            }

            return $"local-{SystemInfo.deviceUniqueIdentifier}";
        }

        private void SavePlayerName()
        {
            var profile = ProfileManager.Load();
            profile.playerName = GetPlayerName();
            ProfileManager.Save(profile);
            _profile = profile;
        }

        private DeckData GetSelectedDeckData()
        {
            if (_deckChoices.Count == 0) return defaultDeck;
            return _deckChoices[Mathf.Clamp(_selectedDeckIndex, 0, _deckChoices.Count - 1)].Deck;
        }

        private string GetSelectedDeckSelectionId()
        {
            if (_deckChoices.Count == 0) return string.Empty;
            return _deckChoices[Mathf.Clamp(_selectedDeckIndex, 0, _deckChoices.Count - 1)].SelectionId;
        }

        // ═════════════════════════════════════════════════
        //  UI HELPERS — build themed UI elements
        // ═════════════════════════════════════════════════

        private static GameObject CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static GameObject CreateText(Transform parent, string name, string text,
                                             int fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            return go;
        }

        private static GameObject CreateButton(Transform parent, string name, string label,
                                               Color borderColor, Color textBgColor,
                                               Action onClick)
        {
            // Button background
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = borderColor;

            // Inner dark fill
            var inner = new GameObject("Inner");
            inner.transform.SetParent(go.transform, false);
            var innerRT = inner.AddComponent<RectTransform>();
            innerRT.anchorMin = Vector2.zero;
            innerRT.anchorMax = Vector2.one;
            innerRT.sizeDelta = new Vector2(-4, -4); // 2px border
            var innerImg = inner.AddComponent<Image>();
            innerImg.color = textBgColor;

            // Label
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(inner.transform, false);
            var labelRT = labelGo.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.sizeDelta = Vector2.zero;
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 20;
            tmp.color = borderColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = borderColor;
            colors.highlightedColor = borderColor * 1.2f;
            colors.pressedColor = borderColor * 0.8f;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());

            return go;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, string placeholder, int fontSize, int placeholderFontSize)
        {
            var inputGo = new GameObject(name);
            inputGo.transform.SetParent(parent, false);
            inputGo.AddComponent<RectTransform>();
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.12f, 0.10f, 0.15f);

            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(inputGo.transform, false);
            var textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(16, 0);
            textAreaRT.offsetMax = new Vector2(-16, 0);

            var inputTextField = new GameObject("Text");
            inputTextField.transform.SetParent(textArea.transform, false);
            var inputTextRT = inputTextField.AddComponent<RectTransform>();
            inputTextRT.anchorMin = Vector2.zero;
            inputTextRT.anchorMax = Vector2.one;
            inputTextRT.sizeDelta = Vector2.zero;
            var inputTMPText = inputTextField.AddComponent<TextMeshProUGUI>();
            inputTMPText.fontSize = fontSize;
            inputTMPText.color = Gold;
            inputTMPText.alignment = TextAlignmentOptions.Center;

            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(textArea.transform, false);
            var placeholderRT = placeholderGo.AddComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.sizeDelta = Vector2.zero;
            var placeholderText = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = placeholderFontSize;
            placeholderText.color = new Color(0.48f, 0.42f, 0.34f);
            placeholderText.alignment = TextAlignmentOptions.Center;
            placeholderText.fontStyle = FontStyles.Italic;

            var input = inputGo.AddComponent<TMP_InputField>();
            input.textViewport = textAreaRT;
            input.textComponent = inputTMPText;
            input.placeholder = placeholderText;
            input.characterLimit = 20;
            input.contentType = TMP_InputField.ContentType.Standard;
            return input;
        }

        private static void SetAnchored(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
                                         Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
        }

        private sealed class DeckChoice
        {
            public string Label { get; }
            public string SelectionId { get; }
            public DeckData Deck { get; }
            public bool IsCustom { get; }

            public DeckChoice(string label, string selectionId, DeckData deck, bool isCustom)
            {
                Label = string.IsNullOrWhiteSpace(label) ? "Unnamed Deck" : label;
                SelectionId = selectionId;
                Deck = deck;
                IsCustom = isCustom;
            }
        }
    }
}
