// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Battle Scene Controller
//  MonoBehaviour that drives the battle UI, player
//  interaction, AI opponent, and full game loop.
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

namespace DualCraft.UI
{
    using AI;
    using Battle;
    using Cards;
    using Core;
    using Visual;

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

        // ─── Runtime State ───────────────────────────────
        private BattleManager _battle;
        private AIPlayer _ai;
        private const int LocalPlayer = 0;
        private const int AIPlayerIndex = 1;
        private const float AIActionDelay = 0.45f;

        // Attack targeting state
        private int _selectedAttackerIndex = -1;
        private bool _waitingForTarget;
        private bool _aiTurnRunning;

        // Highlight colors
        private static readonly Color HighlightPlay = new(0.2f, 0.8f, 0.2f, 0.35f);
        private static readonly Color HighlightAttacker = new(1f, 0.85f, 0.2f, 0.4f);
        private static readonly Color HighlightTarget = new(1f, 0.3f, 0.3f, 0.35f);

        // ─── Lifecycle ───────────────────────────────────
        private void Start()
        {
            cardDatabase.Initialize();

            _battle = new BattleManager(cardDatabase);
            _battle.OnStateChanged += OnStateChanged;
            _battle.OnLogEntry += AddLogEntry;
            _battle.OnGameOver += HandleGameOver;
            _battle.OnDiceRollRequested += HandleDiceRollRequest;

            _ai = new AIPlayer(AIPlayerIndex, AIPlayer.Difficulty.Normal);

            nextPhaseButton?.onClick.AddListener(OnNextPhaseClicked);
            endTurnButton?.onClick.AddListener(OnEndTurnClicked);

            if (gameOverPanel) gameOverPanel.SetActive(false);

            _battle.InitGame("Player 1", player1Deck, "Player 2", player2Deck);

            // Kick off the game loop — auto-draw for player 1's first turn
            StartCoroutine(AutoDrawCoroutine());
        }

        // ─── Game Loop Driver ────────────────────────────
        private void OnStateChanged(GameState state)
        {
            RefreshUI(state);
        }

        private IEnumerator AutoDrawCoroutine()
        {
            yield return new WaitForSeconds(0.3f);
            if (_battle.State.GameOver) yield break;

            // Auto-draw for whichever player's turn it is
            if (_battle.State.Phase == GamePhase.Draw)
            {
                _battle.ProcessAction(_battle.State.CurrentPlayer, new DrawCardAction());
            }

            // If it's the AI's turn, run AI
            if (_battle.State.CurrentPlayer == AIPlayerIndex && !_battle.State.GameOver)
            {
                StartCoroutine(RunAITurn());
            }
        }

        private IEnumerator RunAITurn()
        {
            if (_aiTurnRunning) yield break;
            _aiTurnRunning = true;

            while (!_battle.State.GameOver && _battle.State.CurrentPlayer == AIPlayerIndex)
            {
                var actions = _ai.DecideActions(_battle.State);
                if (actions.Count == 0) break;

                foreach (var action in actions)
                {
                    if (_battle.State.GameOver) break;
                    yield return new WaitForSeconds(AIActionDelay);
                    _battle.ProcessAction(AIPlayerIndex, action);
                }

                // If AI ended turn, break and let the draw coroutine handle next
                if (_battle.State.CurrentPlayer == LocalPlayer)
                    break;
            }

            _aiTurnRunning = false;

            // After AI turn ends, auto-draw for player
            if (!_battle.State.GameOver && _battle.State.Phase == GamePhase.Draw)
            {
                StartCoroutine(AutoDrawCoroutine());
            }
        }

        // ─── Player Button Handlers ─────────────────────
        private void OnNextPhaseClicked()
        {
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.GameOver) return;
            ClearSelection();

            _battle.ProcessAction(LocalPlayer, new NextPhaseAction());

            // Auto-advance End phase into EndTurn immediately
            if (_battle.State.Phase == GamePhase.End)
            {
                _battle.ProcessAction(LocalPlayer, new EndTurnAction());
                // AI's turn
                if (!_battle.State.GameOver)
                    StartCoroutine(AutoDrawCoroutine());
            }
        }

        private void OnEndTurnClicked()
        {
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.GameOver) return;
            ClearSelection();

            // Fast-forward through remaining phases
            while (_battle.State.Phase != GamePhase.End && !_battle.State.GameOver)
                _battle.ProcessAction(LocalPlayer, new NextPhaseAction());

            _battle.ProcessAction(LocalPlayer, new EndTurnAction());

            if (!_battle.State.GameOver)
                StartCoroutine(AutoDrawCoroutine());
        }

        // ─── Card Click: Hand ────────────────────────────
        private void OnHandCardClicked(int handIndex)
        {
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.GameOver) return;
            var phase = _battle.State.Phase;
            if (phase != GamePhase.Main1 && phase != GamePhase.Main2) return;

            var player = _battle.State.Players[LocalPlayer];
            if (handIndex < 0 || handIndex >= player.Hand.Count) return;
            var card = player.Hand[handIndex].Card;

            switch (card)
            {
                case DaemonCardData:
                    _battle.ProcessAction(LocalPlayer, new PlayDaemonAction { HandIndex = handIndex });
                    break;
                case DomainCardData:
                    _battle.ProcessAction(LocalPlayer, new PlayDomainAction { HandIndex = handIndex });
                    break;
                case SealCardData:
                    _battle.ProcessAction(LocalPlayer, new SetSealAction { HandIndex = handIndex });
                    break;
                case MaskCardData when player.Field.Count > 0:
                    // Auto-target strongest daemon
                    int best = FindStrongestDaemon(player);
                    if (best >= 0)
                        _battle.ProcessAction(LocalPlayer, new PlayMaskAction { HandIndex = handIndex, TargetDaemonIndex = best });
                    break;
                case DispelCardData:
                    _battle.ProcessAction(LocalPlayer, new PlayDispelAction
                    {
                        HandIndex = handIndex,
                        TargetType = DispelTarget.Any,
                        TargetIndex = 0,
                    });
                    break;
            }
        }

        // ─── Card Click: Own Field (select attacker) ────
        private void OnOwnDaemonClicked(int fieldIndex)
        {
            if (_battle.State.CurrentPlayer != LocalPlayer || _battle.State.GameOver) return;
            if (_battle.State.Phase != GamePhase.Battle) return;

            var player = _battle.State.Players[LocalPlayer];
            if (fieldIndex < 0 || fieldIndex >= player.Field.Count) return;
            var daemon = player.Field[fieldIndex];
            if (!daemon.CanAttack || daemon.HasAttacked || daemon.Frozen || daemon.Entangled)
                return;

            _selectedAttackerIndex = fieldIndex;
            _waitingForTarget = true;
            RefreshUI(_battle.State); // Re-render to show highlights
        }

        // ─── Card Click: Opponent targets ────────────────
        private void OnOpponentDaemonClicked(int fieldIndex)
        {
            if (!_waitingForTarget || _selectedAttackerIndex < 0) return;
            // Play attack animation on the attacker card
            PlayAttackAnimOnField(p1FieldContainer, _selectedAttackerIndex);
            _battle.ProcessAction(LocalPlayer, new AttackAction
            {
                AttackerIndex = _selectedAttackerIndex,
                Target = TargetType.Daemon,
                TargetIndex = fieldIndex,
            });
            ClearSelection();
        }

        private void OnOpponentPillarClicked(int pillarIndex)
        {
            if (!_waitingForTarget || _selectedAttackerIndex < 0) return;
            PlayAttackAnimOnField(p1FieldContainer, _selectedAttackerIndex);
            _battle.ProcessAction(LocalPlayer, new AttackAction
            {
                AttackerIndex = _selectedAttackerIndex,
                Target = TargetType.Pillar,
                TargetIndex = pillarIndex,
            });
            ClearSelection();
        }

        private void OnOpponentConjurorClicked()
        {
            if (!_waitingForTarget || _selectedAttackerIndex < 0) return;
            PlayAttackAnimOnField(p1FieldContainer, _selectedAttackerIndex);
            _battle.ProcessAction(LocalPlayer, new AttackAction
            {
                AttackerIndex = _selectedAttackerIndex,
                Target = TargetType.Conjuror,
                TargetIndex = 0,
            });
            ClearSelection();
        }

        private void ClearSelection()
        {
            _selectedAttackerIndex = -1;
            _waitingForTarget = false;
        }

        private void PlayAttackAnimOnField(Transform fieldContainer, int index)
        {
            if (fieldContainer == null || index < 0 || index >= fieldContainer.childCount) return;
            var go = fieldContainer.GetChild(index).gameObject;
            var visual = go.GetComponent<CardVisual>();
            if (visual != null)
                visual.PlayAttackAnimation();
            else
                StartCoroutine(UIAnimUtils.PopScale(go.transform, 0.15f, 1.1f));
        }

        // ─── UI Refresh ──────────────────────────────────
        private void RefreshUI(GameState state)
        {
            bool isPlayerTurn = state.CurrentPlayer == LocalPlayer && !state.GameOver;
            bool isMainPhase = state.Phase == GamePhase.Main1 || state.Phase == GamePhase.Main2;
            bool isBattlePhase = state.Phase == GamePhase.Battle;

            RefreshPlayerUI(state.Players[0], p1NameText, p1HpText, p1HpBar, p1WillText, p1DeckCountText,
                p1HandContainer, p1FieldContainer, p1PillarContainer, 0,
                isPlayerTurn && isMainPhase, isPlayerTurn && isBattlePhase);
            RefreshPlayerUI(state.Players[1], p2NameText, p2HpText, p2HpBar, p2WillText, p2DeckCountText,
                p2HandContainer, p2FieldContainer, p2PillarContainer, 1,
                false, false);

            if (phaseText)
            {
                phaseText.text = state.Phase.ToString().ToUpper();
                // Quick pulse animation on phase change
                StartCoroutine(UIAnimUtils.PopScale(phaseText.transform, 0.18f, 1.15f));
            }
            if (turnText) turnText.text = $"Turn {state.TurnNumber}";

            // Button visibility
            if (nextPhaseButton) nextPhaseButton.interactable = isPlayerTurn;
            if (endTurnButton) endTurnButton.interactable = isPlayerTurn;
        }

        private void RefreshPlayerUI(PlayerState player,
            TextMeshProUGUI nameText, TextMeshProUGUI hpText, Slider hpBar,
            TextMeshProUGUI willText, TextMeshProUGUI deckCount,
            Transform handContainer, Transform fieldContainer, Transform pillarContainer,
            int playerIndex, bool handPlayable, bool fieldSelectable)
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

            // ─── Refresh hand ────────────────────────────
            if (handContainer)
            {
                ClearChildren(handContainer);
                bool isLocal = playerIndex == LocalPlayer;
                for (int i = 0; i < player.Hand.Count; i++)
                {
                    var cardInst = player.Hand[i];
                    var go = Instantiate(cardPrefab, handContainer);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                    {
                        if (isLocal)
                            visual.SetCard(cardInst.Card);
                        else
                            visual.SetFaceDown();
                    }
                    // Card deal animation — staggered slide-in from bottom
                    var rt = go.GetComponent<RectTransform>();
                    if (rt != null)
                        StartCoroutine(UIAnimUtils.SlideIn(rt, new Vector2(0, -80), 0.2f + i * 0.05f));

                    // Make local hand cards clickable during main phases
                    if (isLocal && handPlayable)
                    {
                        int idx = i;
                        var btn = go.GetComponent<Button>();
                        if (btn == null) btn = go.AddComponent<Button>();
                        btn.onClick.AddListener(() => OnHandCardClicked(idx));

                        // Highlight playable cards
                        bool canAfford = player.Will >= cardInst.Card.GetWillCost();
                        bool canPlay = canAfford && CanPlayCard(player, cardInst.Card);
                        if (canPlay)
                            AddHighlightOverlay(go, HighlightPlay);
                    }
                }
            }

            // ─── Refresh field ───────────────────────────
            if (fieldContainer)
            {
                ClearChildren(fieldContainer);
                for (int i = 0; i < player.Field.Count; i++)
                {
                    var daemon = player.Field[i];
                    var go = Instantiate(daemonFieldPrefab ?? cardPrefab, fieldContainer);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                        visual.SetCard(daemon.Card);

                    // Summon pop animation for field daemons
                    StartCoroutine(UIAnimUtils.PopScale(go.transform, 0.25f, 1.12f));

                    // Add ashe/attack overlay text
                    AddStatOverlay(go, daemon);

                    int idx = i;
                    if (playerIndex == LocalPlayer && fieldSelectable)
                    {
                        // Own daemons: click to select as attacker
                        bool canAct = daemon.CanAttack && !daemon.HasAttacked && !daemon.Frozen && !daemon.Entangled;
                        if (canAct)
                        {
                            var btn = go.GetComponent<Button>();
                            if (btn == null) btn = go.AddComponent<Button>();
                            btn.onClick.AddListener(() => OnOwnDaemonClicked(idx));
                            if (_selectedAttackerIndex == idx)
                                AddHighlightOverlay(go, HighlightAttacker);
                        }
                    }
                    else if (playerIndex == AIPlayerIndex && _waitingForTarget)
                    {
                        // Opponent daemons: click to target
                        var btn = go.GetComponent<Button>();
                        if (btn == null) btn = go.AddComponent<Button>();
                        btn.onClick.AddListener(() => OnOpponentDaemonClicked(idx));
                        AddHighlightOverlay(go, HighlightTarget);
                    }
                }
            }

            // ─── Refresh pillars ─────────────────────────
            if (pillarContainer)
            {
                ClearChildren(pillarContainer);
                bool isLocal = playerIndex == LocalPlayer;
                int visIdx = 0;
                for (int i = 0; i < player.Pillars.Count; i++)
                {
                    var pillar = player.Pillars[i];
                    if (pillar.Destroyed) continue;
                    var go = Instantiate(pillarPrefab ?? cardPrefab, pillarContainer);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                    {
                        if (isLocal || pillar.Revealed)
                            visual.SetCard(pillar.Card);
                        else
                            visual.SetFaceDown();
                    }

                    if (playerIndex == AIPlayerIndex && _waitingForTarget)
                    {
                        int pIdx = i;
                        var btn = go.GetComponent<Button>();
                        if (btn == null) btn = go.AddComponent<Button>();
                        btn.onClick.AddListener(() => OnOpponentPillarClicked(pIdx));
                        AddHighlightOverlay(go, HighlightTarget);
                    }
                    visIdx++;
                }

                // Add clickable Conjuror target when waiting for attack target
                if (playerIndex == AIPlayerIndex && _waitingForTarget)
                {
                    AddConjurorTarget(pillarContainer, player);
                }
            }
        }

        // ─── Helpers ─────────────────────────────────────
        private bool CanPlayCard(PlayerState player, CardData card)
        {
            return card switch
            {
                DaemonCardData => player.Field.Count < GameConstants.MaxFieldDaemons,
                MaskCardData => player.Field.Count > 0,
                SealCardData => player.SealZone.Count < GameConstants.MaxSeals,
                DomainCardData => true,
                DispelCardData => true,
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

        private void AddStatOverlay(GameObject cardGO, DaemonInstance daemon)
        {
            var overlay = new GameObject("Stats");
            overlay.transform.SetParent(cardGO.transform, false);
            var rt = overlay.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0.18f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = overlay.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.6f);
            bg.raycastTarget = false;

            var textGO = new GameObject("StatText");
            textGO.transform.SetParent(overlay.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(4, 0);
            trt.offsetMax = new Vector2(-4, 0);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            string statusIcons = "";
            if (daemon.Frozen) statusIcons += " FRZ";
            if (daemon.Entangled) statusIcons += " ENT";
            if (daemon.Stealthed) statusIcons += " STL";
            if (daemon.Poisoned) statusIcons += " PSN";
            if (daemon.Burning) statusIcons += " BRN";
            if (daemon.HasTaunt) statusIcons += " TNT";
            if (daemon.ShieldAmount > 0) statusIcons += $" SH{daemon.ShieldAmount}";
            if (daemon.NextAttackDouble) statusIcons += " x2";
            tmp.text = $"ATK {daemon.Attack}  HP {daemon.CurrentAshe}/{daemon.MaxAshe}{statusIcons}";
            tmp.fontSize = 10;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 6;
            tmp.fontSizeMax = 12;
            tmp.raycastTarget = false;
        }

        private void AddConjurorTarget(Transform parent, PlayerState player)
        {
            var go = new GameObject("ConjurorTarget");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(80, 40);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.6f, 0.1f, 0.1f, 0.7f);
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(OnOpponentConjurorClicked);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = $"Conjuror\n{player.Conjuror.Hp} HP";
            tmp.fontSize = 11;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
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
            _aiTurnRunning = false;
            StopAllCoroutines();
            if (gameOverPanel)
            {
                gameOverPanel.SetActive(true);
                // Animate game over panel in
                StartCoroutine(UIAnimUtils.PopScale(gameOverPanel.transform, 0.4f, 1.1f));
                var cg = gameOverPanel.GetComponent<CanvasGroup>();
                if (cg == null) cg = gameOverPanel.AddComponent<CanvasGroup>();
                StartCoroutine(UIAnimUtils.FadeIn(cg, 0.4f));
            }
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

        private void HandleDiceRollRequest(string attackerName, int threshold, System.Action<int, bool> callback)
        {
            if (diceRoller == null) { callback?.Invoke(0, false); return; }
            diceRoller.RollWithThreshold(threshold, $"{attackerName}: HIT!", $"{attackerName}: weak...", callback);
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
            if (diceRoller != null) diceRoller.Hide();
        }

        private void OnDestroy()
        {
            if (_battle != null)
            {
                _battle.OnStateChanged -= OnStateChanged;
                _battle.OnLogEntry -= AddLogEntry;
                _battle.OnGameOver -= HandleGameOver;
                _battle.OnDiceRollRequested -= HandleDiceRollRequest;
            }
        }
    }
}
