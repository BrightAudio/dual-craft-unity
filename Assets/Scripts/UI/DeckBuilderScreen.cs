// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Deck Builder Screen
// ═══════════════════════════════════════════════════════

using UnityEngine;
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
        [SerializeField] private Button backButton;

        // Deck being built
        private Element _deckElement = Element.Flame;
        private Element _poolFilter = Element.Flame;
        private readonly List<CardData> _deckCards = new();
        private readonly List<PillarCardData> _deckPillars = new();
        private string _editingDeckId; // null = new deck, set when editing existing

        private GameObject CreateDeckEntryPrefab()
        {
            var go = new GameObject("DeckEntry", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 36);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8, 2);
            trt.offsetMax = new Vector2(-8, -2);
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 14;
            tmp.color = Color.white;
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
                return;

            cardDatabase.Initialize();

            // Create deckEntryPrefab at runtime if not assigned in Inspector
            if (deckEntryPrefab == null)
                deckEntryPrefab = CreateDeckEntryPrefab();

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
                    if (txt != null) txt.text = "AUTO";
                    abGo.transform.SetSiblingIndex(clearButton.transform.GetSiblingIndex());
                    autoBuildButton = abGo.GetComponent<Button>();
                    autoBuildButton.onClick.RemoveAllListeners();
                    autoBuildButton.onClick.AddListener(AutoBuildDeck);
                }
            }

            RefreshPool();
            RefreshDeckPanel();
        }

        private void OnFilterChanged(int index)
        {
            _poolFilter = (Element)Mathf.Clamp(index, 0, System.Enum.GetValues(typeof(Element)).Length - 1);
            RefreshPool();
        }

        private void RefreshPool()
        {
            ClearChildren(cardPoolContainer);

            var cards = cardDatabase.GetAllCards()
                .Where(c =>
                {
                    // Show daemons/pillars of the filter element + neutral support cards
                    if (c is DaemonCardData d) return d.element == _poolFilter;
                    if (c is PillarCardData p) return p.element == _poolFilter;
                    return true; // Masks, seals, domains, dispels are element-neutral
                })
                .OrderBy(c => c.GetWillCost())
                .ToList();

            foreach (var card in cards)
            {
                var go = Instantiate(cardThumbnailPrefab, cardPoolContainer);
                var visual = go.GetComponent<CardVisual>();
                if (visual != null) visual.SetCard(card);

                var btn = go.GetComponent<Button>();
                if (btn == null) btn = go.AddComponent<Button>();
                var capturedCard = card;
                btn.onClick.AddListener(() => AddCardToDeck(capturedCard));

                // Show a small badge on cards that count as Capture/Seal cards
                try
                {
                    if (StoryCaptureHelper.IsCaptureCard(card))
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
                catch
                {
                    // No-op if TMPro or other runtime setup isn't available in this context
                }
            }
        }

        private void AddCardToDeck(CardData card)
        {
            if (card is PillarCardData pillar)
            {
                if (_deckPillars.Count >= GameConstants.PillarCount) return;
                if (pillar.element != _deckElement) return;
                _deckPillars.Add(pillar);
            }
            else
            {
                if (_deckCards.Count >= GameConstants.DeckSize) return;
                int copies = _deckCards.Count(c => c.cardId == card.cardId);
                if (copies >= GameConstants.MaxCardCopies) return;
                _deckCards.Add(card);
            }

            RefreshDeckPanel();
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
        }

        private void RefreshDeckPanel()
        {
            // Main card count
            if (cardCountText)
            {
                bool full = _deckCards.Count == GameConstants.DeckSize;
                cardCountText.text = $"Sigils: {_deckCards.Count}/{GameConstants.DeckSize}";
                cardCountText.color = full ? Color.green : Color.white;
            }

            if (pillarCountText)
            {
                bool full = _deckPillars.Count == GameConstants.PillarCount;
                pillarCountText.text = $"Pillars: {_deckPillars.Count}/{GameConstants.PillarCount}";
                pillarCountText.color = full ? Color.green : Color.white;
            }

            // Save button state
            if (saveButton)
                saveButton.interactable = _deckCards.Count == GameConstants.DeckSize && _deckPillars.Count == GameConstants.PillarCount;

            // Refresh pillar list
            ClearChildren(pillarListContainer);
            foreach (var group in _deckPillars
                .Where(p => p != null)
                .GroupBy(p => p.cardId)
                .Select(g => new { Card = g.First(), Count = g.Count() })
                .OrderBy(g => g.Card.element)
                .ThenBy(g => g.Card.cardName))
            {
                var go = Instantiate(deckEntryPrefab, pillarListContainer);
                go.SetActive(true);
                var text = go.GetComponentInChildren<TextMeshProUGUI>();
                if (text) text.text = $"{group.Count}x {group.Card.cardName}";

                var capturedCard = group.Card;
                var btn = go.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(() => RemoveOneCardFromDeck(capturedCard, true));
            }

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
        }

        private void SaveDeck()
        {
            if (_deckCards.Count != GameConstants.DeckSize || _deckPillars.Count != GameConstants.PillarCount) return;

            string deckName = deckNameInput?.text ?? "Custom Grimware";
            if (string.IsNullOrWhiteSpace(deckName)) deckName = "Custom Grimware";

            var profile = ProfileManager.Load();
            var saved = new SavedDeck
            {
                id = _editingDeckId ?? System.Guid.NewGuid().ToString(),
                name = deckName,
                element = _deckElement,
                cardIds = _deckCards.Select(c => c.cardId).ToList(),
                pillarIds = _deckPillars.Select(p => p.cardId).ToList()
            };
            ProfileManager.SaveDeck(profile, saved);
            _editingDeckId = saved.id;

            Debug.Log($"[DeckBuilder] Saved deck '{deckName}' ({saved.id}) with {_deckCards.Count} cards and {_deckPillars.Count} pillars.");

            // Flash save button green briefly
            if (saveButton != null)
                StartCoroutine(FlashButton(saveButton, Color.green, 0.6f));
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

            foreach (var id in deck.cardIds)
            {
                var card = cardDatabase.GetCard(id);
                if (card != null) _deckCards.Add(card);
            }
            foreach (var id in deck.pillarIds)
            {
                var card = cardDatabase.GetCard(id) as PillarCardData;
                if (card != null) _deckPillars.Add(card);
            }

            RefreshPool();
            RefreshDeckPanel();
        }

        private void ClearDeck()
        {
            _deckCards.Clear();
            _deckPillars.Clear();
            RefreshDeckPanel();
        }

        private void AutoBuildDeck()
        {
            ClearDeck();

            var allCards = cardDatabase.GetAllCards();

            // Pick pillars of the selected element
            var pillars = allCards
                .OfType<PillarCardData>()
                .Where(p => p.element == _deckElement)
                .OrderByDescending(p => p.hp)
                .Take(GameConstants.PillarCount)
                .ToList();

            foreach (var p in pillars)
                _deckPillars.Add(p);

            // Pick deck cards: daemons of this element + best spells
            var elementDaemons = allCards
                .OfType<DaemonCardData>()
                .Where(d => d.element == _deckElement)
                .OrderByDescending(d => d.attack + d.ashe)
                .ToList();

            var spells = allCards
                .Where(c => !(c is DaemonCardData) && !(c is PillarCardData))
                .OrderBy(c => c.GetWillCost())
                .ToList();

            // Fill daemons first (up to MaxCardCopies each)
            foreach (var d in elementDaemons)
            {
                for (int i = 0; i < GameConstants.MaxCardCopies && _deckCards.Count < GameConstants.DeckSize; i++)
                    _deckCards.Add(d);
            }

            // Fill remaining with spells
            foreach (var s in spells)
            {
                for (int i = 0; i < GameConstants.MaxCardCopies && _deckCards.Count < GameConstants.DeckSize; i++)
                    _deckCards.Add(s);
            }

            RefreshDeckPanel();
        }

        private void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }
    }
}
