// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Deck Builder Screen
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

namespace DualCraft.UI
{
    using Cards;
    using Core;
    using Data;
    using Story;

    public class DeckBuilderScreen : MonoBehaviour
    {
        [Header("Database")]
        [SerializeField] private CardDatabase cardDatabase;

        [Header("Card Pool")]
        [SerializeField] private Transform cardPoolContainer;
        [SerializeField] private GameObject cardThumbnailPrefab;
        [SerializeField] private TMP_Dropdown elementFilterDropdown;

        [Header("Deck Panel")]
        [SerializeField] private Transform deckListContainer;
        [SerializeField] private Transform pillarListContainer;
        [SerializeField] private GameObject deckEntryPrefab;
        [SerializeField] private TMP_InputField deckNameInput;
        [SerializeField] private TMP_Dropdown deckElementDropdown;

        [Header("Stats")]
        [SerializeField] private TextMeshProUGUI cardCountText;
        [SerializeField] private TextMeshProUGUI pillarCountText;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button clearButton;
        [SerializeField] private Button autoBuildButton;
        [SerializeField] private Button synergyButton;
        [SerializeField] private TextMeshProUGUI synergyText;
        [SerializeField] private Button backButton;

        [Header("Deck Browser")]
        [SerializeField] private GameObject deckBrowserPage;
        [SerializeField] private Transform savedDeckListContainer;
        [SerializeField] private Button buildPageButton;
        [SerializeField] private Button decksPageButton;

        // Deck being built
        private Element _deckElement = Element.Flame;
        private Element _poolFilter = Element.Flame;
        private readonly List<CardData> _deckCards = new();
        private readonly List<PillarCardData> _deckPillars = new();
        private readonly Dictionary<string, GameObject> _poolCardObjects = new();
        private string _editingDeckId; // null = new deck, set when editing existing
        private GameObject _poolScrollPage;
        private string _highlightedCardId;
        private TextMeshProUGUI _deckSummaryText;
        private Transform _selectedCardPreviewRoot;
        private readonly Color _inkColor = new Color(0.18f, 0.10f, 0.05f, 1f);
        private readonly Color _goldInkColor = new Color(0.78f, 0.52f, 0.18f, 1f);
        private readonly Color _pageColor = new Color(0.92f, 0.78f, 0.52f, 0.90f);

        private GameObject CreateDeckEntryPrefab()
        {
            var go = new GameObject("DeckEntry", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 36);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.78f, 0.58f, 0.32f, 0.58f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8, 2);
            trt.offsetMax = new Vector2(-8, -2);
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 14;
            tmp.color = _inkColor;
            tmp.alignment = TextAlignmentOptions.Left;

            go.SetActive(false); // keep prefab inactive
            return go;
        }

        private void Start()
        {
            cardDatabase = RuntimeAssetLocator.LoadCardDatabase(cardDatabase, this);
            if (cardDatabase == null)
                return;

            cardThumbnailPrefab = RuntimeAssetLocator.LoadCardPrefab(cardThumbnailPrefab, this);
            if (cardThumbnailPrefab == null)
                Debug.LogWarning("[DeckBuilder] Card prefab missing; using compact fallback cards in tome slots.");

            cardDatabase.Initialize();

            // Create deckEntryPrefab at runtime if not assigned in Inspector
            if (deckEntryPrefab == null)
                deckEntryPrefab = CreateDeckEntryPrefab();

            ApplyGrimoireSkin();

            elementFilterDropdown?.onValueChanged.AddListener(OnFilterChanged);
            deckElementDropdown?.onValueChanged.AddListener(i => { _deckElement = (Element)Mathf.Clamp(i, 0, System.Enum.GetValues(typeof(Element)).Length - 1); RefreshDeckPanel(); RefreshPool(); });

            if (elementFilterDropdown != null)
                _poolFilter = (Element)Mathf.Clamp(elementFilterDropdown.value, 0, System.Enum.GetValues(typeof(Element)).Length - 1);
            if (deckElementDropdown != null)
                _deckElement = (Element)Mathf.Clamp(deckElementDropdown.value, 0, System.Enum.GetValues(typeof(Element)).Length - 1);

            saveButton?.onClick.AddListener(SaveDeck);
            clearButton?.onClick.AddListener(ClearDeck);
            autoBuildButton?.onClick.AddListener(AutoBuildDeck);
            // Auto-find buttons if not assigned in Inspector
            if (backButton == null)
            {
                var go = GameObject.Find("BackButton");
                if (go != null) backButton = go.GetComponent<Button>();
            }
            backButton?.onClick.AddListener(() => RuntimeAssetLocator.TryLoadScene("MainMenu", this));

            // Create auto-build button next to existing buttons if not in scene
            if (autoBuildButton == null)
            {
                var go = GameObject.Find("AutoBuildButton");
                if (go != null)
                    autoBuildButton = go.GetComponent<Button>();
                else if (clearButton != null)
                {
                    var abGo = Instantiate(clearButton.gameObject, clearButton.transform.parent);
                    abGo.name = "AutoBuildButton";
                    var txt = abGo.GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null) txt.text = "AUTO SCRIPT";
                    abGo.transform.SetSiblingIndex(clearButton.transform.GetSiblingIndex());
                    autoBuildButton = abGo.GetComponent<Button>();
                    autoBuildButton.onClick.RemoveAllListeners();
                    autoBuildButton.onClick.AddListener(AutoBuildDeck);
                }
            }

            EnsureRuntimeDeckBrowser();
            EnsureRuntimeSynergyButton();
            ApplyGrimoireSkin();

            RefreshPool();
            RefreshDeckPanel();
            PopulateDeckBrowser();
        }

        private void OnFilterChanged(int index)
        {
            _poolFilter = (Element)Mathf.Clamp(index, 0, System.Enum.GetValues(typeof(Element)).Length - 1);
            RefreshPool();
        }

        private void RefreshPool()
        {
            ClearChildren(cardPoolContainer);
            _poolCardObjects.Clear();

            var cards = cardDatabase.GetAllCards()
                .Where(c =>
                {
                    if (IsRetiredPillarCard(c)) return false;
                    // Show daemons of the filter element + neutral support cards
                    if (c is DaemonCardData d) return d.element == _poolFilter;
                    return true; // Relics, seals, domains, dispels, and hexes are element-neutral
                })
                .OrderBy(c => c.GetWillCost())
                .ToList();

            foreach (var card in cards)
            {
                var go = Instantiate(cardThumbnailPrefab, cardPoolContainer);
                ConfigureCardThumbnailRoot(go, new Vector2(164f, 230f));
                var visual = go.GetComponent<CardVisual>();
                if (visual != null) visual.SetCard(card);
                DisableChildRaycasts(go.transform);

                var btn = go.GetComponent<Button>();
                if (btn == null) btn = go.AddComponent<Button>();
                var capturedCard = card;
                btn.onClick.AddListener(() => AddCardToDeck(capturedCard));
                EnsureFullCardClickTarget(go.transform, () => AddCardToDeck(capturedCard));
                EnsureVisibleCardClickForwarders(go.transform, () => AddCardToDeck(capturedCard));
                _poolCardObjects[card.cardId] = go;
                ApplySynergyHighlight(go, card);
                ApplyPoolSelectedBadge(go, card);

                // Show a small badge on cards that count as Capture/Seal cards
                try
                {
                    if (StoryCaptureHelper.IsCaptureCard(card))
                    {
                        // Prefer a sprite at Resources/UI/capture_badge, fallback to text
                        var badgeSprite = Resources.Load<Sprite>("UI/capture_badge");
                        if (badgeSprite != null)
                        {
                            var badge = new GameObject("CaptureBadge", typeof(RectTransform), typeof(Image));
                            badge.transform.SetParent(go.transform, false);
                            var brt = badge.GetComponent<RectTransform>();
                            brt.anchorMin = new Vector2(1f, 1f);
                            brt.anchorMax = new Vector2(1f, 1f);
                            brt.pivot = new Vector2(1f, 1f);
                            brt.anchoredPosition = new Vector2(-6f, -6f);
                            brt.sizeDelta = new Vector2(48f, 18f);

                            var img = badge.GetComponent<Image>();
                            img.sprite = badgeSprite;
                            img.color = Color.white;
                            img.raycastTarget = false;
                        }
                        else
                        {
                            var badge = new GameObject("CaptureBadge", typeof(RectTransform), typeof(TextMeshProUGUI));
                            badge.transform.SetParent(go.transform, false);
                            var brt = badge.GetComponent<RectTransform>();
                            brt.anchorMin = new Vector2(1f, 1f);
                            brt.anchorMax = new Vector2(1f, 1f);
                            brt.pivot = new Vector2(1f, 1f);
                            brt.anchoredPosition = new Vector2(-6f, -6f);
                            brt.sizeDelta = new Vector2(56f, 18f);

                            var btxt = badge.GetComponent<TextMeshProUGUI>();
                            btxt.text = "CAPTURE";
                            btxt.fontSize = 12;
                            btxt.color = new Color(0.98f, 0.82f, 0.17f);
                            btxt.alignment = TextAlignmentOptions.Center;
                            btxt.enableWordWrapping = false;
                        }
                    }
                }
                catch
                {
                    // No-op if Resources/TMPro isn't available in this context
                }
            }
        }

        private void AddCardToDeck(CardData card)
        {
            if (IsRetiredPillarCard(card)) return;

            if (card is PillarCardData pillar)
            {
                if (_deckPillars.Count >= GameConstants.PillarCount) return;
                if (pillar.element != _deckElement) return;
                _deckPillars.Add(pillar);
            }
            else
            {
                if (_deckCards.Count >= GameConstants.DeckSize)
                {
                    ShowDeckBuilderHint("Tome is full. Remove a card or bind this tome.");
                    return;
                }
                int copies = _deckCards.Count(c => c.cardId == card.cardId);
                if (copies >= GameConstants.MaxCardCopies)
                {
                    ShowDeckBuilderHint($"{card.cardName} is already at {GameConstants.MaxCardCopies} copies.");
                    return;
                }
                _deckCards.Add(card);
                ShowDeckBuilderHint($"+ {card.cardName}");
            }

            RefreshDeckPanel();
            if (!string.IsNullOrEmpty(_highlightedCardId))
                HighlightBestSynergyCard();
        }

        private void RemoveCardFromDeck(int index, bool isPillar)
        {
            if (isPillar)
            {
                if (index >= 0 && index < _deckPillars.Count)
                    _deckPillars.RemoveAt(index);
            }
            else
            {
                if (index >= 0 && index < _deckCards.Count)
                    _deckCards.RemoveAt(index);
            }

            RefreshDeckPanel();
            if (!string.IsNullOrEmpty(_highlightedCardId))
                HighlightBestSynergyCard();
        }

        private void RemoveOneCardFromDeck(CardData card, bool isPillar)
        {
            if (card == null) return;

            if (isPillar)
            {
                int index = _deckPillars.FindIndex(p => p != null && p.cardId == card.cardId);
                if (index >= 0) _deckPillars.RemoveAt(index);
            }
            else
            {
                int index = _deckCards.FindIndex(c => c != null && c.cardId == card.cardId);
                if (index >= 0) _deckCards.RemoveAt(index);
            }

            RefreshDeckPanel();
            if (!string.IsNullOrEmpty(_highlightedCardId))
                HighlightBestSynergyCard();
        }

        private void RefreshDeckPanel()
        {
            // Main card count
            if (cardCountText)
            {
                bool full = _deckCards.Count == GameConstants.DeckSize;
                cardCountText.text = $"Runes: {_deckCards.Count}/{GameConstants.DeckSize}";
                cardCountText.color = full ? new Color(0.16f, 0.42f, 0.16f) : _inkColor;
            }

            if (pillarCountText)
            {
                pillarCountText.text = "";
                pillarCountText.gameObject.SetActive(false);
            }

            // Save button state
            if (saveButton)
                saveButton.interactable = IsDeckReadyToSave(out _);

            // Retired pillar data can still exist in older saves, but the Grimoire no longer builds with it.
            _deckPillars.Clear();
            ClearChildren(pillarListContainer);
            if (pillarListContainer != null)
                pillarListContainer.gameObject.SetActive(false);

            // Refresh grimware card list
            ClearChildren(deckListContainer);
            foreach (var group in _deckCards
                .Where(c => c != null)
                .GroupBy(c => c.cardId)
                .Select(g => new { Card = g.First(), Count = g.Count() })
                .OrderBy(g => g.Card.category)
                .ThenBy(g => g.Card.GetWillCost())
                .ThenBy(g => g.Card.cardName))
            {
                var go = Instantiate(deckEntryPrefab, deckListContainer);
                go.SetActive(true);
                var text = go.GetComponentInChildren<TextMeshProUGUI>();
                if (text) text.text = $"{group.Count}x {group.Card.cardName}";

                var capturedCard = group.Card;
                var btn = go.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(() => RemoveOneCardFromDeck(capturedCard, false));
            }

            UpdateDeckSummaryOverlay();
            UpdateSelectedCardPreviews();
            UpdatePoolSelectedBadges();
        }

        private void SaveDeck()
        {
            if (!IsDeckReadyToSave(out string validationError))
            {
                ShowDeckBuilderHint(validationError);
                return;
            }

            string deckName = deckNameInput?.text ?? "Custom Grimoire";
            if (string.IsNullOrWhiteSpace(deckName)) deckName = "Custom Grimoire";

            var profile = ProfileManager.Load();
            var saved = new SavedDeck
            {
                id = _editingDeckId ?? System.Guid.NewGuid().ToString(),
                name = deckName,
                element = _deckElement,
                cardIds = _deckCards.Select(c => c.cardId).ToList(),
                pillarIds = new List<string>()
            };
            ProfileManager.SaveDeck(profile, saved);
            _editingDeckId = saved.id;

            Debug.Log($"[DeckBuilder] Saved grimoire '{deckName}' ({saved.id}) with {_deckCards.Count} cards.");

            // Flash save button green briefly
            if (saveButton != null)
                StartCoroutine(FlashButton(saveButton, Color.green, 0.6f));

            PopulateDeckBrowser();
        }

        private bool IsDeckReadyToSave(out string error)
        {
            error = null;

            if (_deckCards.Count != GameConstants.DeckSize)
            {
                error = $"Tome needs {GameConstants.DeckSize} cards.";
                return false;
            }

            int sourceCount = _deckCards.OfType<AsheCardData>().Count();
            if (sourceCount != GameConstants.DeckAsheCount)
            {
                error = $"Tome needs exactly {GameConstants.DeckAsheCount} Sources.";
                return false;
            }

            int daemonCount = _deckCards.OfType<DaemonCardData>().Count();
            if (daemonCount < GameConstants.DeckDaemonCount)
            {
                error = $"Tome needs at least {GameConstants.DeckDaemonCount} Daemons.";
                return false;
            }

            foreach (var group in _deckCards.Where(c => c != null).GroupBy(c => c.cardId))
            {
                if (group.Count() > GameConstants.MaxCardCopies)
                {
                    error = $"{group.First().cardName} is over {GameConstants.MaxCardCopies} copies.";
                    return false;
                }
            }

            return true;
        }

        private System.Collections.IEnumerator FlashButton(Button btn, Color color, float duration)
        {
            var img = btn.GetComponent<Image>();
            if (img == null) yield break;
            var orig = img.color;
            img.color = color;
            yield return new WaitForSeconds(duration);
            if (img) img.color = orig;
        }

        public void LoadDeckForEditing(SavedDeck deck)
        {
            ClearDeck();
            _editingDeckId = deck.id;
            _deckElement = deck.element;
            if (deckNameInput) deckNameInput.text = deck.name;

            foreach (var id in deck.cardIds ?? new List<string>())
            {
                var card = cardDatabase.GetCard(id);
                if (card != null) _deckCards.Add(card);
            }
            _deckPillars.Clear();

            RefreshPool();
            RefreshDeckPanel();
            ShowBuildPage();
        }

        public void LoadDeckForEditing(DeckData deck)
        {
            if (deck == null) return;

            ClearDeck();
            _editingDeckId = null;
            _deckElement = deck.element;
            if (deckNameInput) deckNameInput.text = deck.deckName;

            foreach (var entry in deck.cards ?? System.Array.Empty<DeckEntry>())
            {
                if (entry?.card == null) continue;
                for (int i = 0; i < Mathf.Max(1, entry.count); i++)
                    _deckCards.Add(entry.card);
            }

            _deckPillars.Clear();

            RefreshPool();
            RefreshDeckPanel();
            ShowBuildPage();
        }

        private void ClearDeck()
        {
            _deckCards.Clear();
            _deckPillars.Clear();
            _editingDeckId = null;
            _highlightedCardId = null;
            RefreshDeckPanel();
        }

        private void AutoBuildDeck()
        {
            ClearDeck();

            var allCards = cardDatabase.GetAllCards()
                .Where(c => c != null && !IsRetiredPillarCard(c))
                .ToList();

            AddAutoBuildCards(
                allCards.OfType<DaemonCardData>().Where(d => d.element == _deckElement).Cast<CardData>(),
                allCards.OfType<DaemonCardData>().Cast<CardData>(),
                GameConstants.DeckDaemonCount);
            AddAutoBuildCards(
                allCards.OfType<AsheCardData>().Where(IsAshePreferredForDeck).Cast<CardData>(),
                allCards.OfType<AsheCardData>().Cast<CardData>(),
                GameConstants.DeckAsheCount);
            AddAutoBuildCards(
                allCards.OfType<MaskCardData>().Cast<CardData>(),
                allCards.Where(c => c.category == CardCategory.Relic),
                GameConstants.DeckRelicCount);
            AddAutoBuildCards(
                allCards.OfType<DomainCardData>().Where(d => d.effectElement == _deckElement).Cast<CardData>(),
                allCards.OfType<DomainCardData>().Cast<CardData>(),
                GameConstants.DeckDomainCount);
            AddAutoBuildCards(
                allCards.OfType<HexCardData>().Cast<CardData>(),
                allCards.Where(c => c.category == CardCategory.Hex),
                GameConstants.DeckHexCount);
            AddAutoBuildCards(
                allCards.OfType<DispelCardData>().Cast<CardData>(),
                allCards.Where(c => c.category == CardCategory.Dispel),
                GameConstants.DeckDispelCount);

            if (_deckCards.Count < GameConstants.DeckSize)
                AddAutoBuildCards(allCards, allCards, GameConstants.DeckSize - _deckCards.Count);

            RefreshDeckPanel();
            ShowDeckBuilderHint("Auto Script wrote a balanced 40-card tome.");
            if (!string.IsNullOrEmpty(_highlightedCardId))
                HighlightBestSynergyCard();
        }

        private void AddAutoBuildCards(IEnumerable<CardData> preferred, IEnumerable<CardData> fallback, int requestedCount)
        {
            int added = 0;
            foreach (var card in RankAutoBuildCards(preferred).Concat(RankAutoBuildCards(fallback)))
            {
                if (card == null || IsRetiredPillarCard(card)) continue;
                while (added < requestedCount
                    && _deckCards.Count < GameConstants.DeckSize
                    && _deckCards.Count(c => c != null && c.cardId == card.cardId) < GameConstants.MaxCardCopies)
                {
                    _deckCards.Add(card);
                    added++;
                }

                if (added >= requestedCount || _deckCards.Count >= GameConstants.DeckSize)
                    break;
            }
        }

        private IEnumerable<CardData> RankAutoBuildCards(IEnumerable<CardData> cards)
        {
            return cards
                .Where(c => c != null)
                .GroupBy(c => c.cardId)
                .Select(g => g.First())
                .OrderByDescending(c => GetSynergyScore(c, out _))
                .ThenBy(c => c.GetWillCost())
                .ThenBy(c => c.cardName);
        }

        private bool IsAshePreferredForDeck(AsheCardData ashe)
        {
            if (ashe == null) return false;
            if (ashe.matchType == AsheMatchType.Element)
                return ashe.targetElement == _deckElement;
            if (ashe.matchType == AsheMatchType.CreatureType)
                return _deckCards.OfType<DaemonCardData>().Any(ashe.Matches);
            return true;
        }

        private void EnsureRuntimeSynergyButton()
        {
            if (synergyButton == null && clearButton != null)
            {
                var syGo = Instantiate(clearButton.gameObject, clearButton.transform.parent);
                syGo.name = "SynergyButton";
                syGo.transform.SetSiblingIndex(clearButton.transform.GetSiblingIndex());
                var txt = syGo.GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null) txt.text = "BEST PICK";
                var layout = syGo.GetComponent<LayoutElement>();
                if (layout != null) layout.preferredWidth = 112;
                synergyButton = syGo.GetComponent<Button>();
                synergyButton.onClick.RemoveAllListeners();
            }

            if (synergyButton != null)
            {
                synergyButton.onClick.RemoveAllListeners();
                synergyButton.onClick.AddListener(HighlightBestSynergyCard);
            }

            if (synergyText == null && cardCountText != null)
            {
                var textGo = Instantiate(cardCountText.gameObject, cardCountText.transform.parent);
                textGo.name = "SynergyText";
                synergyText = textGo.GetComponent<TextMeshProUGUI>();
                synergyText.text = "";
                synergyText.fontSize = 11;
                synergyText.enableAutoSizing = true;
                synergyText.fontSizeMin = 8;
                synergyText.fontSizeMax = 11;

                var layout = textGo.GetComponent<LayoutElement>() ?? textGo.AddComponent<LayoutElement>();
                layout.preferredWidth = 150;
                synergyText.color = _inkColor;
            }
        }

        private void EnsureFullCardClickTarget(Transform parent, UnityEngine.Events.UnityAction action)
        {
            if (parent == null || action == null) return;

            Transform existing = parent.Find("FullCardClickTarget");
            var hit = existing != null
                ? existing.gameObject
                : new GameObject("FullCardClickTarget", typeof(RectTransform), typeof(Image), typeof(Button));
            hit.transform.SetParent(parent, false);
            hit.transform.SetAsLastSibling();

            var rt = hit.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = hit.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.01f);
            image.raycastTarget = true;

            var button = hit.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private static void ConfigureCardThumbnailRoot(GameObject go, Vector2 size)
        {
            if (go == null) return;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.sizeDelta = size;
                rt.pivot = new Vector2(0.5f, 0.5f);
            }

            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
            layout.minWidth = size.x;
            layout.minHeight = size.y;
        }

        private void EnsureVisibleCardClickForwarders(Transform root, UnityEngine.Events.UnityAction action)
        {
            if (root == null || action == null) return;

            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null) continue;
                graphic.raycastTarget = true;
                var forwarder = graphic.GetComponent<CardClickForwarder>() ?? graphic.gameObject.AddComponent<CardClickForwarder>();
                forwarder.Action = action;
            }
        }

        private sealed class CardClickForwarder : MonoBehaviour, IPointerClickHandler
        {
            public UnityEngine.Events.UnityAction Action;

            public void OnPointerClick(PointerEventData eventData)
            {
                Action?.Invoke();
            }
        }

        private void DisableChildRaycasts(Transform root)
        {
            if (root == null) return;

            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic != null && graphic.transform != root)
                    graphic.raycastTarget = false;
            }

            var rootGraphic = root.GetComponent<Graphic>();
            if (rootGraphic != null)
                rootGraphic.raycastTarget = true;
        }

        private void ShowDeckBuilderHint(string message)
        {
            if (synergyText == null || string.IsNullOrWhiteSpace(message)) return;

            synergyText.text = message;
            synergyText.color = new Color(1f, 0.86f, 0.34f, 1f);
        }

        private void ApplyGrimoireSkin()
        {
            SetText("Header", "GRIMOIRE");
            SetText("PoolLabel", "CARD LIBRARY");
            SetText("SaveButton", "BIND TOME");
            SetText("ClearButton", "ERASE");
            SetText("AutoBuildButton", "AUTO SCRIPT");
            SetText("SynergyButton", "BEST PICK");
            SetText("BuildPageButton", "INSCRIBE");
            SetText("DecksPageButton", "TOMES");
            SetText("BackButton", "< BACK");

            TintButton(saveButton, new Color(0.66f, 0.42f, 0.13f, 0.95f), Color.white);
            TintButton(clearButton, new Color(0.42f, 0.16f, 0.12f, 0.92f), Color.white);
            TintButton(autoBuildButton, new Color(0.30f, 0.21f, 0.12f, 0.92f), Color.white);
            TintButton(synergyButton, new Color(0.52f, 0.34f, 0.10f, 0.94f), Color.white);

            var bgSprite = Resources.Load<Sprite>("UI/deckbuilder-bg");
            var pageSprite = Resources.Load<Sprite>("UI/grimoire-page");

            SetImageSprite("Background", bgSprite, new Color(0.07f, 0.045f, 0.03f, 1f), Image.Type.Simple, false);
            SetImageSprite("CardPoolPanel", pageSprite, _pageColor, Image.Type.Sliced, false);
            SetImageSprite("DeckPanel", pageSprite, _pageColor, Image.Type.Sliced, false);
            SetImageColor("PoolFilterRow", new Color(0.48f, 0.30f, 0.12f, 0.42f));
            SetImageColor("DeckConfigRow", new Color(0.48f, 0.30f, 0.12f, 0.42f));
            SetImageColor("BottomRow", new Color(0.48f, 0.30f, 0.12f, 0.50f));

            SetRect("HeaderBar", new Vector2(0f, 0.91f), Vector2.one, Vector2.zero, Vector2.zero);
            SetImageColor("HeaderBar", new Color(0.05f, 0.025f, 0.02f, 0.80f));
            SetRect("CardPoolPanel", new Vector2(0.055f, 0.085f), new Vector2(0.505f, 0.895f), Vector2.zero, Vector2.zero);
            SetRect("DeckPanel", new Vector2(0.515f, 0.085f), new Vector2(0.955f, 0.895f), Vector2.zero, Vector2.zero);

            if (deckNameInput != null)
            {
                deckNameInput.text = string.IsNullOrWhiteSpace(deckNameInput.text) ? "New Grimoire" : deckNameInput.text;
                var image = deckNameInput.GetComponent<Image>();
                if (image != null) image.color = new Color(0.96f, 0.82f, 0.55f, 0.72f);
                if (deckNameInput.textComponent != null)
                    deckNameInput.textComponent.color = _inkColor;
            }

            RecolorText("Header", _goldInkColor);
            RecolorText("PoolLabel", _inkColor);
            if (cardCountText != null) cardCountText.color = _inkColor;
            if (pillarCountText != null)
            {
                pillarCountText.text = "";
                pillarCountText.gameObject.SetActive(false);
            }

            var poolGrid = cardPoolContainer != null ? cardPoolContainer.GetComponent<GridLayoutGroup>() : null;
            if (poolGrid != null)
            {
                poolGrid.cellSize = new Vector2(164f, 230f);
                poolGrid.spacing = new Vector2(10f, 10f);
                poolGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                poolGrid.constraintCount = 3;
                poolGrid.padding = new RectOffset(12, 12, 48, 12);
            }

            var deckLayout = deckListContainer != null ? deckListContainer.GetComponent<VerticalLayoutGroup>() : null;
            if (deckLayout != null)
            {
                deckLayout.spacing = 4;
                deckLayout.padding = new RectOffset(10, 10, 8, 8);
            }
            if (deckListContainer != null)
                deckListContainer.gameObject.SetActive(false);

            EnsureDeckSummaryOverlay();
            UpdateDeckSummaryOverlay();
        }

        private void EnsureDeckSummaryOverlay()
        {
            if (_deckSummaryText != null || deckListContainer == null || deckListContainer.parent == null)
                return;

            var go = new GameObject("TomeContentsText", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(deckListContainer.parent, false);
            go.transform.SetAsLastSibling();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.82f);
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(28f, 6f);
            rt.offsetMax = new Vector2(-28f, -10f);

            _deckSummaryText = go.GetComponent<TextMeshProUGUI>();
            _deckSummaryText.color = new Color(1f, 0.88f, 0.56f, 0.98f);
            _deckSummaryText.fontSize = 14;
            _deckSummaryText.fontStyle = FontStyles.Bold;
            _deckSummaryText.alignment = TextAlignmentOptions.TopLeft;
            _deckSummaryText.enableWordWrapping = true;
            _deckSummaryText.outlineColor = new Color(0.02f, 0.01f, 0f, 0.95f);
            _deckSummaryText.outlineWidth = 0.16f;
            _deckSummaryText.raycastTarget = false;
        }

        private void EnsureSelectedCardPreviewRoot()
        {
            if (_selectedCardPreviewRoot != null || deckListContainer == null || deckListContainer.parent == null)
                return;

            var viewport = new GameObject("SelectedCardPocketViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(deckListContainer.parent, false);
            viewport.transform.SetAsLastSibling();

            var viewportRt = viewport.GetComponent<RectTransform>();
            viewportRt.anchorMin = new Vector2(0.06f, 0.12f);
            viewportRt.anchorMax = new Vector2(0.94f, 0.80f);
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;

            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(0.04f, 0.025f, 0.015f, 0.20f);
            viewportImage.raycastTarget = true;

            var content = new GameObject("SelectedCardPocketGrid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
            content.transform.SetParent(viewport.transform, false);

            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.offsetMin = new Vector2(0f, contentRt.offsetMin.y);
            contentRt.offsetMax = new Vector2(0f, contentRt.offsetMax.y);

            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(86f, 122f);
            grid.spacing = new Vector2(10f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.padding = new RectOffset(10, 10, 10, 10);

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var canvasGroup = content.GetComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;

            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = viewportRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 18f;

            _selectedCardPreviewRoot = content.transform;
        }

        private void UpdateDeckSummaryOverlay()
        {
            EnsureDeckSummaryOverlay();
            if (_deckSummaryText == null) return;

            if (_deckCards.Count == 0)
            {
                _deckSummaryText.text =
                    "Tome Contents\n" +
                    $"Need: {GameConstants.DeckAsheCount} Source  |  {GameConstants.DeckDaemonCount}+ Daemon  |  40 cards";
                _deckSummaryText.color = new Color(1f, 0.88f, 0.56f, 0.88f);
                return;
            }

            var categoryCounts = _deckCards
                .Where(c => c != null)
                .GroupBy(c => c.category)
                .OrderBy(g => g.Key)
                .Select(g => $"{GetCategoryLabel(g.Key)}: {g.Count()}");

            int sourceCount = _deckCards.OfType<AsheCardData>().Count();
            int daemonCount = _deckCards.OfType<DaemonCardData>().Count();
            string ruleLine = $"Need: Source {sourceCount}/{GameConstants.DeckAsheCount}  |  Daemon {daemonCount}/{GameConstants.DeckDaemonCount}+";
            string status = IsDeckReadyToSave(out string error) ? "Ready to bind." : error;

            _deckSummaryText.text =
                $"Tome Contents  {_deckCards.Count}/{GameConstants.DeckSize}\n" +
                $"{string.Join("  |  ", categoryCounts)}\n" +
                $"{ruleLine}\n" +
                $"{status}";
            _deckSummaryText.color = IsDeckReadyToSave(out _)
                ? new Color(0.62f, 1f, 0.58f, 0.98f)
                : new Color(1f, 0.88f, 0.56f, 0.98f);
        }

        private void UpdateSelectedCardPreviews()
        {
            EnsureSelectedCardPreviewRoot();
            if (_selectedCardPreviewRoot == null) return;

            ClearChildren(_selectedCardPreviewRoot);

            var sortedCards = _deckCards
                .Where(c => c != null)
                .OrderBy(c => c.category)
                .ThenBy(c => c.GetWillCost())
                .ThenBy(c => c.cardName)
                .ToList();

            for (int i = 0; i < GameConstants.DeckSize; i++)
            {
                var card = i < sortedCards.Count ? sortedCards[i] : null;
                var slot = CreateTomeCardSlot(i, card);
                if (card == null)
                    continue;

                var go = cardThumbnailPrefab != null
                    ? Instantiate(cardThumbnailPrefab, slot.transform)
                    : CreateFallbackTomeCard(card, slot.transform);
                ConfigureCardThumbnailRoot(go, new Vector2(168f, 236f));
                go.transform.localScale = cardThumbnailPrefab != null ? Vector3.one * 0.455f : Vector3.one;

                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                }

                var visual = go.GetComponent<CardVisual>();
                if (visual != null)
                    visual.SetCard(card);

                foreach (var graphic in go.GetComponentsInChildren<Graphic>(true))
                    if (graphic != null) graphic.raycastTarget = false;

                var button = slot.GetComponent<Button>() ?? slot.AddComponent<Button>();
                var captured = card;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => RemoveOneCardFromDeck(captured, false));
            }
        }

        private GameObject CreateTomeCardSlot(int index, CardData card)
        {
            var slot = new GameObject($"TomeSlot_{index + 1:00}", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(Button), typeof(LayoutElement), typeof(Outline));
            slot.transform.SetParent(_selectedCardPreviewRoot, false);

            var layout = slot.GetComponent<LayoutElement>();
            layout.preferredWidth = 86f;
            layout.preferredHeight = 122f;
            layout.minWidth = 86f;
            layout.minHeight = 122f;

            var image = slot.GetComponent<Image>();
            image.color = card == null
                ? new Color(0.17f, 0.10f, 0.04f, 0.34f)
                : new Color(0.05f, 0.035f, 0.025f, 0.82f);
            image.raycastTarget = card != null;

            var outline = slot.GetComponent<Outline>();
            outline.effectColor = card == null
                ? new Color(0.93f, 0.68f, 0.32f, 0.24f)
                : new Color(1f, 0.78f, 0.34f, 0.62f);
            outline.effectDistance = new Vector2(2f, -2f);

            if (card == null)
                AddSlotNumber(slot.transform, index + 1);

            return slot;
        }

        private GameObject CreateFallbackTomeCard(CardData card, Transform parent)
        {
            var go = new GameObject("FallbackTomeCard", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 4f);
            rt.offsetMax = new Vector2(-4f, -4f);

            var image = go.GetComponent<Image>();
            image.color = GetFallbackCardColor(card);
            image.raycastTarget = false;

            var art = new GameObject("Art", typeof(RectTransform), typeof(Image));
            art.transform.SetParent(go.transform, false);
            var artRt = art.GetComponent<RectTransform>();
            artRt.anchorMin = new Vector2(0.08f, 0.33f);
            artRt.anchorMax = new Vector2(0.92f, 0.94f);
            artRt.offsetMin = Vector2.zero;
            artRt.offsetMax = Vector2.zero;
            var artImage = art.GetComponent<Image>();
            artImage.sprite = card != null ? card.artwork : null;
            artImage.preserveAspect = true;
            artImage.color = artImage.sprite != null ? Color.white : new Color(0.9f, 0.7f, 0.38f, 0.35f);
            artImage.raycastTarget = false;

            var title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            title.transform.SetParent(go.transform, false);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.07f, 0.07f);
            titleRt.anchorMax = new Vector2(0.93f, 0.31f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;
            var text = title.GetComponent<TextMeshProUGUI>();
            text.text = card != null ? card.cardName : "";
            text.fontSize = 9f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 5f;
            text.fontSizeMax = 9f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Top;
            text.color = new Color(1f, 0.88f, 0.62f, 1f);
            text.raycastTarget = false;

            return go;
        }

        private static Color GetFallbackCardColor(CardData card)
        {
            return card?.category switch
            {
                CardCategory.Daemon => new Color(0.09f, 0.055f, 0.045f, 0.98f),
                CardCategory.AsheCard => new Color(0.045f, 0.12f, 0.06f, 0.98f),
                CardCategory.Relic => new Color(0.13f, 0.10f, 0.045f, 0.98f),
                CardCategory.Domain => new Color(0.055f, 0.09f, 0.13f, 0.98f),
                CardCategory.Hex => new Color(0.14f, 0.045f, 0.10f, 0.98f),
                CardCategory.Dispel => new Color(0.12f, 0.12f, 0.045f, 0.98f),
                _ => new Color(0.08f, 0.055f, 0.04f, 0.98f),
            };
        }

        private void AddSlotNumber(Transform parent, int index)
        {
            var labelGo = new GameObject("SlotNumber", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(parent, false);

            var rt = labelGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = labelGo.GetComponent<TextMeshProUGUI>();
            text.text = index.ToString();
            text.fontSize = 18f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.78f, 0.54f, 0.25f, 0.48f);
            text.raycastTarget = false;
        }

        private void AddCountBadge(Transform parent, int count)
        {
            if (parent == null || count <= 1) return;

            var badge = new GameObject("CountBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);
            var rt = badge.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-3f, -3f);
            rt.sizeDelta = new Vector2(24f, 18f);

            var image = badge.GetComponent<Image>();
            image.color = new Color(0.04f, 0.02f, 0.01f, 0.88f);
            image.raycastTarget = false;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(badge.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.text = $"x{count}";
            text.fontSize = 11;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.86f, 0.32f, 1f);
            text.raycastTarget = false;
        }

        private static string GetCategoryLabel(CardCategory category)
        {
            return category switch
            {
                CardCategory.Relic => "Relic",
                CardCategory.AsheCard => "Source",
                CardCategory.Dispel => "Dispel",
                CardCategory.Domain => "Domain",
                CardCategory.Hex => "Hex",
                CardCategory.Daemon => "Daemon",
                _ => category.ToString(),
            };
        }

        private void UpdatePoolSelectedBadges()
        {
            foreach (var pair in _poolCardObjects)
                ApplyPoolSelectedBadge(pair.Value, cardDatabase.GetCard(pair.Key));
        }

        private void ApplyPoolSelectedBadge(GameObject go, CardData card)
        {
            if (go == null || card == null) return;

            int count = _deckCards.Count(c => c != null && c.cardId == card.cardId);
            var badge = go.transform.Find("SelectedCountBadge")?.gameObject;
            if (count <= 0)
            {
                if (badge != null) Destroy(badge);
                return;
            }

            if (badge == null)
            {
                badge = new GameObject("SelectedCountBadge", typeof(RectTransform), typeof(Image));
                badge.transform.SetParent(go.transform, false);
                badge.transform.SetAsLastSibling();

                var rt = badge.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(1f, 0f);
                rt.anchoredPosition = new Vector2(-8f, 8f);
                rt.sizeDelta = new Vector2(48f, 24f);

                var image = badge.GetComponent<Image>();
                image.color = new Color(0.06f, 0.03f, 0.01f, 0.94f);
                image.raycastTarget = false;

                var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(badge.transform, false);
                var textRt = textGo.GetComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = Vector2.zero;
                textRt.offsetMax = Vector2.zero;

                var label = textGo.GetComponent<TextMeshProUGUI>();
                label.fontSize = 12f;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.color = new Color(1f, 0.84f, 0.32f, 1f);
                label.raycastTarget = false;
            }

            var tmp = badge.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
                tmp.text = $"IN x{count}";
        }

        private static void SetRect(string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = GameObject.Find(objectName);
            var rt = go != null ? go.GetComponent<RectTransform>() : null;
            if (rt == null) return;

            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private static void SetText(string objectName, string text)
        {
            var go = GameObject.Find(objectName);
            var tmp = go != null ? go.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (tmp != null) tmp.text = text;
        }

        private static void RecolorText(string objectName, Color color)
        {
            var go = GameObject.Find(objectName);
            var tmp = go != null ? go.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (tmp != null) tmp.color = color;
        }

        private static void SetImageColor(string objectName, Color color)
        {
            var go = GameObject.Find(objectName);
            var image = go != null ? go.GetComponent<Image>() : null;
            if (image != null)
            {
                image.color = color;
                image.raycastTarget = false;
            }
        }

        private static void SetImageSprite(string objectName, Sprite sprite, Color color, Image.Type imageType, bool preserveAspect)
        {
            var go = GameObject.Find(objectName);
            var image = go != null ? go.GetComponent<Image>() : null;
            if (image == null) return;

            if (sprite != null)
                image.sprite = sprite;
            image.color = color;
            image.type = image.sprite != null ? imageType : Image.Type.Simple;
            image.preserveAspect = preserveAspect;
            image.raycastTarget = false;
        }

        private static void TintButton(Button button, Color imageColor, Color textColor)
        {
            if (button == null) return;

            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = imageColor;

            var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
                text.color = textColor;
        }

        private void EnsureRuntimeDeckBrowser()
        {
            if (cardPoolContainer == null) return;

            var scrollRect = cardPoolContainer.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
                _poolScrollPage = scrollRect.gameObject;

            Transform poolPanel = _poolScrollPage != null ? _poolScrollPage.transform.parent : cardPoolContainer.parent;
            if (poolPanel == null) return;

            if (deckBrowserPage == null)
            {
                deckBrowserPage = new GameObject("DeckBrowserPage", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
                deckBrowserPage.transform.SetParent(poolPanel, false);
                var rt = deckBrowserPage.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = new Vector2(1f, 0.93f);
                rt.offsetMin = new Vector2(4f, 4f);
                rt.offsetMax = new Vector2(-4f, 0f);

                var img = deckBrowserPage.GetComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);

                var scroll = deckBrowserPage.GetComponent<ScrollRect>();
                scroll.vertical = true;
                scroll.horizontal = false;

                var content = new GameObject("SavedDeckListContainer", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                content.transform.SetParent(deckBrowserPage.transform, false);
                var contentRt = content.GetComponent<RectTransform>();
                contentRt.anchorMin = new Vector2(0f, 1f);
                contentRt.anchorMax = Vector2.one;
                contentRt.pivot = new Vector2(0.5f, 1f);

                var layout = content.GetComponent<VerticalLayoutGroup>();
                layout.spacing = 6;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.padding = new RectOffset(8, 8, 8, 8);
                content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scroll.content = contentRt;
                savedDeckListContainer = content.transform;
            }

            if ((buildPageButton == null || decksPageButton == null) && elementFilterDropdown != null)
            {
                Transform row = elementFilterDropdown.transform.parent;
                buildPageButton ??= CreateRuntimeButton(row, "BuildPageButton", "INSCRIBE", 92);
                decksPageButton ??= CreateRuntimeButton(row, "DecksPageButton", "TOMES", 80);
            }

            if (buildPageButton != null)
            {
                buildPageButton.onClick.RemoveAllListeners();
                buildPageButton.onClick.AddListener(() => ShowBuildPage());
            }
            if (decksPageButton != null)
            {
                decksPageButton.onClick.RemoveAllListeners();
                decksPageButton.onClick.AddListener(() => ShowDeckBrowserPage());
            }
            ShowBuildPage();
        }

        private Button CreateRuntimeButton(Transform parent, string name, string label, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.44f, 0.27f, 0.10f, 0.88f);

            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 30;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return go.GetComponent<Button>();
        }

        private void ShowBuildPage()
        {
            if (_poolScrollPage != null) _poolScrollPage.SetActive(true);
            if (deckBrowserPage != null) deckBrowserPage.SetActive(false);
            if (buildPageButton != null) buildPageButton.interactable = false;
            if (decksPageButton != null) decksPageButton.interactable = true;
        }

        private void ShowDeckBrowserPage()
        {
            PopulateDeckBrowser();
            if (_poolScrollPage != null) _poolScrollPage.SetActive(false);
            if (deckBrowserPage != null) deckBrowserPage.SetActive(true);
            if (buildPageButton != null) buildPageButton.interactable = true;
            if (decksPageButton != null) decksPageButton.interactable = false;
        }

        private void PopulateDeckBrowser()
        {
            if (savedDeckListContainer == null || cardDatabase == null) return;

            ClearChildren(savedDeckListContainer);
            bool hasAny = false;

            var profile = ProfileManager.Load();
            PopulateInvokerDesignRows(profile);
            hasAny = true;

            if (profile.customDecks != null)
            {
                foreach (var deck in profile.customDecks.OrderBy(d => d.name))
                {
                    hasAny = true;
                    AddDeckBrowserRow(
                        $"CUSTOM  {deck.name}  {deck.element}  {deck.cardIds?.Count ?? 0}/{GameConstants.DeckSize}",
                        () => LoadDeckForEditing(deck));
                }
            }

            foreach (var deck in Resources.LoadAll<DeckData>("CardData/Decks").OrderBy(d => d.element).ThenBy(d => d.deckName))
            {
                hasAny = true;
                AddDeckBrowserRow(
                    $"PREBUILT  {deck.deckName}  {deck.element}  {deck.TotalMainCards}/{GameConstants.DeckSize}",
                    () => LoadDeckForEditing(deck));
            }

            if (!hasAny)
                AddDeckBrowserRow("No grimware decks saved yet.", null);
        }

        private void PopulateInvokerDesignRows(PlayerProfile profile)
        {
            AddDeckBrowserRow("INVOKER DESIGNS  choose your story character", null);
            foreach (var design in GetInvokerDesigns())
            {
                bool active = string.Equals(profile.activeInvokerDesign, design.Id, System.StringComparison.OrdinalIgnoreCase);
                string prefix = active ? "ACTIVE" : "SELECT";
                AddDeckBrowserRow($"{prefix}  {design.Name}  {design.Description}", () =>
                {
                    var currentProfile = ProfileManager.Load();
                    currentProfile.activeInvokerDesign = design.Id;
                    ProfileManager.Save(currentProfile);
                    PopulateDeckBrowser();
                    ShowDeckBuilderHint($"{design.Name} will appear as your story invoker.");
                }, $"UI/invoker-design-{design.Id}");
            }
            AddDeckBrowserRow(" ", null);
        }

        private static IEnumerable<(string Id, string Name, string Description)> GetInvokerDesigns()
        {
            yield return ("arcane", "Arcane Novice", "balanced gold-violet academy look");
            yield return ("verdant", "Verdant Binder", "nature-green robe and spirit-vine accent");
            yield return ("storm", "Storm Scribe", "blue-white coat with lightning SE marks");
            yield return ("umbral", "Umbral Caller", "dark cloak with violet source glow");
        }

        private void AddDeckBrowserRow(string label, UnityEngine.Events.UnityAction onClick, string iconResource = null)
        {
            var row = new GameObject("DeckRow", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(savedDeckListContainer, false);

            var img = row.GetComponent<Image>();
            img.color = new Color(0.78f, 0.58f, 0.32f, 0.58f);

            var layout = row.GetComponent<LayoutElement>();
            layout.preferredHeight = 42;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(row.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = !string.IsNullOrWhiteSpace(iconResource) ? new Vector2(58, 4) : new Vector2(12, 4);
            trt.offsetMax = new Vector2(-12, -4);

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 13;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 9;
            tmp.fontSizeMax = 13;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = _inkColor;

            var button = row.GetComponent<Button>();
            button.interactable = onClick != null;
            if (onClick != null) button.onClick.AddListener(onClick);

            if (!string.IsNullOrWhiteSpace(iconResource))
            {
                var iconGo = new GameObject("InvokerDesignIcon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(row.transform, false);
                var irt = iconGo.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.anchoredPosition = new Vector2(10f, 0f);
                irt.sizeDelta = new Vector2(38f, 38f);

                var icon = iconGo.GetComponent<Image>();
                icon.sprite = Resources.Load<Sprite>(iconResource);
                icon.color = icon.sprite != null ? Color.white : new Color(0.36f, 0.22f, 0.10f, 0.72f);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }
        }

        private void HighlightBestSynergyCard()
        {
            var best = cardDatabase.GetAllCards()
                .Where(c => c != null)
                .Where(c =>
                {
                    if (c is DaemonCardData d) return d.element == _poolFilter;
                    if (IsRetiredPillarCard(c)) return false;
                    return true;
                })
                .Where(CanAddMoreCopies)
                .Select(c => new { Card = c, Score = GetSynergyScore(c, out string reason), Reason = reason })
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Card.GetWillCost())
                .ThenBy(c => c.Card.cardName)
                .FirstOrDefault();

            _highlightedCardId = best?.Card.cardId;
            RefreshVisibleHighlights();

            if (synergyText != null)
            {
                synergyText.text = best == null || best.Score <= 0
                    ? "No synergy pick"
                    : $"{best.Card.cardName}: {best.Reason}";
                synergyText.color = best != null && best.Score > 0
                    ? new Color(0.98f, 0.82f, 0.17f)
                    : Color.white;
            }
        }

        private bool CanAddMoreCopies(CardData card)
        {
            if (card == null) return false;
            if (IsRetiredPillarCard(card)) return false;
            if (card is PillarCardData pillar)
                return pillar.element == _deckElement && _deckPillars.Count < GameConstants.PillarCount;

            if (_deckCards.Count >= GameConstants.DeckSize) return false;
            return _deckCards.Count(c => c != null && c.cardId == card.cardId) < GameConstants.MaxCardCopies;
        }

        private void RefreshVisibleHighlights()
        {
            foreach (var pair in _poolCardObjects)
                ApplySynergyHighlight(pair.Value, cardDatabase.GetCard(pair.Key));
        }

        private void ApplySynergyHighlight(GameObject go, CardData card)
        {
            if (go == null || card == null) return;

            bool active = !string.IsNullOrEmpty(_highlightedCardId) && card.cardId == _highlightedCardId;
            var outline = go.GetComponent<Outline>();
            if (active && outline == null)
                outline = go.AddComponent<Outline>();

            if (outline != null)
            {
                outline.enabled = active;
                outline.effectColor = new Color(1f, 0.78f, 0.16f, 1f);
                outline.effectDistance = new Vector2(6f, 6f);
            }

            var badge = go.transform.Find("SynergyBadge")?.gameObject;
            if (active && badge == null)
            {
                badge = new GameObject("SynergyBadge", typeof(RectTransform), typeof(Image));
                badge.transform.SetParent(go.transform, false);

                var brt = badge.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0f, 1f);
                brt.anchorMax = new Vector2(1f, 1f);
                brt.pivot = new Vector2(0.5f, 1f);
                brt.anchoredPosition = new Vector2(0f, -6f);
                brt.sizeDelta = new Vector2(-16f, 20f);

                var img = badge.GetComponent<Image>();
                img.color = new Color(1f, 0.72f, 0.10f, 0.92f);
                img.raycastTarget = false;

                var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(badge.transform, false);
                var trt = textGo.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;

                var tmp = textGo.GetComponent<TextMeshProUGUI>();
                tmp.text = "BEST PICK";
                tmp.fontSize = 10;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.black;
                tmp.raycastTarget = false;
            }

            if (badge != null)
                badge.SetActive(active);
        }

        private int GetSynergyScore(CardData candidate, out string reason)
        {
            var deckContext = _deckCards.Where(c => c != null).ToList();
            int score = candidate.GetWillCost() <= 2 ? 2 : 0;
            var reasons = new List<string>();

            Element? element = GetCardElement(candidate);
            if (element.HasValue && element.Value == _deckElement)
            {
                score += 12;
                reasons.Add($"{_deckElement} plan");
            }

            CreatureType? type = GetCreatureType(candidate);
            if (type.HasValue)
            {
                int matchingType = deckContext.Count(c => GetCreatureType(c) == type);
                if (matchingType > 0)
                {
                    score += matchingType * 3;
                    reasons.Add($"{type.Value} density");
                }
            }

            foreach (Keyword keyword in candidate.keywords ?? System.Array.Empty<Keyword>())
            {
                if (keyword == Keyword.None) continue;
                int matches = deckContext.Count(c => (c.keywords ?? System.Array.Empty<Keyword>()).Contains(keyword));
                if (matches <= 0) continue;
                score += matches * 2;
                reasons.Add($"{keyword} chain");
            }

            if (candidate is AsheCardData ashe)
            {
                int compatible = _deckCards.OfType<DaemonCardData>().Count(ashe.Matches);
                if (compatible > 0)
                {
                    score += compatible * 5;
                    reasons.Add("Life support");
                }
            }

            string candidateTags = BuildTextTags(candidate);
            foreach (var tag in new[]
            {
                "shield", "heal", "draw", "damage", "silence", "resonance", "ward", "echo", "resurrect",
                "domain", "will", "relic", "seal", "ashe", "life", "freeze", "burn", "poison", "drain",
                "control", "pressure", "protect", "buff", "combo", "tempo"
            })
            {
                if (candidateTags.IndexOf(tag, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                int tagMatches = deckContext.Count(c => BuildTextTags(c).IndexOf(tag, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (tagMatches <= 0) continue;
                score += tagMatches;
                reasons.Add(tag);
            }

            score += Mathf.Max(0, (int)candidate.rarity - 1);
            reason = reasons.Count == 0 ? "best curve fit" : string.Join(", ", reasons.Distinct().Take(3));
            return score;
        }

        private static Element? GetCardElement(CardData card)
        {
            return card switch
            {
                DaemonCardData d => d.element,
                InvokerCardData i => i.element,
                AsheCardData a when a.matchType == AsheMatchType.Element => a.targetElement,
                _ => null,
            };
        }

        private static CreatureType? GetCreatureType(CardData card)
        {
            return card switch
            {
                DaemonCardData d => d.creatureType,
                AsheCardData a when a.matchType == AsheMatchType.CreatureType => a.targetCreatureType,
                _ => null,
            };
        }

        private static bool IsRetiredPillarCard(CardData card)
        {
            return card is PillarCardData || card?.category == CardCategory.Pillar;
        }

        private static string BuildTextTags(CardData card)
        {
            if (card == null) return string.Empty;
            return $"{card.cardName} {card.description} {card.flavorText}".ToLowerInvariant();
        }

        private void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }
    }
}
