// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Battle Scene Controller
//  MonoBehaviour that drives the battle UI and connects
//  to BattleManager game engine
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace DualCraft.UI
{
    using Battle;
    using Cards;
    using Core;

    public class BattleSceneController : MonoBehaviour
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

        private BattleManager _battle;
        private int _localPlayerIndex = 0;
        private readonly List<GameObject> _spawnedObjects = new();

        private void Start()
        {
            cardDatabase.Initialize();

            _battle = new BattleManager(cardDatabase);
            _battle.OnStateChanged += RefreshUI;
            _battle.OnLogEntry += AddLogEntry;
            _battle.OnGameOver += HandleGameOver;
            _battle.OnDiceRollRequested += HandleDiceRollRequest;

            nextPhaseButton?.onClick.AddListener(() => _battle.ProcessAction(_localPlayerIndex, new NextPhaseAction()));
            endTurnButton?.onClick.AddListener(() => _battle.ProcessAction(_localPlayerIndex, new EndTurnAction()));

            if (gameOverPanel) gameOverPanel.SetActive(false);

            _battle.InitGame("Player 1", player1Deck, "Player 2", player2Deck);
        }

        // ─── UI Refresh ──────────────────────────────────
        private void RefreshUI(GameState state)
        {
            RefreshPlayerUI(state.Players[0], p1NameText, p1HpText, p1HpBar, p1WillText, p1DeckCountText,
                p1HandContainer, p1FieldContainer, p1PillarContainer, 0);
            RefreshPlayerUI(state.Players[1], p2NameText, p2HpText, p2HpBar, p2WillText, p2DeckCountText,
                p2HandContainer, p2FieldContainer, p2PillarContainer, 1);

            if (phaseText) phaseText.text = state.Phase.ToString().ToUpper();
            if (turnText) turnText.text = $"Turn {state.TurnNumber}";
        }

        private void RefreshPlayerUI(PlayerState player,
            TextMeshProUGUI nameText, TextMeshProUGUI hpText, Slider hpBar,
            TextMeshProUGUI willText, TextMeshProUGUI deckCount,
            Transform handContainer, Transform fieldContainer, Transform pillarContainer,
            int playerIndex)
        {
            if (nameText) nameText.text = player.Name;
            if (hpText) hpText.text = $"{player.Conjuror.Hp}/{player.Conjuror.MaxHp}";
            if (hpBar)
            {
                hpBar.maxValue = player.Conjuror.MaxHp;
                hpBar.value = player.Conjuror.Hp;
            }
            if (willText) willText.text = $"Will: {player.Will}/{player.MaxWill}";
            if (deckCount) deckCount.text = $"Deck: {player.Deck.Count}";

            // Refresh hand
            if (handContainer)
            {
                ClearChildren(handContainer);
                bool isLocal = playerIndex == _localPlayerIndex;
                foreach (var cardInst in player.Hand)
                {
                    var go = Instantiate(cardPrefab, handContainer);
                    _spawnedObjects.Add(go);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                    {
                        if (isLocal)
                            visual.SetCard(cardInst.Card);
                        else
                            visual.SetFaceDown();
                    }
                }
            }

            // Refresh field
            if (fieldContainer)
            {
                ClearChildren(fieldContainer);
                foreach (var daemon in player.Field)
                {
                    var go = Instantiate(daemonFieldPrefab ?? cardPrefab, fieldContainer);
                    _spawnedObjects.Add(go);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                        visual.SetCard(daemon.Card);
                }
            }

            // Refresh pillars
            if (pillarContainer)
            {
                ClearChildren(pillarContainer);
                bool isLocal = playerIndex == _localPlayerIndex;
                foreach (var pillar in player.Pillars)
                {
                    if (pillar.Destroyed) continue;
                    var go = Instantiate(pillarPrefab ?? cardPrefab, pillarContainer);
                    _spawnedObjects.Add(go);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                    {
                        // Local player always sees their own pillars; opponent pillars face-down until revealed
                        if (isLocal || pillar.Revealed)
                            visual.SetCard(pillar.Card);
                        else
                            visual.SetFaceDown();
                    }
                }
            }
        }

        // ─── Log ──────────────────────────────────────────
        private void AddLogEntry(LogEntry entry)
        {
            if (logContainer == null || logEntryPrefab == null) return;
            var go = Instantiate(logEntryPrefab, logContainer);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (text)
            {
                text.text = entry.Message;
                text.color = entry.Type switch
                {
                    LogEntryType.Combat => Color.red,
                    LogEntryType.Effect => Color.cyan,
                    LogEntryType.System => Color.yellow,
                    _ => Color.white,
                };
            }

            if (logScrollRect)
                Canvas.ForceUpdateCanvases();
        }

        // ─── Game Over ───────────────────────────────────
        private void HandleGameOver(int winner, string reason)
        {
            if (gameOverPanel) gameOverPanel.SetActive(true);
            if (gameOverText) gameOverText.text = $"{_battle.State.Players[winner].Name} Wins!\n{reason}";
        }

        // ─── Utilities ───────────────────────────────────
        private void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Called by BattleManager when an attack triggers a dice roll.
        /// Shows the dice animation in the UI.
        /// </summary>
        private void HandleDiceRollRequest(string attackerName, int threshold, System.Action<int, bool> callback)
        {
            if (diceRoller == null) { callback?.Invoke(0, false); return; }
            diceRoller.RollWithThreshold(threshold, $"{attackerName}: HIT!", $"{attackerName}: weak...", callback);
        }

        /// <summary>
        /// Trigger a dice roll with threshold check (e.g., for card abilities).
        /// successMin: minimum roll for success (e.g. 4 means 4-6 succeed, 1-3 fail).
        /// </summary>
        public void RollDice(int successMin, string successText, string failText, System.Action<int, bool> onComplete = null)
        {
            if (diceRoller == null) { onComplete?.Invoke(0, false); return; }
            diceRoller.RollWithThreshold(successMin, successText, failText, onComplete);
        }

        /// <summary>
        /// Simple dice roll without threshold.
        /// </summary>
        public void RollDice(System.Action<int> onComplete = null)
        {
            if (diceRoller == null) { onComplete?.Invoke(0); return; }
            diceRoller.Roll(onComplete);
        }

        public void HideDice()
        {
            if (diceRoller != null) diceRoller.Hide();
        }

        private void OnDestroy()
        {
            if (_battle != null)
            {
                _battle.OnStateChanged -= RefreshUI;
                _battle.OnLogEntry -= AddLogEntry;
                _battle.OnGameOver -= HandleGameOver;
                _battle.OnDiceRollRequested -= HandleDiceRollRequest;
            }
        }
    }
}
