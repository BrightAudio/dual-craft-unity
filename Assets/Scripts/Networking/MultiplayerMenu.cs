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
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace DualCraft.Networking
{
    using Cards;

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

        // Host panel refs
        private TMP_Text _joinCodeText;
        private TMP_Text _hostStatusText;
        private Button _copyCodeButton;

        // Join panel refs
        private TMP_InputField _codeInput;
        private TMP_Text _joinStatusText;

        // Connecting panel refs
        private TMP_Text _connectingText;

        // ── Components ──────────────────────────────────
        private RelayManager _relay;
        private RelayGameHost _host;
        private RelayGameClient _client;

        // ═════════════════════════════════════════════════
        //  LIFECYCLE
        // ═════════════════════════════════════════════════

        private async void Start()
        {
            BuildUI();
            ShowPage(MenuPage.Main);

            // Ensure relay manager exists
            if (RelayManager.Instance == null)
            {
                var go = new GameObject("RelayManager");
                go.AddComponent<RelayManager>();
            }
            _relay = RelayManager.Instance;

            // Initialize Unity Gaming Services
            _connectingPanel.SetActive(true);
            _connectingText.text = "INITIALIZING...";
            await _relay.InitializeServices();
            _connectingPanel.SetActive(false);
            ShowPage(MenuPage.Main);
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

            var subtitle = CreateText(bg.transform, "Subtitle", "Challenge a friend to a duel", 22, GoldDim);
            SetAnchored(subtitle, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(600, 40));

            // ── Main Page ───────────────────────────────
            _mainPanel = CreatePanel(bg.transform, "MainPanel", Color.clear);
            SetAnchored(_mainPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 300));

            var hostBtn = CreateButton(_mainPanel.transform, "HostButton", "HOST GAME", Gold, DarkBg, OnHostClicked);
            SetAnchored(hostBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(320, 60));

            var joinBtn = CreateButton(_mainPanel.transform, "JoinButton", "JOIN GAME", Gold, DarkBg, OnJoinClicked);
            SetAnchored(joinBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(320, 60));

            var backBtn = CreateButton(_mainPanel.transform, "BackButton", "BACK", GoldDim, DarkBg, OnBackClicked);
            SetAnchored(backBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(200, 50));

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

            _hostStatusText = CreateText(_hostPanel.transform, "HostStatus", "Waiting for opponent...", 20, Cream).GetComponent<TMP_Text>();
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
            SetAnchored(connectBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(200, 50));

            _joinStatusText = CreateText(_joinPanel.transform, "JoinStatus", "", 18, ErrorRed).GetComponent<TMP_Text>();
            SetAnchored(_joinStatusText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(400, 30));

            var cancelJoinBtn = CreateButton(_joinPanel.transform, "CancelJoin", "BACK", GoldDim, DarkBg, () => ShowPage(MenuPage.Main));
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
                ShowPage(MenuPage.Main);
                return;
            }

            // Set up the game host
            if (_host == null)
            {
                _host = gameObject.AddComponent<RelayGameHost>();
            }
            _host.Initialize(cardDatabase, defaultDeck, "Host", Unity.Services.Authentication.AuthenticationService.Instance.PlayerId);
            _host.OnGuestJoined += () =>
            {
                _hostStatusText.text = "<color=#40C96E>Opponent connected!</color>";
            };
            _host.OnGameStarted += () =>
            {
                Debug.Log("[MultiplayerMenu] Game started! Loading battle...");
                SceneManager.LoadScene("Battle");
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
                _client = gameObject.AddComponent<RelayGameClient>();
            }
            _client.OnConnectedToHost += () =>
            {
                _connectingText.text = "CONNECTED! WAITING FOR HOST...";
            };
            _client.OnGameStateReceived += _ =>
            {
                Debug.Log("[MultiplayerMenu] Game state received, loading battle...");
                SceneManager.LoadScene("Battle");
            };
            _client.OnError += msg =>
            {
                _joinStatusText.text = msg;
                ShowPage(MenuPage.JoinInput);
            };

            _client.Connect(code, "Guest", "default_deck");
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
            SceneManager.LoadScene("MainMenu");
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
    }
}
