// ═══════════════════════════════════════════════════════
// DUAL CRAFT — Battle Manager (Game Engine Core)
// Handles turn flow, action processing, win conditions
// ═══════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace DualCraft.Battle
{
    using Core;
    using Cards;
    using Data;
    using Effects;

    /// <summary>
    /// Core engine for the Duel Craft TCG.  Manages the game state, enforces
    /// phase order, processes player actions, resolves combat, and checks
    /// victory conditions.  This implementation incorporates the rules
    /// described in the Dual Craft TCG rules summary: summoning sickness,
    /// attack ordering (Daemons → Invoker), damage modifiers,
    /// elemental/creature matchups, will (mana) progression and max will caps.
    /// </summary>
    public class BattleManager
    {
        public GameState State { get; private set; }
        public event Action<GameState> OnStateChanged;
        public event Action<LogEntry> OnLogEntry;
        public event Action<int, string> OnGameOver;
        public event Action<CombatResolution> OnCombatResolved;
        public event Action<HexResolution> OnHexResolved;
        /// <summary>Fired when Source cards add SE at the start of a turn. Args: (playerName, generatedAmount, storedTotal).</summary>
        public event Action<string, int, int> OnSERolled;

        private readonly CardDatabase _cardDb;
        private EffectResolver _effects;
        public EffectResolver Effects => _effects;
        private TriggerManager _triggers;
        public TriggerManager Triggers => _triggers;
        private static readonly Random _rng = new();

        public BattleManager(CardDatabase cardDb)
        {
            _cardDb = cardDb;
        }

        /// <summary>
        /// Replaces the local state from an authoritative network snapshot.
        /// The client uses this only for rendering confirmed server state.
        /// </summary>
        public void SetExternalState(GameState state)
        {
            State = state;
            OnStateChanged?.Invoke(State);
        }

        /// <summary>
        /// Initializes a new game.  Builds player states from deck data,
        /// shuffles each deck, draws starting hands.
        /// The UI layer drives the opening sequence (dice roll for first player,
        /// deck shuffle animation, etc.) and then calls CompleteSetup().
        /// </summary>
        public void InitGame(string p1Name, DeckData p1Deck, string p2Name, DeckData p2Deck)
        {
            State = new GameState
            {
                RoomId = Guid.NewGuid().ToString(),
                CurrentPlayer = 0,
                Phase = GamePhase.Setup,
                TurnNumber = 1,
            };
            State.Players[0] = CreatePlayerState("p1", p1Name, p1Deck, true);
            State.Players[1] = CreatePlayerState("p2", p2Name, p2Deck, false);

            // Initialize the effect resolver
            _effects = new EffectResolver(State);
            _effects.OnEffectLog += msg => AddLog(msg, LogEntryType.Effect);

            // Initialize the trigger manager (forge: TriggerHandler)
            _triggers = new TriggerManager();
            _triggers.OnTriggerLog += msg => AddLog(msg, LogEntryType.Effect);

            // Wire trigger manager into the effect resolver
            _effects.TriggerManager = _triggers;

            AddLog("Game started!", LogEntryType.System);
            OnStateChanged?.Invoke(State);
        }

        /// <summary>Legacy random SE helper retained for old test callers.</summary>
        public int RollSE() => _rng.Next(1, GameConstants.SEDiceSides + 1);

        /// <summary>
        /// Captures a serializable snapshot of the current match state for
        /// analysis, testing, replay inspection, or future simulation tooling.
        /// </summary>
        public GameStateSnapshot CaptureSnapshot() => GameStateSnapshot.FromState(State);

        /// <summary>
        /// Called by the UI after the opening animation.  Picks first player,
        /// shuffles decks, draws opening hands, and transitions to Draw phase.
        /// Returns (0, 0) for legacy UI callers that used to display dice rolls.
        /// </summary>
        public (int p1Roll, int p2Roll) CompleteSetup()
        {
            State.CurrentPlayer = 0;
            AddLog($"{State.Players[State.CurrentPlayer].Name} takes the first turn.", LogEntryType.System);

            // Shuffle decks
            ShuffleDeck(State.Players[0]);
            ShuffleDeck(State.Players[1]);

            // Draw opening hands
            for (int i = 0; i < GameConstants.StartingHandSize; i++)
            {
                DrawCard(State.Players[0], false);
                DrawCard(State.Players[1], false);
            }
            SmoothOpeningHand(State.Players[0]);
            SmoothOpeningHand(State.Players[1]);

            var firstPlayer = State.Players[State.CurrentPlayer];
            int generated = GenerateSourceSpiritEnergy(firstPlayer);
            if (generated > 0)
                AddLog($"{firstPlayer.Name}'s Sources generate {generated} SE ({firstPlayer.Will} total).", LogEntryType.System);
            ApplyInvokerStartTurnPassive(State.CurrentPlayer, firstPlayer);
            State.Phase = GamePhase.Draw;
            State.TurnNumber = 1;
            OnStateChanged?.Invoke(State);
            return (0, 0);
        }

        /// <summary>
        /// Generates SE from the current player's Source cards at turn start.
        /// Returns the generated value for UI feedback.
        /// </summary>
        public int RollTurnSE()
        {
            var player = State.Players[State.CurrentPlayer];
            int se = GenerateSourceSpiritEnergy(player);
            AddLog(se > 0
                ? $"{player.Name}'s Sources generate {se} SE ({player.Will} total)."
                : $"{player.Name} has no active Sources. Set a Source in Prepare to make SE.", LogEntryType.System);
            OnStateChanged?.Invoke(State);
            return se;
        }

        private PlayerState CreatePlayerState(string id, string name, DeckData deckData, bool localPlayer)
        {
            var player = new PlayerState
            {
                Id = id,
                Name = name,
                Invoker = new InvokerState
                {
                    Hp = GameConstants.InvokerMaxHp,
                    MaxHp = GameConstants.InvokerMaxHp,
                },
                InvokerCard = ResolveInvokerCard(deckData, localPlayer),
                InvokerArchetype = deckData != null ? deckData.primaryCreatureType : CreatureType.Elemental,
                Will = 0,
                MaxWill = 0,
            };
            player.Wards = ResolveWardLoadout(deckData, localPlayer);
            // Build deck from card entries
            if (deckData.cards != null)
            {
                foreach (var entry in deckData.cards)
                {
                    for (int i = 0; i < entry.count; i++)
                    {
                        player.Deck.Add(new CardInstance
                        {
                            InstanceId = Guid.NewGuid().ToString(),
                            Card = entry.card,
                        });
                    }
                }
            }
            EnsureDeckHasSourceCards(player, deckData);
            return player;
        }

        private void EnsureDeckHasSourceCards(PlayerState player, DeckData deckData)
        {
            if (player?.Deck == null)
                return;

            int sourceCount = player.Deck.Count(IsSourceCard);
            int missing = Math.Max(0, GameConstants.DeckAsheCount - sourceCount);
            if (missing <= 0)
                return;

            CreatureType archetype = deckData != null ? deckData.primaryCreatureType : player.InvokerArchetype;
            Element element = deckData != null ? deckData.element : Element.Light;
            var templates = _cardDb?.GetCardsByType<AsheCardData>()?
                .Where(card => card != null)
                .OrderBy(card => card.GetAffinityType() == archetype ? 0 : 1)
                .ThenBy(card => card.GetWillCost())
                .ThenBy(card => card.cardName)
                .ToList() ?? new List<AsheCardData>();

            for (int i = 0; i < missing; i++)
            {
                AsheCardData source = templates.Count > 0
                    ? CloneRuntimeSourceForDeck(templates[i % templates.Count], archetype, element, i)
                    : CreateFallbackRuntimeSource(archetype, element, i);

                var sourceInstance = new CardInstance
                {
                    InstanceId = Guid.NewGuid().ToString(),
                    Card = source,
                };

                int replaceIndex = player.Deck.Count >= GameConstants.DeckSize
                    ? FindSourceTopUpReplacementIndex(player)
                    : -1;
                if (replaceIndex >= 0)
                    player.Deck[replaceIndex] = sourceInstance;
                else
                    player.Deck.Add(sourceInstance);
            }

            AddLog($"{player.Name}'s deck was topped up with {missing} Source card{(missing == 1 ? "" : "s")} for the current rules mix.", LogEntryType.System);
        }

        private static int FindSourceTopUpReplacementIndex(PlayerState player)
        {
            if (player?.Deck == null)
                return -1;

            int index = player.Deck.FindIndex(card => !IsSourceCard(card) && card?.Card is not DaemonCardData);
            if (index >= 0)
                return index;

            index = player.Deck.FindIndex(card => !IsSourceCard(card) && card?.Card is DaemonCardData daemon && daemon.evolvesTo == null);
            if (index >= 0)
                return index;

            return player.Deck.FindIndex(card => !IsSourceCard(card));
        }

        private static AsheCardData CloneRuntimeSourceForDeck(AsheCardData template, CreatureType archetype, Element element, int variant)
        {
            AsheCardData source = template != null
                ? UnityEngine.ScriptableObject.Instantiate(template)
                : CreateFallbackRuntimeSource(archetype, element, variant);
            ConfigureRuntimeSource(source, archetype, element, variant);
            return source;
        }

        private static AsheCardData CreateFallbackRuntimeSource(CreatureType archetype, Element element, int variant)
        {
            AsheCardData source = UnityEngine.ScriptableObject.CreateInstance<AsheCardData>();
            source.category = CardCategory.AsheCard;
            source.rarity = Rarity.Common;
            ConfigureRuntimeSource(source, archetype, element, variant);
            return source;
        }

        private static void ConfigureRuntimeSource(AsheCardData source, CreatureType archetype, Element element, int variant)
        {
            if (source == null)
                return;

            source.hideFlags = UnityEngine.HideFlags.DontSave;
            source.category = CardCategory.AsheCard;
            source.cardId = $"bm-source-{archetype.ToString().ToLowerInvariant()}-{variant}";
            source.cardName = $"{archetype} Source";
            source.name = source.cardId;
            source.willCost = 0;
            source.sePerTurn = GameConstants.SourceSEPerTurn;
            source.matchType = AsheMatchType.CreatureType;
            source.targetCreatureType = archetype;
            source.targetElement = element;
            source.shieldAmount = 0;
            source.resurrectHp = 0;
            source.buffAttack = 0;
            source.buffAshe = 0;
            source.buffTurns = 0;
            source.description = $"Source. Free to play. Generates {GameConstants.SourceSEPerTurn} SE each turn.";
            if (string.IsNullOrWhiteSpace(source.flavorText))
                source.flavorText = "A quiet bondstone tuned to the Invoker's grimware, steady enough to wake power without angering it.";
        }

        private InvokerCardData ResolveInvokerCard(DeckData deckData, bool localPlayer)
        {
            if (deckData?.invokerCard != null)
                return deckData.invokerCard;

            IReadOnlyList<InvokerCardData> invokers = _cardDb?.GetCardsByType<InvokerCardData>();
            if (invokers == null || invokers.Count == 0)
                return null;

            if (localPlayer)
            {
                string selectedDesign = ProfileManager.Load()?.activeInvokerDesign;
                if (!string.IsNullOrWhiteSpace(selectedDesign))
                {
                    InvokerCardData selectedInvoker = _cardDb?.GetCard(selectedDesign) as InvokerCardData;
                    if (selectedInvoker != null)
                        return selectedInvoker;
                }
            }

            InvokerCardData fallback = null;
            foreach (var invoker in invokers)
            {
                if (invoker == null)
                    continue;

                fallback ??= invoker;
                if (deckData != null && invoker.archetype == deckData.primaryCreatureType)
                    return invoker;
            }

            foreach (var invoker in invokers)
            {
                if (deckData != null && invoker != null && invoker.element == deckData.element)
                    return invoker;
            }

            return fallback;
        }

        private static List<WardInstance> ResolveWardLoadout(DeckData deckData, bool localPlayer)
        {
            if (deckData?.wardIds != null && deckData.wardIds.Length > 0)
                return WardCatalog.BuildInstances(deckData.wardIds);

            if (localPlayer)
            {
                var profile = ProfileManager.Load();
                return WardCatalog.BuildInstances(profile.equippedWardIds);
            }

            return WardCatalog.BuildInstances(WardCatalog.StarterWardIds);
        }

        /// <summary>
        /// Processes a player action.  Validates turn ownership and delegates to
        /// specific handlers based on the action type.  Returns true if the
        /// action was successfully executed.
        /// </summary>
        public bool ProcessAction(int playerIndex, GameAction action)
        {
            if (State.GameOver)
                return false;
            if (playerIndex != State.CurrentPlayer)
                return false;
            var player = State.Players[playerIndex];
            var opponent = State.Players[1 - playerIndex];
            bool result = action switch
            {
                DrawCardAction => HandleDraw(player),
                PlayDaemonAction pda => HandlePlayDaemon(player, pda.HandIndex, pda.TargetLane),
                PlayDomainAction pdo => HandlePlayDomain(player, playerIndex, pdo.HandIndex, pdo.ResponseDispelHandIndex),
                PlayMaskAction pma => HandlePlayMask(player, pma.HandIndex, pma.TargetDaemonIndex),
                SetSealAction ssa => HandleSetSeal(player, ssa.HandIndex),
                PlayDispelAction pdi => HandlePlayDispel(player, opponent, playerIndex, pdi),
                PlayHexAction pha => HandlePlayHex(player, playerIndex, pha.HandIndex, pha.TargetDaemonIndex, pha.ResponseDispelHandIndex),
                EvolveAction ea => HandleSecondForm(player, ea.FieldIndex, ea.ConsumeIndex, ea.AnchorFieldIndices),
                AttackAction aa => HandleAttack(player, opponent, playerIndex, aa),
                FuseDaemonsAction fda => HandleFuseDaemons(player, fda.PrimaryIndex, fda.SecondaryIndex),
                ActivatePillarAction apa => HandleActivatePillar(player, apa.PillarIndex, apa.AbilityIndex),
                ActivateInvokerAction => HandleActivateInvoker(player, playerIndex),
                PlayAsheCardAction pac => HandlePlayAsheCard(player, pac.HandIndex, pac.TargetDaemonFieldIndex),
                AssignAsheCardAction aac => HandleAssignAsheCard(player, aac.AsheCardBoardIndex, aac.TargetDaemonFieldIndex),
                AttackAsheCardAction atk => HandleAttackAsheCard(player, opponent, playerIndex, atk.AttackerFieldIndex, atk.AsheCardBoardIndex),
                SacrificeDaemonAction sacrifice => HandleSacrificeDaemon(player, sacrifice.FieldIndex),
                SwitchLaneAction switchLane => HandleSwitchLane(player, switchLane.FieldIndex, switchLane.TargetLane),
                NextPhaseAction => HandleNextPhase(),
                EndTurnAction => EndTurn(),
                _ => false,
            };
            if (result)
                ClearRitualChain(action.Type.ToString());
            return result;
        }

        // ─── Draw Phase ───────────────────────────────────────────────
        private bool HandleDraw(PlayerState player)
        {
            if (State.Phase != GamePhase.Draw)
                return false;

            // Grimware exhaustion loss
            if (player.Deck.Count == 0)
            {
                int loser = State.CurrentPlayer;
                State.Winner = 1 - loser;
                State.GameOver = true;
                AddLog($"{player.Name}'s grimware is exhausted!", LogEntryType.System);
                OnGameOver?.Invoke(1 - loser, "Grimware exhausted!");
                return true;
            }

            if (State.TurnNumber == 1 && State.CurrentPlayer == 0)
                AddLog($"{player.Name} takes the opening turn without drawing.", LogEntryType.System);
            else
                DrawCard(player);
            // Transition to Main phase (play cards, then Combat for attacks)
            State.Phase = GamePhase.Main;
            OnStateChanged?.Invoke(State);
            return true;
        }

        private void DrawCard(PlayerState player, bool announce = true)
        {
            if (player.Deck.Count == 0)
                return;
            if (player.Hand.Count >= GameConstants.MaxHandSize)
            {
                var overdrawn = player.Deck[0];
                player.Deck.RemoveAt(0);
                player.AshePile.Add(overdrawn);
                string overdrawnName = overdrawn?.Card != null && !string.IsNullOrWhiteSpace(overdrawn.Card.cardName)
                    ? overdrawn.Card.cardName
                    : "a card";
                AddLog($"{player.Name}'s hand is full. {overdrawnName} is overdrawn into the Void.", LogEntryType.System);
                return;
            }
            var card = player.Deck[0];
            player.Deck.RemoveAt(0);
            player.Hand.Add(card);
            if (announce)
                AddLog($"{player.Name} draws a card.", LogEntryType.System);
        }

        private void SmoothOpeningHand(PlayerState player)
        {
            EnsureOpeningCard(player, IsSourceCard, FindOpeningSourceDeckIndex, FindSourceReplacementHandIndex);
            EnsureOpeningMinimumSources(player, 2);
            EnsureOpeningCard(player, IsOpeningDaemon, FindOpeningDaemonDeckIndex, FindDaemonReplacementHandIndex);
        }

        private static void EnsureOpeningMinimumSources(PlayerState player, int minimumSources)
        {
            if (player == null)
                return;

            while (player.Hand.Count(IsSourceCard) < minimumSources)
            {
                int deckIndex = FindOpeningSourceDeckIndex(player);
                int handIndex = FindSourceReplacementHandIndex(player);
                if (deckIndex < 0 || handIndex < 0 || IsSourceCard(player.Hand[handIndex]))
                    break;

                (player.Hand[handIndex], player.Deck[deckIndex]) = (player.Deck[deckIndex], player.Hand[handIndex]);
            }
        }

        private static void EnsureOpeningCard(PlayerState player, Predicate<CardInstance> handPredicate,
            Func<PlayerState, int> deckFinder, Func<PlayerState, int> replacementFinder)
        {
            if (player == null || player.Hand.Exists(handPredicate))
                return;

            int deckIndex = deckFinder(player);
            int handIndex = replacementFinder(player);
            if (deckIndex < 0 || handIndex < 0)
                return;

            (player.Hand[handIndex], player.Deck[deckIndex]) = (player.Deck[deckIndex], player.Hand[handIndex]);
        }

        private static int FindOpeningSourceDeckIndex(PlayerState player)
        {
            return player.Deck.FindIndex(IsSourceCard);
        }

        private static int FindOpeningDaemonDeckIndex(PlayerState player)
        {
            int bestIndex = -1;
            int bestCost = int.MaxValue;
            int bestAttack = int.MinValue;
            for (int i = 0; i < player.Deck.Count; i++)
            {
                if (player.Deck[i].Card is not DaemonCardData daemon)
                    continue;

                int cost = daemon.GetWillCost();
                bool better = cost < bestCost || (cost == bestCost && daemon.attack > bestAttack);
                if (better)
                {
                    bestIndex = i;
                    bestCost = cost;
                    bestAttack = daemon.attack;
                }
            }

            return bestIndex;
        }

        private static int FindSourceReplacementHandIndex(PlayerState player)
        {
            int index = player.Hand.FindIndex(c => !IsSourceCard(c) && c.Card is not DaemonCardData);
            if (index >= 0)
                return index;

            index = player.Hand.FindIndex(c => c.Card is DaemonCardData daemon && daemon.GetWillCost() > 2);
            return index >= 0 ? index : player.Hand.Count - 1;
        }

        private static int FindDaemonReplacementHandIndex(PlayerState player)
        {
            int index = player.Hand.FindIndex(c => !IsSourceCard(c) && c.Card is not DaemonCardData);
            if (index >= 0)
                return index;

            int sourceCount = player.Hand.Count(IsSourceCard);
            if (sourceCount > 1)
                return player.Hand.FindLastIndex(IsSourceCard);

            int highestCostIndex = -1;
            int highestCost = int.MinValue;
            for (int i = 0; i < player.Hand.Count; i++)
            {
                if (player.Hand[i].Card is not DaemonCardData daemon)
                    continue;

                int cost = daemon.GetWillCost();
                if (cost > highestCost)
                {
                    highestCost = cost;
                    highestCostIndex = i;
                }
            }

            return highestCostIndex;
        }

        private static bool IsSourceCard(CardInstance card)
        {
            return card?.Card is AsheCardData || card?.Card?.category == CardCategory.AsheCard;
        }

        private static bool IsOpeningDaemon(CardInstance card)
        {
            return card?.Card is DaemonCardData;
        }

        // ─── Play Ashe Card ──────────────────────────────────────
        private bool HandlePlayAsheCard(PlayerState player, int handIndex, int targetDaemonFieldIndex)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;

            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not AsheCardData ashe)
                return false;
            if (player.SourcePlayedThisTurn)
            {
                AddLog($"{player.Name} can set only one Source each turn.", LogEntryType.System);
                return false;
            }

            // Source cards are energy engines only. Relics now carry daemon-attachment effects.
            targetDaemonFieldIndex = -1;

            player.Hand.RemoveAt(handIndex);
            CommitElementForCard(player, ashe);
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.CardPlayed, ashe,
                $"{player.Name} sets Source {ashe.cardName}.");

            var instance = new AsheCardInstance
            {
                InstanceId = cardInstance.InstanceId,
                Card = ashe,
                AssignedDaemonInstanceId = null,
            };
            player.AsheCards.Add(instance);
            player.SourcePlayedThisTurn = true;

            AddLog($"{player.Name} sets Source {ashe.cardName}. It adds {GameConstants.SourceSEPerTurn} SE each turn while active.", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        private bool HandleAssignAsheCard(PlayerState player, int boardIndex, int targetDaemonFieldIndex)
        {
            AddLog("Sources cannot attach to daemons. Use Relics for daemon upgrades.", LogEntryType.System);
            return false;
        }

        private bool HandleAttackAsheCard(PlayerState player, PlayerState opponent, int playerIndex, int attackerFieldIndex, int asheCardBoardIndex)
        {
            if (State.Phase != GamePhase.Combat)
                return false;
            if (attackerFieldIndex < 0 || attackerFieldIndex >= player.Field.Count)
                return false;
            if (asheCardBoardIndex < 0 || asheCardBoardIndex >= opponent.AsheCards.Count)
                return false;
            if (opponent.Field.Any(d => !d.Stealthed))
            {
                AddLog("Daemons are defending the Source line. Clear them before targeting Source cards.", LogEntryType.System);
                return false;
            }
            if (player.SourceRaidUsedThisTurn)
            {
                AddLog($"{player.Name} already raided a Source this turn.", LogEntryType.System);
                return false;
            }

            var attacker = player.Field[attackerFieldIndex];
            if (attacker.IsBindAnchor || !attacker.CanAttack || attacker.HasAttacked || attacker.Frozen || attacker.Entangled || attacker.DeathsDoor)
                return false;
            if (!TrySpendAttackSpiritEnergy(player, attacker, out _))
            {
                AddLog($"{attacker.Card.cardName} needs more SE to strike.", LogEntryType.System);
                return false;
            }

            var asheTarget = opponent.AsheCards[asheCardBoardIndex];
            if (!string.IsNullOrEmpty(asheTarget.AssignedDaemonInstanceId))
            {
                AddLog($"{asheTarget.Card.cardName} is bonded. Clear the daemon before raiding it.", LogEntryType.System);
                return false;
            }

            asheTarget.SuppressedTurnsRemaining = Math.Max(asheTarget.SuppressedTurnsRemaining, 1);
            player.SourceRaidUsedThisTurn = true;
            AddLog($"{attacker.Card.cardName} raids {asheTarget.Card.cardName}! That Source is offline next turn and generates 0 SE.", LogEntryType.Combat);

            attacker.HasAttacked = true;
            if (!State.GameOver)
                CheckWinConditions();

            OnStateChanged?.Invoke(State);
            return true;
        }

        /// <summary>
        /// Initialises type-specific runtime state on an Ashe card when it is assigned
        /// to a daemon (both on fresh play and on reassignment after daemon death).
        /// </summary>
        private void ApplyAsheCardOnAssign(AsheCardInstance instance, AsheCardData ashe, DaemonInstance targetDaemon)
        {
            // Reset prior state (handles reassignment)
            instance.ShieldRemaining = 0;
            instance.BuffTurnsRemaining = 0;

            CreatureType affinity = ashe.GetAffinityType();
            if (affinity == CreatureType.Artificial && ashe.shieldAmount > 0)
            {
                instance.ShieldRemaining = ashe.shieldAmount;
                AddLog($"{ashe.cardName} raises a Force Field of {ashe.shieldAmount} on {targetDaemon.Card.cardName}!", LogEntryType.Effect);
            }
            else if (affinity == CreatureType.Spirit && ashe.buffTurns > 0)
            {
                int turns = ashe.buffTurns;
                instance.BuffTurnsRemaining = turns;
                if (ashe.buffAttack > 0)
                    targetDaemon.Modifiers.Add(StatModifier.FlatAttack(ashe.buffAttack, ashe.cardName, turns));
                if (ashe.buffAshe > 0)
                    targetDaemon.Modifiers.Add(StatModifier.FlatAshe(ashe.buffAshe, ashe.cardName, turns));
                targetDaemon.RecalculateStats();
                AddLog($"{ashe.cardName} empowers {targetDaemon.Card.cardName}: +{ashe.buffAttack} ATK, +{ashe.buffAshe} Life for {turns} turns.", LogEntryType.Effect);
            }
        }

        private AsheCardInstance FindAssignedAsheCard(PlayerState player, DaemonInstance daemon, CreatureType affinity)
        {
            if (player?.AsheCards == null || daemon == null)
                return null;

            return player.AsheCards.Find(ashe =>
                ashe != null
                && ashe.Card != null
                && ashe.AssignedDaemonInstanceId == daemon.InstanceId
                && ashe.Card.GetAffinityType() == affinity);
        }

        private int GetAttackSpiritCost(PlayerState player, DaemonInstance attacker)
        {
            if (player == null || attacker == null)
                return 0;

            int cost = Math.Max(1, attacker.AsheCost);
            if (attacker.Taxed)
                cost += Math.Max(1, attacker.TaxedExtraCost);
            return cost;
        }

        private bool TrySpendAttackSpiritEnergy(PlayerState player, DaemonInstance attacker, out int attackCost)
        {
            attackCost = GetAttackSpiritCost(player, attacker);
            if (player == null)
                return false;
            if (player.Will < attackCost)
            {
                if (!TryOverchannel(player, attacker?.Card, attackCost - player.Will))
                    return false;
            }

            player.Will -= attackCost;
            return true;
        }

        private bool TrySpendWill(PlayerState player, CardData sourceCard, int cost)
        {
            if (player == null)
                return false;
            if (player.Will < cost && !TryOverchannel(player, sourceCard, cost - player.Will))
                return false;
            player.Will -= cost;
            return true;
        }

        private bool TryOverchannel(PlayerState player, CardData sourceCard, int shortfall)
        {
            if (player?.Invoker == null || sourceCard == null || shortfall <= 0)
                return false;
            if (!HasKeyword(sourceCard, Keyword.Overchannel))
                return false;

            int pulses = (int)Math.Ceiling(shortfall / (float)GameConstants.OverchannelSEGain);
            int lifeCost = pulses * GameConstants.OverchannelLifeCost;
            int seGain = pulses * GameConstants.OverchannelSEGain;
            if (player.Invoker.Hp <= lifeCost)
                return false;

            player.Invoker.Hp -= lifeCost;
            int gained = AddStoredSpiritEnergy(player, seGain);
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.KeywordResponse, sourceCard,
                $"Overchannel: paid {lifeCost} Invoker life for {gained} SE.");
            AddLog($"{player.Name} overchannels {sourceCard.cardName}: -{lifeCost} Invoker life, +{gained} SE.", LogEntryType.Effect);
            return player.Will >= shortfall;
        }

        // ─── Play Daemon ─────────────────────────────────────────────
        private bool HandlePlayDaemon(PlayerState player, int handIndex, int targetLane = -1)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;
            if (player.Field.Count >= GameConstants.MaxFieldDaemons)
                return false;
            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not DaemonCardData daemon)
                return false;
            if (IsSecondFormOnlyDaemon(daemon))
            {
                AddLog($"{daemon.cardName} is a Second Form. Bind it through matching anchors instead of summoning it.", LogEntryType.System);
                return false;
            }
            int cost = daemon.GetWillCost();
            // Apply cost reduction from effects
            if (player.CostReduction > 0)
            {
                int reduction = Math.Min(cost, player.CostReduction);
                cost -= reduction;
                player.CostReduction -= reduction;
            }
            if (!TrySpendWill(player, daemon, cost))
                return false;
            player.Hand.RemoveAt(handIndex);
            CommitElementForCard(player, daemon);
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.CardPlayed, daemon,
                $"{player.Name} summons {daemon.cardName}.");
            int summonAshe = daemon.ashe;
            if (daemon.creatureType == CreatureType.Undead)
            {
                summonAshe += GameConstants.UndeadSpawnAsheBonus;
                AddLog($"{daemon.cardName} rises with +{GameConstants.UndeadSpawnAsheBonus} Life, but will decay each turn.", LogEntryType.Effect);
            }
            // Create runtime instance. Daemons can act right away unless status effects stop them.
            int resolvedLane = ResolveSummonLane(player, targetLane);
            var instance = new DaemonInstance
            {
                InstanceId = cardInstance.InstanceId,
                Card = daemon,
                BaseAttack = daemon.attack,
                BaseAshe = summonAshe,
                CurrentAshe = summonAshe,
                MaxAshe = summonAshe,
                Attack = daemon.attack,
                AsheCost = daemon.asheCost,
                LaneIndex = resolvedLane,
                CanAttack = true,
                HasAttacked = false,
            };
            player.Field.Add(instance);
            AddLog($"{player.Name} summoned {daemon.cardName}!", LogEntryType.Action);
            ResolveDevourOnSummon(player, instance);

            // Fire OnSummon effects
            _effects.OnDaemonSummoned(State.CurrentPlayer, instance);
            _triggers.Fire(GameTrigger.OnDaemonSummoned, new TriggerEvent
            {
                State = State, SourcePlayer = State.CurrentPlayer, Daemon = instance,
            });

            // Apply mask effects for Haste
            foreach (var m in instance.Masks)
            {
                if (m.Card.effectType == MaskEffectType.Haste)
                    instance.CanAttack = true;
                _effects.ApplyMaskEffect(State.CurrentPlayer, instance, m.Card);
            }

            // Clean up any daemons killed by OnSummon effects
            var deadOpponent = _effects.CleanupDead(1 - State.CurrentPlayer);
            foreach (var dead in deadOpponent)
                ResolveDaemonDestroyed(1 - State.CurrentPlayer, dead);
            CheckWinConditions();

            OnStateChanged?.Invoke(State);
            return true;
        }

        private bool IsSecondFormOnlyDaemon(DaemonCardData daemon)
        {
            if (daemon == null)
                return false;

            if (_cardDb != null && _cardDb.GetCardsByType<DaemonCardData>().Any(card => card != null && card.evolvesTo == daemon))
                return true;

            return daemon.evolvesTo == null
                && !string.IsNullOrWhiteSpace(daemon.cardName)
                && daemon.cardName.IndexOf("Second Form", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ResolveSummonLane(PlayerState player, int requestedLane)
        {
            int lane = requestedLane >= 0 ? requestedLane : -1;
            if (lane >= 0 && lane < GameConstants.MaxFieldDaemons && !IsLaneOccupied(player, lane))
                return lane;

            for (int i = 0; i < GameConstants.MaxFieldDaemons; i++)
            {
                if (!IsLaneOccupied(player, i))
                    return i;
            }

            int fallback = player?.Field?.Count ?? 0;
            return Math.Max(0, Math.Min(fallback, GameConstants.MaxFieldDaemons - 1));
        }

        private static bool IsLaneOccupied(PlayerState player, int lane)
        {
            return player?.Field != null
                && player.Field.Any(d => d != null && d.LaneIndex == lane);
        }

        private bool HandleSwitchLane(PlayerState player, int fieldIndex, int targetLane)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (player?.Field == null || fieldIndex < 0 || fieldIndex >= player.Field.Count)
                return false;
            if (targetLane < 0 || targetLane >= GameConstants.MaxFieldDaemons)
                return false;

            var daemon = player.Field[fieldIndex];
            if (daemon == null || daemon.HasAttacked || !daemon.CanAttack || daemon.Frozen || daemon.Entangled || daemon.DeathsDoor)
                return false;
            if (IsLaneOccupied(player, targetLane))
            {
                AddLog("That lane is occupied. Sacrifice, destroy, or choose an open lane.", LogEntryType.System);
                return false;
            }

            int fromLane = daemon.LaneIndex >= 0 ? daemon.LaneIndex : fieldIndex;
            daemon.LaneIndex = targetLane;
            daemon.HasAttacked = true;
            AddLog($"{daemon.Card.cardName} shifts from lane {fromLane + 1} to lane {targetLane + 1}, spending its attack turn.", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        private bool HandleSacrificeDaemon(PlayerState player, int fieldIndex)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (player?.Field == null || fieldIndex < 0 || fieldIndex >= player.Field.Count)
                return false;

            var daemon = player.Field[fieldIndex];
            if (daemon?.Card == null)
                return false;

            int ownerIndex = State.Players[0] == player ? 0 : 1;
            bool wasBindAnchor = daemon.IsBindAnchor;
            bool wasBoundSecondForm = daemon.IsSecondFormBound;
            player.Field.RemoveAt(fieldIndex);
            ReleaseAsheBindings(player, daemon);
            player.AshePile.Add(new CardInstance { InstanceId = daemon.InstanceId, Card = daemon.Card });
            ReleaseSecondFormBindReferences(ownerIndex, player, daemon);

            int gained = Math.Max(1, Math.Max(daemon.Card.GetWillCost(), (int)daemon.Card.rarity + 1));
            gained = AddStoredSpiritEnergy(player, gained);
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.CardPlayed, daemon.Card,
                $"{player.Name} sacrifices {daemon.Card.cardName} for {gained} SE.", false);
            AddLog($"{player.Name} sacrifices {daemon.Card.cardName}: +{gained} SE and one field slot opens.", LogEntryType.Action);
            if (wasBindAnchor)
                AddLog("A Bind anchor was removed. The Second Form collapses if no anchors remain.", LogEntryType.Effect);
            else if (wasBoundSecondForm)
                AddLog("The Second Form leaves play and releases its face-down anchors.", LogEntryType.Effect);
            OnStateChanged?.Invoke(State);
            return true;
        }

        private static void ReleaseAsheBindings(PlayerState player, DaemonInstance daemon)
        {
            if (player?.AsheCards == null || daemon == null)
                return;

            foreach (var source in player.AsheCards)
            {
                if (source != null && source.AssignedDaemonInstanceId == daemon.InstanceId)
                    source.AssignedDaemonInstanceId = null;
            }
        }

        private bool HandleSecondForm(
            PlayerState player,
            int fieldIndex,
            int secondFormHandIndex,
            IReadOnlyList<int> explicitAnchorIndices)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (secondFormHandIndex < 0 || secondFormHandIndex >= player.Hand.Count)
                return false;
            if (player.Field.Count >= GameConstants.MaxFieldDaemons)
            {
                AddLog("Bind needs an open lane for the Second Form.", LogEntryType.System);
                return false;
            }

            var handCard = player.Hand[secondFormHandIndex];
            if (handCard.Card is not DaemonCardData secondForm || !IsSecondFormOnlyDaemon(secondForm))
                return false;

            int requiredAnchors = GetSecondFormBindAnchorRequirement(secondForm);
            var anchors = SelectBindAnchors(player, secondForm, fieldIndex, explicitAnchorIndices, out string selectionError);
            if (anchors.Count != requiredAnchors)
            {
                AddLog(string.IsNullOrEmpty(selectionError)
                    ? $"{secondForm.cardName} needs {requiredAnchors} matching archetype anchor{(requiredAnchors == 1 ? "" : "s")} to Bind."
                    : selectionError, LogEntryType.System);
                return false;
            }

            int cost = Math.Max(0, secondForm.GetWillCost());
            if (!TrySpendWill(player, secondForm, cost))
                return false;

            player.Hand.RemoveAt(secondFormHandIndex);

            var primaryAnchor = anchors[0];
            int boundAttack = GetSecondFormAttackFloor(primaryAnchor.Card, secondForm);
            int boundLife = GetSecondFormLifeFloor(primaryAnchor.Card, secondForm);
            int lane = ResolveSummonLane(player, -1);
            var boundDaemon = new DaemonInstance
            {
                InstanceId = handCard.InstanceId,
                Card = secondForm,
                BaseAttack = boundAttack,
                BaseAshe = boundLife,
                CurrentAshe = boundLife,
                MaxAshe = boundLife,
                Attack = boundAttack,
                AsheCost = secondForm.asheCost,
                LaneIndex = lane,
                CanAttack = true,
                HasAttacked = false,
                IsSecondFormBound = true,
                BoundAnchorInstanceIds = anchors.Select(a => a.InstanceId).ToList(),
            };

            foreach (var anchor in anchors)
            {
                anchor.IsBindAnchor = true;
                anchor.BoundSecondFormInstanceId = boundDaemon.InstanceId;
                anchor.CanAttack = false;
                anchor.HasAttacked = true;
            }

            player.Field.Add(boundDaemon);
            CommitElementForCard(player, secondForm);

            AddRitualStep(State.CurrentPlayer, RitualChainActionType.CardPlayed, secondForm,
                $"{player.Name} binds {secondForm.cardName} through {anchors.Count} anchor{(anchors.Count == 1 ? "" : "s")}.");
            AddLog($"{player.Name} binds {secondForm.cardName}. Anchors cannot attack and lose 1 Life each turn.", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        private static int GetSecondFormBindAnchorRequirement(DaemonCardData secondForm)
        {
            if (secondForm == null)
                return 1;

            return secondForm.rarity switch
            {
                Rarity.Legendary => 3,
                Rarity.Epic => 2,
                _ => 1,
            };
        }

        private List<DaemonInstance> SelectBindAnchors(
            PlayerState player,
            DaemonCardData secondForm,
            int preferredFieldIndex,
            IReadOnlyList<int> explicitAnchorIndices,
            out string error)
        {
            int required = GetSecondFormBindAnchorRequirement(secondForm);
            var anchors = new List<DaemonInstance>();
            error = null;
            if (player?.Field == null || secondForm == null)
                return anchors;

            bool Matches(DaemonInstance daemon)
            {
                if (daemon?.Card == null || daemon.CurrentAshe <= 0)
                    return false;
                if (daemon.IsSecondFormBound || daemon.IsBindAnchor)
                    return false;
                return daemon.Card.creatureType == secondForm.creatureType
                    || daemon.Card.evolvesTo == secondForm;
            }

            if (explicitAnchorIndices != null && explicitAnchorIndices.Count > 0)
            {
                if (explicitAnchorIndices.Count != required)
                {
                    error = $"{secondForm.cardName} requires exactly {required} chosen Bind anchor{(required == 1 ? "" : "s")}.";
                    return anchors;
                }

                var chosenIndices = new HashSet<int>();
                foreach (int index in explicitAnchorIndices)
                {
                    if (!chosenIndices.Add(index))
                    {
                        error = "The same daemon cannot be chosen as more than one Bind anchor.";
                        anchors.Clear();
                        return anchors;
                    }
                    if (index < 0 || index >= player.Field.Count || !Matches(player.Field[index]))
                    {
                        error = "One of the chosen Bind anchors is no longer eligible.";
                        anchors.Clear();
                        return anchors;
                    }

                    anchors.Add(player.Field[index]);
                }

                return anchors;
            }

            // Legacy clients only sent one preferred index. Retain the old fallback so
            // an in-progress room can finish, but current clients always send all choices.
            if (preferredFieldIndex >= 0 && preferredFieldIndex < player.Field.Count && Matches(player.Field[preferredFieldIndex]))
                anchors.Add(player.Field[preferredFieldIndex]);

            foreach (var daemon in player.Field)
            {
                if (anchors.Count >= required)
                    break;
                if (anchors.Contains(daemon) || !Matches(daemon))
                    continue;
                anchors.Add(daemon);
            }

            return anchors;
        }

        private static int GetSecondFormAttackFloor(DaemonCardData baseForm, DaemonCardData secondForm)
        {
            if (secondForm == null)
                return 0;

            int baseAttack = baseForm != null ? Math.Max(0, baseForm.attack) : 0;
            int rarityBonus = GetSecondFormAttackBonus(secondForm.rarity);
            return Math.Max(Math.Max(1, secondForm.attack), baseAttack + rarityBonus);
        }

        private static int GetSecondFormLifeFloor(DaemonCardData baseForm, DaemonCardData secondForm)
        {
            if (secondForm == null)
                return 1;

            int baseLife = baseForm != null ? Math.Max(1, baseForm.ashe) : 1;
            int rarityBonus = GetSecondFormLifeBonus(secondForm.rarity);
            return Math.Max(Math.Max(1, secondForm.ashe), baseLife + rarityBonus);
        }

        private static int GetSecondFormAttackBonus(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Legendary => 9,
                Rarity.Epic => 6,
                Rarity.Rare => 4,
                _ => 3,
            };
        }

        private static int GetSecondFormLifeBonus(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Legendary => 14,
                Rarity.Epic => 10,
                Rarity.Rare => 7,
                _ => 5,
            };
        }

        // ─── Play Domain ─────────────────────────────────────────────
        private bool HandlePlayDomain(PlayerState player, int playerIndex, int handIndex, int responseDispelHandIndex)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;
            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not DomainCardData domain)
                return false;
            int cost = domain.GetWillCost();
            if (!TrySpendWill(player, domain, cost))
                return false;
            player.Hand.RemoveAt(handIndex);
            CommitElementForCard(player, domain);
            AddRitualStep(playerIndex, RitualChainActionType.CardPlayed, domain,
                $"{player.Name} casts domain {domain.cardName}.");

            if (TryConsumeSpellResponseDispel(State.Players[1 - playerIndex], playerIndex,
                    responseDispelHandIndex, domain, SpellResponseWindow.Domain) != null)
            {
                player.AshePile.Add(cardInstance);
                AddLog($"{domain.cardName} is fully negated.", LogEntryType.Effect);
                OnStateChanged?.Invoke(State);
                return true;
            }

            if (_effects.OnSpellCast(playerIndex))
            {
                player.AshePile.Add(cardInstance);
                AddLog($"{domain.cardName} was negated by a Seal!", LogEntryType.Effect);
                OnStateChanged?.Invoke(State);
                return true;
            }

            int domainTurns = ResolveDomainDuration(domain);
            State.ActiveDomain = new ActiveDomain
            {
                Card = domain,
                Owner = playerIndex,
                TurnsRemaining = domainTurns,
            };
            AddLog($"{player.Name} played {domain.cardName}!", LogEntryType.Action);
            AddLog($"Domain: {DescribeDomainEffect(domain)} ({domainTurns} turns).", LogEntryType.Effect);
            ResolveDomainEntrance(playerIndex, domain);
            OnStateChanged?.Invoke(State);
            return true;
        }

        private void ResolveDomainEntrance(int ownerIndex, DomainCardData domain)
        {
            if (domain == null || ownerIndex < 0 || ownerIndex >= State.Players.Length)
                return;

            int opposingIndex = 1 - ownerIndex;
            bool hostilePulse = domain.effectType is DomainEffectType.PoisonAll
                or DomainEffectType.BurnAll or DomainEffectType.WillDrain
                or DomainEffectType.FreezeAll or DomainEffectType.EntangleAll
                or DomainEffectType.SilenceAll or DomainEffectType.DebuffAtkAll
                or DomainEffectType.WeakenAll or DomainEffectType.StealWill;
            _effects.ApplyDomainEffect(hostilePulse ? opposingIndex : ownerIndex);

            if (domain.effectType == DomainEffectType.ArtificialConstruct)
                ApplyStrategicDomainTurnStart(ownerIndex, true);
            else if (domain.effectType is DomainEffectType.PetrifyCycle
                or DomainEffectType.SpiritDrain or DomainEffectType.ArtificialCorrupt
                or DomainEffectType.MachinePropagate or DomainEffectType.SpiritDefile)
                ApplyStrategicDomainTurnStart(opposingIndex, true);
            else if (domain.effectType is DomainEffectType.MachineGlitch
                or DomainEffectType.RelicLock or DomainEffectType.TypeNull
                or DomainEffectType.UndeadConsume)
            {
                DrawCard(State.Players[ownerIndex]);
                AddLog($"{domain.cardName} reveals one card as its field takes hold.", LogEntryType.Effect);
            }

            for (int playerIndex = 0; playerIndex < State.Players.Length; playerIndex++)
            {
                foreach (DaemonInstance defeated in _effects.CleanupDead(playerIndex))
                    ResolveDaemonDestroyed(playerIndex, defeated);
            }
            CheckWinConditions();
        }

        // ─── Play Relic ──────────────────────────────────────────────
        private bool HandlePlayMask(PlayerState player, int handIndex, int targetIndex)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;
            if (targetIndex < 0 || targetIndex >= player.Field.Count)
                return false;
            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not MaskCardData mask)
                return false;
            if (IsDomainEffectActiveAgainst(State.CurrentPlayer, DomainEffectType.RelicLock))
            {
                AddLog($"{State.ActiveDomain.Card.cardName} locks relics. {player.Name} cannot equip Relics right now.", LogEntryType.System);
                return false;
            }
            var targetDaemon = player.Field[targetIndex];
            if (mask.isAscensionRelic && !CanAscensionRelicFindSecondForm(player, targetDaemon))
            {
                AddLog($"{mask.cardName} needs that daemon's Second Form in your deck.", LogEntryType.System);
                return false;
            }
            int cost = mask.GetWillCost();
            if (!TrySpendWill(player, mask, cost))
                return false;
            player.Hand.RemoveAt(handIndex);
            CommitElementForCard(player, mask);
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.CardPlayed, mask,
                $"{player.Name} equips relic {mask.cardName}.");

            if (_effects.OnSpellCast(State.CurrentPlayer))
            {
                player.AshePile.Add(cardInstance);
                AddLog($"{mask.cardName} was negated by a Seal!", LogEntryType.Effect);
                OnStateChanged?.Invoke(State);
                return true;
            }

            player.Field[targetIndex].Masks.Add(new MaskInstance
            {
                Card = mask,
                TurnsRemaining = mask.duration,
            });
            AddLog("Relic equipped.", LogEntryType.Action);
            AddLog($"Relic: {DescribeMaskEffect(mask)} on {player.Field[targetIndex].Card.cardName}.", LogEntryType.Effect);

            // Apply mask effect immediately
            if (mask.effectType == MaskEffectType.Haste)
                targetDaemon.CanAttack = true;
            _effects.ApplyMaskEffect(State.CurrentPlayer, targetDaemon, mask);
            if (mask.isAscensionRelic)
                SearchSecondFormWithAscensionRelic(player, targetDaemon, mask);
            if (mask.summonsLegendaryDaemon)
                SummonLegendaryDaemonWithRelic(player, mask);

            OnStateChanged?.Invoke(State);
            return true;
        }

        private static bool CanAscensionRelicFindSecondForm(PlayerState player, DaemonInstance daemon)
        {
            DaemonCardData secondForm = daemon?.Card?.evolvesTo;
            if (player?.Deck == null || secondForm == null)
                return false;

            return player.Deck.Any(card => card?.Card == secondForm);
        }

        private void SummonLegendaryDaemonWithRelic(PlayerState player, MaskCardData relic)
        {
            var legendary = relic?.legendaryDaemonToSummon;
            if (player == null || legendary == null)
                return;

            if (player.Field.Count >= GameConstants.MaxFieldDaemons)
            {
                AddLog($"{relic.cardName} calls {legendary.cardName}, but your field is full.", LogEntryType.Effect);
                return;
            }

            int handIndex = player.Hand.FindIndex(card => card?.Card == legendary);
            if (handIndex >= 0)
            {
                var found = player.Hand[handIndex];
                player.Hand.RemoveAt(handIndex);
                SummonRelicDaemon(player, found, relic);
                return;
            }

            int deckIndex = player.Deck.FindIndex(card => card?.Card == legendary);
            if (deckIndex < 0)
            {
                AddLog($"{relic.cardName} finds no {legendary.cardName} in hand or deck.", LogEntryType.Effect);
                return;
            }

            var deckCard = player.Deck[deckIndex];
            player.Deck.RemoveAt(deckIndex);
            SummonRelicDaemon(player, deckCard, relic);
            ShuffleDeck(player);
        }

        private void SummonRelicDaemon(PlayerState player, CardInstance cardInstance, MaskCardData relic)
        {
            if (player == null || cardInstance?.Card is not DaemonCardData daemon)
                return;

            int summonAshe = daemon.ashe;
            if (daemon.creatureType == CreatureType.Undead)
            {
                summonAshe += GameConstants.UndeadSpawnAsheBonus;
                AddLog($"{daemon.cardName} rises with +{GameConstants.UndeadSpawnAsheBonus} Life, but will decay each turn.", LogEntryType.Effect);
            }

            var instance = new DaemonInstance
            {
                InstanceId = cardInstance.InstanceId,
                Card = daemon,
                BaseAttack = daemon.attack,
                BaseAshe = summonAshe,
                CurrentAshe = summonAshe,
                MaxAshe = summonAshe,
                Attack = daemon.attack,
                AsheCost = daemon.asheCost,
                LaneIndex = ResolveSummonLane(player, -1),
                CanAttack = true,
                HasAttacked = false,
            };

            player.Field.Add(instance);
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.CardPlayed, daemon,
                $"{relic.cardName} summons {daemon.cardName}.");
            AddLog($"{relic.cardName} summons legendary daemon {daemon.cardName}!", LogEntryType.Action);
            ResolveDevourOnSummon(player, instance);
            _effects.OnDaemonSummoned(State.CurrentPlayer, instance);
            ApplyLegendarySummonPayoff(player, instance);
            _triggers.Fire(GameTrigger.OnDaemonSummoned, new TriggerEvent
            {
                State = State, SourcePlayer = State.CurrentPlayer, Daemon = instance,
            });

            var deadOpponent = _effects.CleanupDead(1 - State.CurrentPlayer);
            foreach (var dead in deadOpponent)
                ResolveDaemonDestroyed(1 - State.CurrentPlayer, dead);
            CheckWinConditions();
        }

        private void ApplyLegendarySummonPayoff(PlayerState player, DaemonInstance instance)
        {
            if (player == null || instance?.Card == null)
                return;

            var opponent = State.Players[1 - State.CurrentPlayer];
            switch (instance.Card.cardId)
            {
                case "lg-mach-iron-seraph":
                    player.Will = Math.Min(GameConstants.MaxWill, player.Will + 2);
                    player.MaxWill = Math.Min(GameConstants.MaxWill, Math.Max(player.MaxWill, player.Will));
                    var machine = player.Field
                        .Where(d => d != instance && d.Card != null && d.Card.creatureType == CreatureType.Machine)
                        .OrderBy(d => d.Attack)
                        .FirstOrDefault();
                    if (machine != null)
                        machine.CanAttack = true;
                    AddLog($"{instance.Card.cardName} overclocks the Machine line (+2 SE).", LogEntryType.Effect);
                    break;

                case "lg-spirit-eidolon":
                    player.Invoker.Hp = Math.Min(player.Invoker.MaxHp, player.Invoker.Hp + 3);
                    var wounded = player.Field
                        .Where(d => d.CurrentAshe < d.MaxAshe)
                        .OrderBy(d => d.CurrentAshe)
                        .FirstOrDefault();
                    if (wounded != null)
                        wounded.CurrentAshe = Math.Min(wounded.MaxAshe, wounded.CurrentAshe + 3);
                    AddLog($"{instance.Card.cardName} restores 3 Life to your Invoker and a wounded ally.", LogEntryType.Effect);
                    break;

                case "lg-arti-prime-angel":
                    foreach (var ally in player.Field)
                        ally.ShieldAmount += 2;
                    AddLog($"{instance.Card.cardName} gives each allied daemon Shield 2.", LogEntryType.Effect);
                    break;

                case "lg-undead-necrorex":
                    int fallen = Math.Min(4, player.AshePile.Count(card => card?.Card is DaemonCardData));
                    if (fallen > 0)
                    {
                        opponent.Invoker.Hp = Math.Max(0, opponent.Invoker.Hp - fallen);
                        player.Invoker.Hp = Math.Min(player.Invoker.MaxHp, player.Invoker.Hp + fallen);
                    }
                    AddLog($"{instance.Card.cardName} drains {fallen} from the enemy Invoker for fallen daemons.", LogEntryType.Effect);
                    break;

                case "lg-elem-worldheart":
                    int elements = player.ElementCommitments.Count(value => value > 0);
                    int boost = Math.Min(3, Math.Max(1, elements));
                    instance.Attack += boost;
                    instance.CurrentAshe = Math.Min(instance.MaxAshe + boost, instance.CurrentAshe + boost);
                    instance.MaxAshe += boost;
                    AddLog($"{instance.Card.cardName} gains +{boost} ATK and Life from committed elements.", LogEntryType.Effect);
                    break;
            }
        }

        private void SearchSecondFormWithAscensionRelic(PlayerState player, DaemonInstance daemon, MaskCardData relic)
        {
            if (player == null || daemon?.Card?.evolvesTo == null)
                return;

            DaemonCardData secondForm = daemon.Card.evolvesTo;
            int deckIndex = player.Deck.FindIndex(card => card?.Card == secondForm);
            if (deckIndex < 0)
            {
                AddLog($"{relic.cardName} finds no Second Form for {daemon.Card.cardName} in the deck.", LogEntryType.Effect);
                return;
            }

            var found = player.Deck[deckIndex];
            player.Deck.RemoveAt(deckIndex);
            if (player.Hand.Count < GameConstants.MaxHandSize)
            {
                player.Hand.Add(found);
                AddLog($"{relic.cardName} reveals {secondForm.cardName} from the deck.", LogEntryType.Effect);
            }
            else
            {
                player.Deck.Add(found);
                AddLog($"{relic.cardName} found {secondForm.cardName}, but your hand is full.", LogEntryType.Effect);
            }
            ShuffleDeck(player);
        }

        // ─── Set Seal ────────────────────────────────────────────────
        private bool HandleSetSeal(PlayerState player, int handIndex)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;
            if (player.SealZone.Count >= GameConstants.MaxSeals)
                return false;
            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not SealCardData seal)
                return false;
            int cost = seal.GetWillCost();
            if (!TrySpendWill(player, seal, cost))
                return false;
            player.Hand.RemoveAt(handIndex);
            CommitElementForCard(player, seal);
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.CardPlayed, seal,
                $"{player.Name} sets {seal.cardName}.");

            if (_effects.OnSpellCast(State.CurrentPlayer))
            {
                player.AshePile.Add(cardInstance);
                AddLog("A counter-seal snapped and negated the set.", LogEntryType.Effect);
                OnStateChanged?.Invoke(State);
                return true;
            }

            player.SealZone.Add(new SealInstance
            {
                InstanceId = cardInstance.InstanceId,
                Card = seal,
            });
            AddLog($"{player.Name} set a Seal face-down.", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Play Dispel ─────────────────────────────────────────────
        private bool HandlePlayHex(PlayerState player, int playerIndex, int handIndex, int targetDaemonIndex, int responseDispelHandIndex)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;

            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not HexCardData hex)
                return false;

            if (HexRequiresEnemyDaemonTarget(hex))
            {
                var enemyField = State.Players[1 - playerIndex].Field;
                if (enemyField.Count <= 0)
                    return false;

                // Human UI sends the chosen index. AI and older network clients
                // fall back to the strongest legal target so the turn cannot stall.
                if (targetDaemonIndex < 0 || targetDaemonIndex >= enemyField.Count)
                {
                    targetDaemonIndex = enemyField
                        .Select((daemon, index) => (daemon, index))
                        .OrderByDescending(entry => entry.daemon.CurrentAshe + entry.daemon.Attack)
                        .Select(entry => entry.index)
                        .FirstOrDefault();
                }
            }

            int cost = hex.GetWillCost();
            if (!TrySpendWill(player, hex, cost))
                return false;

            player.Hand.RemoveAt(handIndex);
            player.AshePile.Add(cardInstance);
            CommitElementForCard(player, hex);
            AddRitualStep(playerIndex, RitualChainActionType.CardPlayed, hex,
                $"{player.Name} casts hex {hex.cardName}.");

            if (TryConsumeSpellResponseDispel(State.Players[1 - playerIndex], playerIndex,
                    responseDispelHandIndex, hex, SpellResponseWindow.Hex) != null)
            {
                AddLog($"{hex.cardName} is fully negated.", LogEntryType.Effect);
                OnStateChanged?.Invoke(State);
                return true;
            }

            if (_effects.OnSpellCast(playerIndex))
            {
                AddLog($"{hex.cardName} was negated by a Seal!", LogEntryType.Effect);
                OnStateChanged?.Invoke(State);
                return true;
            }

            bool resolved = _effects.ResolveOneShotEffect(playerIndex, hex, hex.effectKey, targetDaemonIndex);
            if (resolved)
            {
                var enemyField = State.Players[1 - playerIndex].Field;
                DaemonInstance target = targetDaemonIndex >= 0 && targetDaemonIndex < enemyField.Count
                    ? enemyField[targetDaemonIndex]
                    : null;
                OnHexResolved?.Invoke(new HexResolution
                {
                    CasterPlayer = playerIndex,
                    TargetPlayer = 1 - playerIndex,
                    TargetIndex = targetDaemonIndex,
                    TargetInstanceId = target?.InstanceId,
                    TargetName = target?.Card?.cardName,
                    HexCardId = hex.cardId,
                    HexName = hex.cardName,
                    EffectKey = hex.effectKey,
                    EffectElement = hex.effectElement,
                });
            }
            AddLog(resolved
                ? $"{player.Name} cast hex {hex.cardName}: {DescribeHexEffect(hex)}"
                : $"{hex.cardName} fizzled — no hex effect was configured.", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        private static bool HexRequiresEnemyDaemonTarget(HexCardData hex)
        {
            if (hex == null)
                return false;

            string key = hex.effectKey?.Trim().ToLowerInvariant() ?? string.Empty;
            string[] targetedPrefixes =
            {
                "corrupt", "marked", "mark", "fracture", "haunt", "tax", "sunder",
                "silence", "freeze-target", "entangle", "poison", "burn", "destroy",
                "drain", "debuff-atk",
            };
            return targetedPrefixes.Any(key.StartsWith);
        }

        private static string DescribeHexEffect(HexCardData hex)
        {
            string key = hex?.effectKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                return "no effect";

            string lower = key.ToLowerInvariant();
            if (lower.Contains("damage-invoker")) return "enemy Invoker loses Life";
            if (lower.Contains("freeze")) return "enemy daemon freezes";
            if (lower.Contains("burn")) return "burn pressure spreads";
            if (lower.Contains("poison")) return "poison takes hold";
            if (lower.Contains("marked")) return "target is marked";
            if (lower.Contains("haunted")) return "haunting begins";
            if (lower.Contains("taxed")) return "attacks cost more SE";
            if (lower.Contains("fractured")) return "defenses fracture";
            if (lower.Contains("overloaded")) return "power overloads";
            if (lower.Contains("corrupted")) return "corruption spreads";
            if (lower.Contains("sundered")) return "relic patterns break";
            if (lower.Contains("revive") || lower.Contains("void")) return "the Void stirs";
            return key.Replace('-', ' ');
        }

        // ─── Play Dispel ─────────────────────────────────────────────
        private bool HandlePlayDispel(PlayerState player, PlayerState opponent, int playerIndex, PlayDispelAction action)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (action.HandIndex < 0 || action.HandIndex >= player.Hand.Count)
                return false;
            var cardInstance = player.Hand[action.HandIndex];
            if (cardInstance.Card is not DispelCardData dispel)
                return false;
            int cost = dispel.GetWillCost();
            if (!TrySpendWill(player, dispel, cost))
                return false;
            player.Hand.RemoveAt(action.HandIndex);
            player.AshePile.Add(cardInstance);
            CommitElementForCard(player, dispel);
            AddRitualStep(playerIndex, RitualChainActionType.DispelResponse, dispel,
                $"{player.Name} casts {dispel.cardName}.");

            if (_effects.OnSpellCast(playerIndex))
            {
                AddLog($"{dispel.cardName} was negated by a Seal!", LogEntryType.Effect);
                OnStateChanged?.Invoke(State);
                return true;
            }

            AddLog($"{player.Name} cast {dispel.cardName}!", LogEntryType.Action);

            // Resolve dispel: remove matching domain/mask/seal
            switch (dispel.target)
            {
                case DispelTarget.Domain:
                    if (State.ActiveDomain != null)
                    {
                        AddLog($"Dispelled {State.ActiveDomain.Card.cardName}!", LogEntryType.Effect);
                        State.ActiveDomain = null;
                    }
                    break;
                case DispelTarget.Seal:
                    if (opponent.SealZone.Count > 0 && action.TargetIndex >= 0
                        && action.TargetIndex < opponent.SealZone.Count)
                    {
                        var seal = opponent.SealZone[action.TargetIndex];
                        AddLog($"Dispelled {seal.Card.cardName}!", LogEntryType.Effect);
                        opponent.SealZone.RemoveAt(action.TargetIndex);
                        opponent.AshePile.Add(new CardInstance { InstanceId = seal.InstanceId, Card = seal.Card });
                    }
                    break;
                case DispelTarget.Mask:
                    if (action.TargetIndex >= 0 && action.TargetIndex < opponent.Field.Count)
                    {
                        var target = opponent.Field[action.TargetIndex];
                        if (target.Masks.Count > 0)
                        {
                            var mask = target.Masks[0];
                            AddLog($"Dispelled {mask.Card.cardName}!", LogEntryType.Effect);
                            target.Masks.RemoveAt(0);
                        }
                    }
                    break;
                case DispelTarget.Any:
                    // Remove the first thing found: domain > seal > mask
                    if (State.ActiveDomain != null)
                    {
                        AddLog($"Dispelled {State.ActiveDomain.Card.cardName}!", LogEntryType.Effect);
                        State.ActiveDomain = null;
                    }
                    else if (opponent.SealZone.Count > 0)
                    {
                        var seal = opponent.SealZone[0];
                        AddLog($"Dispelled {seal.Card.cardName}!", LogEntryType.Effect);
                        opponent.SealZone.RemoveAt(0);
                        opponent.AshePile.Add(new CardInstance { InstanceId = seal.InstanceId, Card = seal.Card });
                    }
                    break;
            }

            ResolveDispelCounterEffect(player, opponent, dispel);

            OnStateChanged?.Invoke(State);
            return true;
        }

        private void ResolveDispelCounterEffect(PlayerState caster, PlayerState opposingPlayer, DispelCardData dispel)
        {
            if (caster == null || opposingPlayer == null || dispel?.counterEffect == null || string.IsNullOrEmpty(dispel.counterEffect.effectType))
                return;

            switch (dispel.counterEffect.effectType.ToLowerInvariant())
            {
                case "damage-owner":
                    opposingPlayer.Invoker.Hp -= dispel.counterEffect.value;
                    AddLog($"Counter effect: {dispel.counterEffect.value} damage!", LogEntryType.Effect);
                    break;
                case "draw-cards":
                    for (int i = 0; i < dispel.counterEffect.value; i++)
                        DrawCard(caster);
                    AddLog($"Counter effect: draw {dispel.counterEffect.value}!", LogEntryType.Effect);
                    break;
                case "heal-invoker":
                    caster.Invoker.Hp = Math.Min(
                        caster.Invoker.Hp + dispel.counterEffect.value, caster.Invoker.MaxHp);
                    AddLog($"Counter effect: heal {dispel.counterEffect.value}!", LogEntryType.Effect);
                    break;
                case "weaken-all":
                    foreach (DaemonInstance daemon in opposingPlayer.Field)
                    {
                        daemon.Modifiers.Add(StatModifier.FlatAttack(-Math.Max(1, dispel.counterEffect.value), dispel.cardName, 2));
                        daemon.RecalculateStats();
                    }
                    AddLog($"Counter effect: enemy Daemons lose {Math.Max(1, dispel.counterEffect.value)} Attack for 2 turns!", LogEntryType.Effect);
                    break;
                case "shatter-aoe":
                    {
                        int damage = Math.Max(1, dispel.counterEffect.value);
                        foreach (DaemonInstance daemon in opposingPlayer.Field)
                            daemon.CurrentAshe = Math.Max(0, daemon.CurrentAshe - damage);
                        int opponentIndex = ReferenceEquals(State.Players[0], opposingPlayer) ? 0 : 1;
                        foreach (DaemonInstance dead in _effects.CleanupDead(opponentIndex))
                            ResolveDaemonDestroyed(opponentIndex, dead);
                        AddLog($"Counter effect: shrapnel deals {damage} damage to every enemy Daemon!", LogEntryType.Effect);
                        CheckWinConditions();
                    }
                    break;
                case "drain-will":
                    {
                        int drained = Math.Min(opposingPlayer.Will, Math.Max(1, dispel.counterEffect.value));
                        opposingPlayer.Will -= drained;
                        AddLog($"Counter effect: drain {drained} SE!", LogEntryType.Effect);
                    }
                    break;
                case "reclaim":
                    {
                        int reclaimed = 0;
                        int wanted = Math.Max(1, dispel.counterEffect.value);
                        while (reclaimed < wanted && caster.Hand.Count < GameConstants.MaxHandSize)
                        {
                            int recoverIndex = caster.AshePile.FindLastIndex(card => card?.Card != null
                                && card.Card.category != CardCategory.Dispel);
                            if (recoverIndex < 0)
                                break;
                            CardInstance recovered = caster.AshePile[recoverIndex];
                            caster.AshePile.RemoveAt(recoverIndex);
                            caster.Hand.Add(recovered);
                            reclaimed++;
                        }
                        AddLog($"Counter effect: reclaim {reclaimed} card{(reclaimed == 1 ? "" : "s")} from the Void!", LogEntryType.Effect);
                    }
                    break;
            }
        }

        // ─── Attack ────────────────────────────────────────────────
        // ─── Phase Transition: Main → Combat ─────────────────────
        private bool HandleNextPhase()
        {
            if (State.Phase == GamePhase.Main)
            {
                State.Phase = GamePhase.Combat;
                AddLog("Combat phase!", LogEntryType.System);
                OnStateChanged?.Invoke(State);
                return true;
            }
            return false;
        }

        private bool HandleAttack(PlayerState player, PlayerState opponent, int playerIndex, AttackAction action)
        {
            if (State.Phase != GamePhase.Combat)
                return false;
            if (action.AttackerIndex < 0 || action.AttackerIndex >= player.Field.Count)
                return false;
            var attacker = player.Field[action.AttackerIndex];
            if (attacker.IsBindAnchor || !attacker.CanAttack || attacker.HasAttacked || attacker.Frozen || attacker.Entangled || attacker.DeathsDoor)
                return false;
            if (action.Target != TargetType.Daemon && action.Target != TargetType.Invoker)
                return false;
            if (action.Target == TargetType.Daemon
                && (action.TargetIndex < 0 || action.TargetIndex >= opponent.Field.Count))
                return false;
            int attackSpiritCost = 0;
            AddRitualStep(playerIndex, RitualChainActionType.AttackDeclared, attacker.Card,
                $"{attacker.Card.cardName} declares an attack.");

            if (ShouldMachineGlitchMiss(playerIndex, attacker))
            {
                AddLog($"{attacker.Card.cardName} glitches under {State.ActiveDomain.Card.cardName} and deals no damage!", LogEntryType.Combat);
                attacker.HasAttacked = true;
                if (!State.GameOver)
                {
                    CheckWinConditions();
                    _combatAutoEnd = !player.Field.Any(d => d.CanAttack && !d.HasAttacked && !d.Frozen && !d.Entangled && !d.DeathsDoor);
                }
                OnStateChanged?.Invoke(State);
                return true;
            }

            // ─── Burn miss chance (poketcg: 50% miss when burned) ───
            if (attacker.Burning && _rng.Next(2) == 0)
            {
                AddLog($"{attacker.Card.cardName} is burned and failed to attack!", LogEntryType.Combat);
                attacker.HasAttacked = true;
                if (!State.GameOver)
                {
                    CheckWinConditions();
                    _combatAutoEnd = !player.Field.Any(d => d.CanAttack && !d.HasAttacked && !d.Frozen && !d.Entangled && !d.DeathsDoor);
                }
                OnStateChanged?.Invoke(State);
                return true;
            }

            // Fire OnAttack effects and check seals
            _effects.OnDaemonAttacking(playerIndex, attacker);
            _triggers.Fire(GameTrigger.OnDaemonAttacking, new TriggerEvent
            {
                State = State, SourcePlayer = playerIndex, Daemon = attacker,
            });
            if (attacker.CurrentAshe <= 0)
            {
                AddLog($"{attacker.Card.cardName} is shattered by a Seal before the attack lands!", LogEntryType.Effect);
                attacker.HasAttacked = true;
                player.Field.Remove(attacker);
                player.AshePile.Add(new CardInstance { InstanceId = attacker.InstanceId, Card = attacker.Card });
                ResolveDaemonDestroyed(playerIndex, attacker);
                CheckWinConditions();
                if (!State.GameOver)
                    _combatAutoEnd = !player.Field.Any(d => d.CanAttack && !d.HasAttacked && !d.Frozen && !d.Entangled && !d.DeathsDoor);
                OnStateChanged?.Invoke(State);
                return true;
            }
            if (_effects.ActionNegated)
            {
                AddLog("Attack was negated by a Seal!", LogEntryType.Effect);
                attacker.HasAttacked = true;
                if (!State.GameOver)
                {
                    CheckWinConditions();
                    _combatAutoEnd = !player.Field.Any(d => d.CanAttack && !d.HasAttacked && !d.Frozen && !d.Entangled && !d.DeathsDoor);
                }
                OnStateChanged?.Invoke(State);
                return true;
            }

            // ─── Taunt enforcement (poketcg/forge: must attack taunter) ───
            if (action.Target == TargetType.Daemon)
            {
                var taunters = opponent.Field
                    .Select((d, i) => (d, i))
                    .Where(x => x.d.HasTaunt && !x.d.Stealthed
                        && IsTacticsDaemonTargetReachable(attacker, action.AttackerIndex, x.d, x.i))
                    .ToList();
                if (taunters.Count > 0)
                {
                    bool targetingTaunter = taunters.Any(t => t.i == action.TargetIndex);
                    if (!targetingTaunter)
                    {
                        AddLog("Must attack a daemon with Taunt!", LogEntryType.System);
                        return false;
                    }
                }
            }

            // ─── Stealth targeting immunity (poketcg: can't target stealthed) ───
            if (action.Target == TargetType.Daemon
                && action.TargetIndex >= 0 && action.TargetIndex < opponent.Field.Count
                && opponent.Field[action.TargetIndex].Stealthed)
            {
                AddLog("Cannot target a stealthed daemon!", LogEntryType.System);
                return false;
            }

            // ─── Enforce attack order: Daemons block Invoker access ───
            bool opponentHasDaemons = opponent.Field.Any(d => !d.Stealthed);
            if (action.Target == TargetType.Pillar)
            {
                AddLog("Pillars are not combat targets in story-card battles.", LogEntryType.System);
                return false;
            }
            if (action.Target == TargetType.Invoker && opponentHasDaemons && !CanTacticsBoardAttackInvokerLane(opponent, attacker.LaneIndex >= 0 ? attacker.LaneIndex : action.AttackerIndex))
            {
                AddLog("Cannot attack the Invoker yet. Clear the forward lane or nearby Guard first.", LogEntryType.System);
                return false;
            }

            bool tacticsBoardRangedShot = false;
            if (!ValidateTacticsBoardAttack(player, opponent, attacker, action, out tacticsBoardRangedShot))
                return false;

            if (!TrySpendAttackSpiritEnergy(player, attacker, out attackSpiritCost))
            {
                AddLog($"{attacker.Card.cardName} needs {GetAttackSpiritCost(player, attacker)} SE to attack.", LogEntryType.System);
                return false;
            }

            int rangedAccuracyRoll = 0;
            if (tacticsBoardRangedShot)
            {
                int roll = _rng.Next(1, GameConstants.TacticsBoardRangedAccuracySides + 1);
                rangedAccuracyRoll = roll;
                bool hit = roll >= GameConstants.TacticsBoardRangedAccuracySuccessMin;
                AddLog(hit
                    ? $"{attacker.Card.cardName} lands a ranged shot across the board (rolled {roll}, needs {GameConstants.TacticsBoardRangedAccuracySuccessMin}+)."
                    : $"{attacker.Card.cardName}'s ranged shot misses across the board (rolled {roll}, needs {GameConstants.TacticsBoardRangedAccuracySuccessMin}+).",
                    LogEntryType.Combat);
                if (!hit)
                {
                    var missedTarget = action.TargetIndex >= 0 && action.TargetIndex < opponent.Field.Count
                        ? opponent.Field[action.TargetIndex]
                        : null;
                    OnCombatResolved?.Invoke(new CombatResolution
                    {
                        AttackerPlayer = playerIndex,
                        TargetPlayer = 1 - playerIndex,
                        AttackerIndex = action.AttackerIndex,
                        TargetIndex = action.TargetIndex,
                        AttackerInstanceId = attacker.InstanceId,
                        AttackerName = attacker.Card.cardName,
                        AttackerElement = attacker.Card.element,
                        AttackerCreatureType = attacker.Card.creatureType,
                        AttackerRarity = attacker.Card.rarity,
                        TargetType = action.Target,
                        TargetInstanceId = missedTarget?.InstanceId,
                        TargetName = missedTarget?.Card?.cardName,
                        TargetElement = missedTarget?.Card?.element ?? Element.Light,
                        TargetCreatureType = missedTarget?.Card?.creatureType ?? CreatureType.Elemental,
                        TargetRarity = missedTarget?.Card?.rarity ?? Rarity.Common,
                        AttackMissed = true,
                        AccuracyRoll = roll,
                        AccuracyThreshold = GameConstants.TacticsBoardRangedAccuracySuccessMin,
                        AttackPattern = GetEffectiveAttackPattern(attacker),
                    });
                    attacker.HasAttacked = true;
                    if (!State.GameOver)
                    {
                        CheckWinConditions();
                        _combatAutoEnd = !player.Field.Any(d => d.CanAttack && !d.HasAttacked && !d.Frozen && !d.Entangled && !d.DeathsDoor);
                    }
                    OnStateChanged?.Invoke(State);
                    return true;
                }
            }

            int baseDamage = attacker.Attack;
            if (attacker.Overloaded)
            {
                int bonus = Math.Max(1, attacker.OverloadAttackBonus);
                baseDamage += bonus;
                AddLog($"{attacker.Card.cardName} is Overloaded: +{bonus} attack power.", LogEntryType.Effect);
            }
            if (HasElementCommitment(player, attacker.Card.element))
            {
                baseDamage += GameConstants.ElementCommitmentDamageBonus;
                AddLog($"{attacker.Card.element} commitment adds +{GameConstants.ElementCommitmentDamageBonus} attack power.", LogEntryType.Effect);
            }
            if (HasKeyword(attacker.Card, Keyword.Resonance) && ControlsMatchingElementSupport(player, attacker.Card.element))
            {
                baseDamage += GameConstants.ElementCommitmentDamageBonus;
                AddLog($"{attacker.Card.cardName} resonates with your {attacker.Card.element} support: +{GameConstants.ElementCommitmentDamageBonus} attack power.", LogEntryType.Effect);
            }

            // ─── Next Attack Double (poketcg: Swords Dance) ───
            if (attacker.NextAttackDouble)
            {
                baseDamage *= 2;
                attacker.NextAttackDouble = false;
                AddLog($"{attacker.Card.cardName} unleashes a powered-up attack!", LogEntryType.Combat);
            }

            bool guardIntercepted = TryRedirectToAdjacentGuard(opponent, attacker, action, out string guardName);
            var combat = new CombatResolution
            {
                AttackerPlayer = playerIndex,
                TargetPlayer = 1 - playerIndex,
                AttackerIndex = action.AttackerIndex,
                TargetIndex = action.TargetIndex,
                AttackerInstanceId = attacker.InstanceId,
                AttackerName = attacker.Card.cardName,
                AttackerElement = attacker.Card.element,
                AttackerCreatureType = attacker.Card.creatureType,
                AttackerRarity = attacker.Card.rarity,
                TargetType = action.Target,
                AttackPattern = GetEffectiveAttackPattern(attacker),
                GuardIntercepted = guardIntercepted,
                GuardName = guardName,
                AccuracyRoll = rangedAccuracyRoll,
                AccuracyThreshold = tacticsBoardRangedShot ? GameConstants.TacticsBoardRangedAccuracySuccessMin : 0,
            };

            DispelCardData attackResponse = TryConsumeAttackResponseDispel(opponent, playerIndex, action.ResponseDispelHandIndex, attacker.Card, combat);

            switch (action.Target)
            {
                case TargetType.Daemon:
                    if (action.TargetIndex < 0 || action.TargetIndex >= opponent.Field.Count)
                        return false;
                    var targetDaemon = opponent.Field[action.TargetIndex];
                    int targetLaneBeforeCombat = targetDaemon.LaneIndex >= 0 ? targetDaemon.LaneIndex : action.TargetIndex;
                    combat.TargetInstanceId = targetDaemon.InstanceId;
                    combat.TargetName = targetDaemon.Card.cardName;
                    combat.TargetElement = targetDaemon.Card.element;
                    combat.TargetCreatureType = targetDaemon.Card.creatureType;
                    combat.TargetRarity = targetDaemon.Card.rarity;

                    // Element, creature, and domain-weather matchups
                    bool typeNull = IsTypeNullDomainActive();
                    float elemMult = typeNull
                        ? GameConstants.NeutralMult
                        : ResolveDefiledElementMultiplier(attacker, targetDaemon);
                    float creatMult = ElementSystem.GetCreatureMatchup(attacker.Card.creatureType, targetDaemon.Card.creatureType);
                    float weatherMult = typeNull ? GameConstants.NeutralMult : ResolveDomainWeatherMultiplier(attacker, targetDaemon, out _);
                    if (typeNull)
                        AddLog($"{State.ActiveDomain.Card.cardName} blanks elemental advantage.", LogEntryType.Effect);
                    float totalMatchupMult = elemMult * creatMult * weatherMult;
                    combat.WasCritical = totalMatchupMult > GameConstants.NeutralMult + 0.05f;
                    combat.WasWeak = totalMatchupMult < GameConstants.NeutralMult - 0.05f;
                    int finalDamage = (int)Math.Round(baseDamage * elemMult * creatMult * weatherMult);
                    if (targetDaemon.Marked)
                    {
                        int bonus = Math.Max(1, targetDaemon.MarkedBonusDamage);
                        finalDamage += bonus;
                        targetDaemon.Marked = false;
                        targetDaemon.MarkedTurns = 0;
                        AddLog($"{targetDaemon.Card.cardName}'s Mark breaks: +{bonus} damage.", LogEntryType.Effect);
                    }
                    finalDamage = ApplyAttackResponsePrevention(finalDamage, attackResponse, combat);

                    // ─── Damage reduction pipeline (poketcg: Defender -20, Reduce substatus) ───
                    bool fractured = targetDaemon.Fractured;
                    bool sundered = targetDaemon.Sundered;
                    if (fractured)
                        AddLog($"{targetDaemon.Card.cardName} is Fractured: shields and damage reduction fail.", LogEntryType.Effect);
                    if (targetDaemon.DamageReduction > 0 && !fractured)
                    {
                        int reduced = Math.Min(finalDamage, targetDaemon.DamageReduction);
                        finalDamage -= reduced;
                        if (reduced > 0)
                            AddLog($"Damage reduced by {reduced}!", LogEntryType.Effect);
                    }

                    // Force Field (Artificial ashe card): intercepts damage before other shields
                    var forceField = opponent.AsheCards?.Find(a =>
                        a.AssignedDaemonInstanceId == targetDaemon.InstanceId &&
                        a.Card?.GetAffinityType() == CreatureType.Artificial &&
                        a.ShieldRemaining > 0);
                    if (forceField != null && finalDamage > 0 && !fractured)
                    {
                        int ffAbsorb = Math.Min(finalDamage, forceField.ShieldRemaining);
                        forceField.ShieldRemaining -= ffAbsorb;
                        finalDamage -= ffAbsorb;
                        if (ffAbsorb > 0)
                            AddLog($"{forceField.Card.cardName} absorbs {ffAbsorb} damage!", LogEntryType.Effect);
                        if (forceField.ShieldRemaining <= 0)
                        {
                            opponent.AsheCards.Remove(forceField);
                            opponent.AshePile.Add(new CardInstance { InstanceId = forceField.InstanceId, Card = forceField.Card });
                            AddLog($"{forceField.Card.cardName} shatters!", LogEntryType.Effect);
                        }
                    }

                    // Apply shield absorption
                    if (targetDaemon.ShieldAmount > 0 && !fractured)
                    {
                        int absorbed = Math.Min(finalDamage, targetDaemon.ShieldAmount);
                        targetDaemon.ShieldAmount -= absorbed;
                        finalDamage -= absorbed;
                        if (absorbed > 0)
                            AddLog($"Shield absorbs {absorbed} damage!", LogEntryType.Effect);
                    }

                    if (sundered && targetDaemon.Masks.Count > 0)
                        AddLog($"{targetDaemon.Card.cardName}'s Relics are Sundered and cannot protect it.", LogEntryType.Effect);

                    targetDaemon.CurrentAshe -= finalDamage;
                    combat.Damage = finalDamage;
                    string matchupLabel = combat.WasCritical
                        ? " Super effective."
                        : combat.WasWeak
                            ? " Resisted."
                            : string.Empty;
                    AddLog($"{attacker.Card.cardName} attacks {targetDaemon.Card.cardName} for {finalDamage}!{matchupLabel}", LogEntryType.Combat);

                    if (finalDamage > 0 && targetDaemon.CurrentAshe <= 0)
                    {
                        ResolveMachineHarvest(player, attacker, targetDaemon.Card.cardName);
                    }

                    // Thorns: reflect damage back to attacker
                    if (targetDaemon.ThornsDamage > 0)
                    {
                        attacker.CurrentAshe -= targetDaemon.ThornsDamage;
                        AddLog($"Thorns reflect {targetDaemon.ThornsDamage} damage!", LogEntryType.Effect);
                    }

                    // Fire OnDamaged effects
                    if (finalDamage > 0)
                        _effects.OnDaemonDamaged(1 - playerIndex, targetDaemon, finalDamage);

                    if (GameConstants.ArtificialReactiveDrainAmount > 0
                        && finalDamage > 0
                        && targetDaemon.CurrentAshe > 0
                        && targetDaemon.Card.creatureType == CreatureType.Artificial)
                    {
                        int drained = Math.Min(GameConstants.ArtificialReactiveDrainAmount, Math.Max(0, attacker.CurrentAshe));
                        if (drained > 0)
                        {
                            attacker.CurrentAshe -= drained;
                            targetDaemon.CurrentAshe = Math.Min(targetDaemon.MaxAshe, targetDaemon.CurrentAshe + drained);
                            AddLog($"{targetDaemon.Card.cardName} drains {drained} Life back on hit!", LogEntryType.Effect);
                        }
                    }

                    // Stealth breaks on taking damage (poketcg: invisibility breaks on hit)
                    if (finalDamage > 0 && targetDaemon.Stealthed)
                    {
                        targetDaemon.Stealthed = false;
                        targetDaemon.StealthTurns = 0;
                        AddLog($"{targetDaemon.Card.cardName} is revealed!", LogEntryType.Effect);
                    }

                    if (targetDaemon.CurrentAshe <= 0)
                    {
                        // Undead Last Rite: resurrection before normal destruction
                        bool resurrected = false;
                        if (targetDaemon.Card.creatureType == CreatureType.Undead)
                        {
                            var lastRite = targetDaemon.Masks?.FirstOrDefault(mask =>
                                string.Equals(mask?.Card?.cardId, "aw-undead-last-rite", StringComparison.OrdinalIgnoreCase));
                            if (lastRite != null)
                            {
                                int reviveHp = Math.Min(targetDaemon.MaxAshe, GameConstants.UndeadLastRiteReviveLife);
                                targetDaemon.CurrentAshe = Math.Max(1, reviveHp);
                                targetDaemon.Masks.Remove(lastRite);
                                opponent.AshePile.Add(new CardInstance
                                {
                                    InstanceId = Guid.NewGuid().ToString(),
                                    Card = lastRite.Card,
                                });
                                AddLog($"{targetDaemon.Card.cardName} RISES with {targetDaemon.CurrentAshe} Life as Last Rite crumbles.", LogEntryType.Effect);
                                resurrected = true;
                            }

                            var res = !resurrected ? opponent.AsheCards?.Find(a =>
                                a.AssignedDaemonInstanceId == targetDaemon.InstanceId &&
                                a.Card?.GetAffinityType() == CreatureType.Undead) : null;
                            if (!resurrected && res != null)
                            {
                                int reviveHp = res.Card.resurrectHp > 0 ? res.Card.resurrectHp : 1;
                                targetDaemon.CurrentAshe = reviveHp;
                                opponent.AsheCards.Remove(res);
                                opponent.AshePile.Add(new CardInstance { InstanceId = res.InstanceId, Card = res.Card });
                                AddLog($"{targetDaemon.Card.cardName} RISES! Resurrected with {reviveHp} Life - {res.Card.cardName} crumbles to dust.", LogEntryType.Effect);
                                resurrected = true;
                            }
                        }

                        if (!resurrected)
                        {
                            if (opponent.NoCombatDestructionThisTurn)
                            {
                                // Sanctuary is active — daemon survives at 1 Ashe
                                targetDaemon.CurrentAshe = 1;
                                AddLog($"{targetDaemon.Card.cardName} would be destroyed, but is protected by Sanctuary!", LogEntryType.Effect);
                            }
                            else
                            {
                                combat.TargetDestroyed = true;
                                attacker.HasKilledThisTurn = true;
                                attacker.KillCooldownTurnsRemaining = Math.Max(attacker.KillCooldownTurnsRemaining, 1);
                                opponent.Field.RemoveAt(action.TargetIndex);
                                opponent.AshePile.Add(new CardInstance { InstanceId = targetDaemon.InstanceId, Card = targetDaemon.Card });
                                AddLog($"{targetDaemon.Card.cardName} was destroyed!", LogEntryType.Combat);
                                combat.TargetOwnerInvokerLifeLoss = ResolveDaemonDestroyed(1 - playerIndex, targetDaemon);
                                ResolveHauntedDeath(opponent, targetDaemon, combat);
                            }
                        }
                    }
                    if (finalDamage > 0 && HasTacticsBoardSweepAttack(attacker))
                        ApplySweepSplash(playerIndex, opponent, targetLaneBeforeCombat, combat.TargetInstanceId, attacker);
                    // Check if attacker died from thorns
                    if (attacker.CurrentAshe <= 0)
                    {
                        int atkIdx = player.Field.IndexOf(attacker);
                        if (atkIdx >= 0)
                        {
                            combat.AttackerDestroyed = true;
                            player.Field.RemoveAt(atkIdx);
                            player.AshePile.Add(new CardInstance { InstanceId = attacker.InstanceId, Card = attacker.Card });
                            AddLog($"{attacker.Card.cardName} was destroyed by thorns!", LogEntryType.Combat);
                            combat.AttackerOwnerInvokerLifeLoss = ResolveDaemonDestroyed(playerIndex, attacker);
                            ResolveHauntedDeath(player, attacker, combat);
                        }
                    }
                    break;
                case TargetType.Pillar:
                    if (action.TargetIndex < 0 || action.TargetIndex >= opponent.Pillars.Count)
                        return false;
                    var pillar = opponent.Pillars[action.TargetIndex];
                    if (pillar.Destroyed)
                        return false;
                    combat.TargetName = pillar.Card.cardName;
                    combat.HitPillar = true;
                    // Reveal face‑down pillar on first attack
                    if (!pillar.Revealed)
                    {
                        pillar.Revealed = true;
                        AddLog($"Pillar revealed: {pillar.Card.cardName}!", LogEntryType.Effect);
                        AddLog($"Pillar: {DescribePillarEffect(pillar.Card)}", LogEntryType.Effect);
                    }
                    int pillarDmg = Math.Max(1, pillar.CurrentHp);
                    combat.Damage = pillarDmg;
                    pillar.CurrentHp = 0;
                    AddLog($"{attacker.Card.cardName} shatters Pillar {pillar.Card.cardName} in one strike!", LogEntryType.Combat);
                    combat.TargetDestroyed = true;
                    pillar.Destroyed = true;
                    AddLog($"Pillar {pillar.Card.cardName} was destroyed!", LogEntryType.Combat);
                    ResolvePillarDestroyed(1 - playerIndex, opponent, pillar);
                    break;
                case TargetType.Invoker:
                    int conjDmg = ApplyAttackResponsePrevention(baseDamage, attackResponse, combat);
                    combat.TargetName = opponent.Name;
                    ApplyWardReaction(player, opponent, playerIndex, attacker, ref conjDmg, combat);
                    combat.Damage = conjDmg;
                    combat.HitInvoker = true;
                    opponent.Invoker.Hp -= conjDmg;
                    AddLog($"{attacker.Card.cardName} strikes the Invoker for {conjDmg}!", LogEntryType.Combat);
                    if (opponent.Invoker.Hp <= 0)
                    {
                        opponent.Invoker.Hp = 0;
                        State.Winner = playerIndex;
                        State.GameOver = true;
                        AddLog($"{player.Name} wins!", LogEntryType.System);
                        OnGameOver?.Invoke(playerIndex, $"{player.Name} wins!");
                    }
                    break;
            }
            attacker.HasAttacked = true;
            combat.SelfDamage = attackSpiritCost;
            ResolveOverloadBacklash(player, attacker, combat);

            // Check win conditions after every combat action
            if (!State.GameOver)
                CheckWinConditions();

            OnCombatResolved?.Invoke(combat);
            OnStateChanged?.Invoke(State);

            // In Combat phase, auto-end turn when no more daemons can attack
            if (!State.GameOver && !player.Field.Any(d => d.CanAttack && !d.HasAttacked && !d.Frozen && !d.Entangled && !d.DeathsDoor))
                _combatAutoEnd = true;

            return true;
        }

        private void ApplyWardReaction(PlayerState attackerOwner, PlayerState defender, int attackerPlayerIndex,
            DaemonInstance attacker, ref int damage, CombatResolution combat)
        {
            if (!WardCatalog.WardsEnabled || damage <= 0 || defender?.Wards == null || defender.Wards.Count == 0)
                return;
            if (defender.WardReactionUsedThisTurn)
                return;

            var ward = defender.Wards.FirstOrDefault(w => w != null && !w.UsedThisTurn && !string.IsNullOrWhiteSpace(w.WardId));
            if (ward == null)
                return;

            var definition = WardCatalog.Get(ward.WardId);
            int roll = _rng.Next(1, 7);
            defender.Wards.Remove(ward);
            defender.WardReactionUsedThisTurn = true;

            combat.WardName = definition.Name;
            combat.WardRoll = roll;
            combat.WardThreshold = definition.Threshold;
            combat.WardConsumed = true;

            if (roll < definition.Threshold)
            {
                AddLog($"{definition.Name} rolls {roll}; needs {definition.Threshold}+ to react. The ward burns out.", LogEntryType.Effect);
                return;
            }

            switch (definition.ReactionType)
            {
                case WardReactionType.ReduceInvokerDamage:
                case WardReactionType.DampenStrike:
                {
                    int prevented = Math.Min(damage, Math.Max(1, definition.Value));
                    damage -= prevented;
                    combat.WardPreventedDamage = prevented;
                    AddLog($"{definition.Name} rolls {roll}, blocks {prevented} Invoker damage, then burns out.", LogEntryType.Effect);
                    break;
                }
                case WardReactionType.ReflectToAttacker:
                {
                    int reflected = Math.Max(1, definition.Value);
                    attacker.CurrentAshe -= reflected;
                    combat.WardReflectedDamage = reflected;
                    AddLog($"{definition.Name} rolls {roll}, reflects {reflected} damage into {attacker.Card.cardName}, then burns out.", LogEntryType.Effect);
                    if (attacker.CurrentAshe <= 0)
                    {
                        int atkIdx = attackerOwner.Field.IndexOf(attacker);
                        if (atkIdx >= 0)
                        {
                            combat.AttackerDestroyed = true;
                            attackerOwner.Field.RemoveAt(atkIdx);
                            attackerOwner.AshePile.Add(new CardInstance { InstanceId = attacker.InstanceId, Card = attacker.Card });
                            AddLog($"{attacker.Card.cardName} was destroyed by {definition.Name}!", LogEntryType.Combat);
                            combat.AttackerOwnerInvokerLifeLoss = ResolveDaemonDestroyed(attackerPlayerIndex, attacker);
                        }
                    }
                    break;
                }
                case WardReactionType.DrainAttackerSE:
                {
                    int drained = Math.Min(Math.Max(0, attackerOwner.Will), Math.Max(1, definition.Value));
                    attackerOwner.Will -= drained;
                    combat.WardDrainedSE = drained;
                    AddLog($"{definition.Name} rolls {roll}, drains {drained} SE from {attackerOwner.Name}, then burns out.", LogEntryType.Effect);
                    break;
                }
            }
        }

        private bool ValidateTacticsBoardAttack(PlayerState player, PlayerState opponent, DaemonInstance attacker, AttackAction action, out bool rangedShot)
        {
            rangedShot = false;
            if (!GameConstants.EnableTacticsBoardMode)
                return true;

            if (action.Target == TargetType.Invoker && HasTacticsBoardRangedAttack(attacker))
            {
                // Invokers are unlaned targets. Once the lane is exposed, the hit is
                // automatic; accuracy checks only apply to diagonal daemon targets.
                return true;
            }

            if (action.Target != TargetType.Daemon)
                return true;

            if (action.TargetIndex < 0 || action.TargetIndex >= opponent.Field.Count)
                return false;

            int attackerLane = attacker.LaneIndex >= 0 ? attacker.LaneIndex : action.AttackerIndex;
            int targetLane = opponent.Field[action.TargetIndex].LaneIndex >= 0 ? opponent.Field[action.TargetIndex].LaneIndex : action.TargetIndex;
            int laneDelta = targetLane - attackerLane;
            bool sameLane = laneDelta == 0;
            bool diagonalLane = Math.Abs(laneDelta) == 1;
            bool ranged = HasTacticsBoardRangedAttack(attacker);

            if (!ranged && sameLane)
                return true;

            if (ranged && diagonalLane)
            {
                rangedShot = true;
                return true;
            }

            if (!ranged)
            {
                AddLog($"{attacker.Card.cardName} uses D/G/S: it attacks straight ahead. S splashes after the hit.", LogEntryType.System);
                return false;
            }

            AddLog($"{attacker.Card.cardName} has Ranged: it attacks diagonal lanes only.", LogEntryType.System);
            return false;
        }

        private static bool IsTacticsDaemonTargetReachable(DaemonInstance attacker, int attackerFallbackLane,
            DaemonInstance defender, int defenderFallbackLane)
        {
            if (!GameConstants.EnableTacticsBoardMode)
                return true;
            if (attacker?.Card == null || defender?.Card == null)
                return false;

            int attackerLane = attacker.LaneIndex >= 0 ? attacker.LaneIndex : attackerFallbackLane;
            int defenderLane = defender.LaneIndex >= 0 ? defender.LaneIndex : defenderFallbackLane;
            int distance = Math.Abs(attackerLane - defenderLane);
            return HasTacticsBoardRangedAttack(attacker) ? distance == 1 : distance == 0;
        }

        private static bool CanTacticsBoardAttackInvokerLane(PlayerState opponent, int attackerLane)
        {
            if (!GameConstants.EnableTacticsBoardMode)
                return false;
            if (attackerLane < 0)
                return false;
            if (opponent?.Field == null)
                return true;
            var laneDefender = opponent.Field.FirstOrDefault(d => d != null && (d.LaneIndex >= 0 ? d.LaneIndex : opponent.Field.IndexOf(d)) == attackerLane);
            if (laneDefender != null && !laneDefender.Stealthed)
                return false;

            // A neighboring Guard does not make the lane illegal. It visibly intercepts the
            // first attack instead, so players can pressure the formation and understand why.
            return true;
        }

        private bool TryRedirectToAdjacentGuard(PlayerState opponent, DaemonInstance attacker,
            AttackAction action, out string guardName)
        {
            guardName = null;
            if (!GameConstants.EnableTacticsBoardMode || opponent?.Field == null || attacker == null)
                return false;

            int protectedLane;
            DaemonInstance originalTarget = null;
            if (action.Target == TargetType.Invoker)
            {
                protectedLane = attacker.LaneIndex >= 0 ? attacker.LaneIndex : action.AttackerIndex;
            }
            else if (action.Target == TargetType.Daemon
                && action.TargetIndex >= 0 && action.TargetIndex < opponent.Field.Count)
            {
                originalTarget = opponent.Field[action.TargetIndex];
                protectedLane = originalTarget.LaneIndex >= 0 ? originalTarget.LaneIndex : action.TargetIndex;
            }
            else
            {
                return false;
            }

            var guard = opponent.Field
                .Select((daemon, index) => (daemon, index))
                .Where(x => x.daemon != null
                    && x.daemon != originalTarget
                    && !x.daemon.GuardInterceptUsedThisRound
                    && !x.daemon.Stealthed
                    && !x.daemon.Frozen
                    && !x.daemon.Entangled
                    && !x.daemon.Silenced
                    && !x.daemon.DeathsDoor
                    && !x.daemon.IsBindAnchor
                    && IsGuardAttackPattern(x.daemon))
                .Where(x => Math.Abs((x.daemon.LaneIndex >= 0 ? x.daemon.LaneIndex : x.index) - protectedLane) == 1)
                .OrderByDescending(x => x.daemon.CurrentAshe)
                .FirstOrDefault();

            if (guard.daemon == null)
                return false;

            guard.daemon.GuardInterceptUsedThisRound = true;
            action.Target = TargetType.Daemon;
            action.TargetIndex = guard.index;
            guardName = guard.daemon.Card?.cardName ?? "Guard";
            AddLog($"G INTERCEPT: {guardName} protects lane {protectedLane + 1}.", LogEntryType.Combat);
            return true;
        }

        private static bool HasTacticsBoardRangedAttack(DaemonInstance daemon)
        {
            if (daemon?.Card == null)
                return false;

            return GetEffectiveAttackPattern(daemon) == DaemonAttackPattern.Ranged;
        }

        private static bool HasTacticsBoardSweepAttack(DaemonInstance daemon)
        {
            return GetEffectiveAttackPattern(daemon) == DaemonAttackPattern.Sweep;
        }

        private static bool IsGuardAttackPattern(DaemonInstance daemon)
        {
            return GetEffectiveAttackPattern(daemon) == DaemonAttackPattern.Guard;
        }

        private static DaemonAttackPattern GetEffectiveAttackPattern(DaemonInstance daemon)
        {
            if (daemon?.Card == null)
                return DaemonAttackPattern.Direct;

            if (!daemon.Sundered && daemon.Masks != null)
            {
                var patternMask = daemon.Masks.LastOrDefault(mask =>
                    mask?.Card != null && (mask.Card.grantsRangedAttacks || mask.Card.overridesAttackPattern));
                if (patternMask?.Card != null)
                {
                    if (patternMask.Card.grantsRangedAttacks)
                        return DaemonAttackPattern.Ranged;
                    return patternMask.Card.grantedAttackPattern;
                }
            }

            return daemon.Card.rangedAttack ? DaemonAttackPattern.Ranged : daemon.Card.attackPattern;
        }

        private void ResolveOverloadBacklash(PlayerState owner, DaemonInstance attacker, CombatResolution combat)
        {
            if (owner == null || attacker == null || !attacker.Overloaded)
                return;
            if (!owner.Field.Contains(attacker))
                return;

            int backlash = Math.Max(1, attacker.OverloadBacklash);
            attacker.CurrentAshe -= backlash;
            AddLog($"{attacker.Card.cardName}'s Overload snaps back for {backlash} damage.", LogEntryType.Effect);
            if (attacker.CurrentAshe > 0)
                return;

            int idx = owner.Field.IndexOf(attacker);
            if (idx < 0)
                return;

            combat.AttackerDestroyed = true;
            owner.Field.RemoveAt(idx);
            owner.AshePile.Add(new CardInstance { InstanceId = attacker.InstanceId, Card = attacker.Card });
            AddLog($"{attacker.Card.cardName} was destroyed by Overload!", LogEntryType.Combat);
            ResolveDaemonDestroyed(State.Players[0] == owner ? 0 : 1, attacker);
            ResolveHauntedDeath(owner, attacker, combat);
        }

        private void ResolveHauntedDeath(PlayerState owner, DaemonInstance daemon, CombatResolution combat)
        {
            if (owner == null || daemon == null || !daemon.Haunted)
                return;

            int loss = Math.Max(1, daemon.HauntedLifeLoss);
            owner.Invoker.Hp = Math.Max(0, owner.Invoker.Hp - loss);
            AddLog($"{daemon.Card.cardName}'s Haunting costs {owner.Name} {loss} Invoker Life.", LogEntryType.Effect);
            if (owner.Invoker.Hp <= 0 && !State.GameOver)
            {
                int winner = State.Players[0] == owner ? 1 : 0;
                State.Winner = winner;
                State.GameOver = true;
                AddLog($"{State.Players[winner].Name} wins!", LogEntryType.System);
                OnGameOver?.Invoke(winner, $"{State.Players[winner].Name} wins!");
            }
        }

        private void ApplySweepSplash(int attackerPlayerIndex, PlayerState defender, int primaryLane, string primaryInstanceId, DaemonInstance attacker)
        {
            if (!GameConstants.EnableTacticsBoardMode || defender?.Field == null || primaryLane < 0)
                return;

            var statusMask = GetSweepStatusRelic(attacker);
            var splashTargets = defender.Field
                .Select((daemon, index) => (daemon, index))
                .Where(x => x.daemon != null
                    && x.daemon.InstanceId != primaryInstanceId
                    && Math.Abs((x.daemon.LaneIndex >= 0 ? x.daemon.LaneIndex : x.index) - primaryLane) == 1)
                .OrderByDescending(x => x.index)
                .ToList();

            foreach (var (daemon, index) in splashTargets)
            {
                if (daemon.CurrentAshe <= 0)
                    continue;

                daemon.CurrentAshe -= 1;
                AddLog($"Sweep splashes {daemon.Card.cardName} for 1 damage.", LogEntryType.Combat);
                ApplySweepSplashStatus(statusMask, daemon);
                if (daemon.CurrentAshe > 0)
                    continue;

                defender.Field.RemoveAt(index);
                defender.AshePile.Add(new CardInstance { InstanceId = daemon.InstanceId, Card = daemon.Card });
                AddLog($"{daemon.Card.cardName} was destroyed by Sweep!", LogEntryType.Combat);
                ResolveDaemonDestroyed(1 - attackerPlayerIndex, daemon);
            }
        }

        private static MaskCardData GetSweepStatusRelic(DaemonInstance attacker)
        {
            if (attacker?.Masks == null)
                return null;

            return attacker.Masks
                .Select(mask => mask?.Card)
                .LastOrDefault(card => card != null
                    && card.overridesAttackPattern
                    && card.grantedAttackPattern == DaemonAttackPattern.Sweep
                    && card.splashStatusValue > 0);
        }

        private void ApplySweepSplashStatus(MaskCardData statusRelic, DaemonInstance daemon)
        {
            if (statusRelic == null || daemon == null || statusRelic.splashStatusValue <= 0)
                return;

            int amount = Math.Max(1, statusRelic.splashStatusValue);
            switch (statusRelic.splashStatusEffect)
            {
                case MaskEffectType.Burn:
                    daemon.Burning = true;
                    daemon.BurnDamage = Math.Max(daemon.BurnDamage, amount);
                    AddLog($"{daemon.Card.cardName} is burned by the splash.", LogEntryType.Effect);
                    break;
                case MaskEffectType.Poison:
                    daemon.Poisoned = true;
                    daemon.PoisonDamage = Math.Max(daemon.PoisonDamage, amount);
                    AddLog($"{daemon.Card.cardName} is poisoned by the splash.", LogEntryType.Effect);
                    break;
                case MaskEffectType.Entangle:
                    daemon.Entangled = true;
                    daemon.EntangledTurns = Math.Max(daemon.EntangledTurns, amount);
                    AddLog($"{daemon.Card.cardName} is entangled by the splash.", LogEntryType.Effect);
                    break;
            }
        }

        private DispelCardData TryConsumeAttackResponseDispel(PlayerState defender, int attackerPlayerIndex, int handIndex, DaemonCardData attackerCard, CombatResolution combat)
        {
            if (defender == null || attackerCard == null || handIndex < 0 || handIndex >= defender.Hand.Count)
                return null;

            var cardInstance = defender.Hand[handIndex];
            if (cardInstance.Card is not DispelCardData dispel || !CanDispelCounterAttack(dispel, attackerCard))
                return null;

            int cost = Math.Max(0, dispel.GetWillCost());
            if (defender.Will < cost)
            {
                AddLog($"{defender.Name} needs {cost} SE to answer {attackerCard.cardName}.", LogEntryType.System);
                return null;
            }

            defender.Will -= cost;
            defender.Hand.RemoveAt(handIndex);
            defender.AshePile.Add(cardInstance);
            CommitElementForCard(defender, dispel);
            combat.ResponseDispelName = dispel.cardName;
            AddRitualStep(1 - attackerPlayerIndex, RitualChainActionType.DispelResponse, dispel,
                $"{defender.Name} answers the attack with {dispel.cardName}.");
            AddLog($"{defender.Name} negates the incoming attack with {dispel.cardName}!", LogEntryType.Effect);
            return dispel;
        }

        private enum SpellResponseWindow
        {
            Hex,
            Domain,
        }

        private DispelCardData TryConsumeSpellResponseDispel(PlayerState defender, int casterPlayerIndex,
            int handIndex, CardData incomingCard, SpellResponseWindow window)
        {
            if (defender == null || incomingCard == null || handIndex < 0 || handIndex >= defender.Hand.Count)
                return null;

            var cardInstance = defender.Hand[handIndex];
            if (cardInstance.Card is not DispelCardData dispel)
                return null;

            bool legal = window == SpellResponseWindow.Hex
                ? CanDispelCounterHex(dispel)
                : CanDispelCounterDomain(dispel);
            if (!legal)
                return null;

            int cost = Math.Max(0, dispel.GetWillCost());
            if (defender.Will < cost)
            {
                AddLog($"{defender.Name} needs {cost} SE to answer {incomingCard.cardName}.", LogEntryType.System);
                return null;
            }

            defender.Will -= cost;
            defender.Hand.RemoveAt(handIndex);
            defender.AshePile.Add(cardInstance);
            CommitElementForCard(defender, dispel);
            AddRitualStep(1 - casterPlayerIndex, RitualChainActionType.DispelResponse, dispel,
                $"{defender.Name} answers {incomingCard.cardName} with {dispel.cardName}.");
            AddLog($"{defender.Name} negates {incomingCard.cardName} with {dispel.cardName}!", LogEntryType.Effect);
            if (dispel.counterEffect != null && !string.IsNullOrWhiteSpace(dispel.counterEffect.effectType))
                ResolveDispelCounterEffect(defender, State.Players[casterPlayerIndex], dispel);
            return dispel;
        }

        private int ApplyAttackResponsePrevention(int damage, DispelCardData response, CombatResolution combat)
        {
            if (response == null || damage <= 0)
                return damage;

            combat.ResponsePreventedDamage += damage;
            AddLog($"{response.cardName} negates the attack.", LogEntryType.Effect);

            if (response.counterEffect != null && !string.IsNullOrWhiteSpace(response.counterEffect.effectType))
                ResolveDispelCounterEffect(State.Players[combat.TargetPlayer], State.Players[combat.AttackerPlayer], response);

            return 0;
        }

        public static bool CanDispelCounterAttack(DispelCardData dispel, DaemonCardData attackerCard)
        {
            if (dispel == null || attackerCard == null || !dispel.canCounterAttack)
                return false;

            return true;
        }

        public static bool CanDispelCounterHex(DispelCardData dispel)
        {
            return dispel != null && dispel.canCounterHex;
        }

        public static bool CanDispelCounterDomain(DispelCardData dispel)
        {
            return dispel != null && dispel.canCounterDomain;
        }

        /// <summary>Set when all daemons have attacked — UI checks to auto-end turn.</summary>
        private bool _combatAutoEnd;
        public bool CombatAutoEnd { get { bool v = _combatAutoEnd; _combatAutoEnd = false; return v; } }

        private float ResolveDomainWeatherMultiplier(DaemonInstance attacker, DaemonInstance defender, out string weatherTag)
        {
            weatherTag = null;
            if (State?.ActiveDomain?.Card == null)
                return 1f;

            Element weatherElement = State.ActiveDomain.Card.effectElement;
            float mult = 1f;

            // Same-element daemons with the current domain gain momentum.
            if (attacker.Card.element == weatherElement)
            {
                mult *= 1.25f;
                weatherTag = $"{weatherElement} domain amplifies attack!";
            }
            // Elements weak into the weather lose force.
            else if (ElementSystem.GetElementMatchup(attacker.Card.element, weatherElement) == GameConstants.WeakMult)
            {
                mult *= 0.78f;
                weatherTag = $"{weatherElement} domain suppresses the strike.";
            }

            // Defenders aligned with the active weather resist incoming damage.
            if (defender.Card.element == weatherElement)
            {
                mult *= 0.88f;
                weatherTag = $"{weatherElement} ward softens the impact.";
            }
            else if (ElementSystem.GetElementMatchup(weatherElement, defender.Card.element) == GameConstants.SuperEffectiveMult)
            {
                mult *= 1.12f;
                weatherTag = $"{weatherElement} weather exposes the defender!";
            }

            if (mult < 0.55f) mult = 0.55f;
            if (mult > 1.65f) mult = 1.65f;
            return mult;
        }

        private bool IsDomainEffectActiveAgainst(int playerIndex, DomainEffectType effectType)
        {
            return State?.ActiveDomain?.Card != null
                && State.ActiveDomain.Card.effectType == effectType
                && State.ActiveDomain.Owner != playerIndex;
        }

        private bool IsTypeNullDomainActive()
        {
            return State?.ActiveDomain?.Card?.effectType == DomainEffectType.TypeNull;
        }

        private bool ShouldMachineGlitchMiss(int playerIndex, DaemonInstance attacker)
        {
            if (!IsDomainEffectActiveAgainst(playerIndex, DomainEffectType.MachineGlitch) || attacker?.Card == null)
                return false;

            bool techBody = attacker.Card.creatureType is CreatureType.Machine or CreatureType.Artificial;
            return techBody && _rng.Next(2) == 0;
        }

        private void ApplyStrategicDomainTurnStart(int currentPlayer, bool entrancePulse = false)
        {
            if (State?.ActiveDomain?.Card == null)
                return;

            DomainCardData domain = State.ActiveDomain.Card;
            if (State.ActiveDomain.Owner == currentPlayer)
            {
                if (domain.effectType == DomainEffectType.ArtificialConstruct)
                    ApplyArtificialConstructDomain(currentPlayer, domain);
                return;
            }

            PlayerState affected = State.Players[currentPlayer];
            int value = Math.Max(1, domain.effectValue);

            switch (domain.effectType)
            {
                case DomainEffectType.PetrifyCycle:
                    if (!entrancePulse && State.TurnNumber % 2 != 0)
                        return;
                    foreach (var daemon in affected.Field)
                    {
                        daemon.Frozen = true;
                        daemon.FrozenTurns = Math.Max(daemon.FrozenTurns, 1);
                        daemon.CanAttack = false;
                    }
                    if (affected.Field.Count > 0)
                        AddLog($"{domain.cardName} petrifies {affected.Name}'s board this turn.", LogEntryType.Effect);
                    break;

                case DomainEffectType.SpiritDrain:
                    int spiritCount = affected.Field.Count(d => d.Card != null && d.Card.creatureType == CreatureType.Spirit);
                    int drained = Math.Min(affected.Will, spiritCount * value);
                    if (drained > 0)
                    {
                        affected.Will -= drained;
                        AddLog($"{domain.cardName} drains {drained} SE from {affected.Name}'s Spirits.", LogEntryType.Effect);
                    }
                    break;

                case DomainEffectType.ArtificialCorrupt:
                    int corrupted = 0;
                    foreach (var daemon in affected.Field.Where(d => d.Card != null && d.Card.creatureType == CreatureType.Artificial))
                    {
                        daemon.CurrentAshe -= value;
                        corrupted++;
                    }
                    if (corrupted > 0)
                        AddLog($"{domain.cardName} corrupts {corrupted} Artificial daemon(s) for {value} Life.", LogEntryType.Effect);
                    break;

                case DomainEffectType.MachinePropagate:
                    ApplyMachinePropagateDomain(State.ActiveDomain.Owner, affected, domain);
                    break;

                case DomainEffectType.SpiritDefile:
                    ApplySpiritDefileDomain(State.ActiveDomain.Owner, affected, domain);
                    break;
            }

        }

        private void ApplyMachinePropagateDomain(int ownerIndex, PlayerState affected, DomainCardData domain)
        {
            if (ownerIndex < 0 || ownerIndex >= State.Players.Length || affected?.Field == null)
                return;

            var target = affected.Field
                .Where(d => d?.Card != null && !d.Hologram)
                .OrderBy(d => d.CurrentAshe)
                .FirstOrDefault();
            if (target == null)
                return;

            int drain = Math.Max(1, domain.effectValue);
            target.CurrentAshe -= drain;
            int gained = AddStoredSpiritEnergy(State.Players[ownerIndex], drain);
            AddLog($"{domain.cardName} nanobots consume {drain} Life from {target.Card.cardName}; {State.Players[ownerIndex].Name} gains {gained} SE.", LogEntryType.Effect);
        }

        private void ApplySpiritDefileDomain(int ownerIndex, PlayerState affected, DomainCardData domain)
        {
            if (ownerIndex < 0 || ownerIndex >= State.Players.Length || affected?.Field == null)
                return;

            foreach (var daemon in affected.Field.Where(d => d?.Card != null && !d.Darkened))
            {
                daemon.Darkened = true;
                AddLog($"{domain.cardName} darkens {daemon.Card.cardName}; Light still cuts through the defilement.", LogEntryType.Effect);
            }

            var owner = State.Players[ownerIndex];
            for (int i = affected.Field.Count - 1; i >= 0; i--)
            {
                var daemon = affected.Field[i];
                if (daemon?.Card == null || !daemon.Darkened || owner.Field.Count >= GameConstants.MaxFieldDaemons)
                    continue;

                bool controlBreaks = _rng.Next(2) == 0;
                if (!controlBreaks)
                    continue;

                affected.Field.RemoveAt(i);
                daemon.HasAttacked = false;
                daemon.CanAttack = true;
                owner.Field.Add(daemon);
                AddLog($"{daemon.Card.cardName} fails its control check and turns under {owner.Name}'s command.", LogEntryType.Effect);
            }
        }

        private void ApplyArtificialConstructDomain(int ownerIndex, DomainCardData domain)
        {
            PlayerState owner = State.Players[ownerIndex];
            if (owner?.Field == null || owner.Field.Count >= GameConstants.MaxFieldDaemons)
                return;

            var source = owner.Field
                .Where(d => d?.Card != null && !d.Hologram && !d.ConsumedShade)
                .OrderByDescending(d => d.Attack + d.CurrentAshe)
                .FirstOrDefault();
            if (source == null)
                return;

            int holoAttack = Math.Max(1, source.Attack / 2);
            int holoLife = Math.Max(1, source.MaxAshe / 2);
            var hologram = CreateDaemonInstance(source.Card, Guid.NewGuid().ToString(), holoAttack, holoLife, 0);
            hologram.LaneIndex = ResolveSummonLane(owner, -1);
            hologram.Hologram = true;
            hologram.CanAttack = true;
            owner.Field.Add(hologram);
            AddLog($"{domain.cardName} projects a hologram of {source.Card.cardName}: {holoAttack} ATK / {holoLife} Life.", LogEntryType.Effect);
        }

        private static float ResolveDefiledElementMultiplier(DaemonInstance attacker, DaemonInstance defender)
        {
            if (defender?.Darkened == true)
                return attacker?.Card?.element == Element.Light
                    ? GameConstants.SuperEffectiveMult
                    : GameConstants.NeutralMult;

            if (attacker?.Darkened == true)
                return defender?.Card?.element == Element.Light
                    ? GameConstants.WeakMult
                    : GameConstants.NeutralMult;

            return ElementSystem.GetElementMatchup(attacker.Card.element, defender.Card.element);
        }

        private void ResolveAttackBloodPrice(PlayerState player, int playerIndex, DaemonInstance attacker, int attackBloodPrice,
            CombatResolution combat = null)
        {
            // Self-damage mechanic removed — attacking no longer costs Ashe from the attacker.
            // SE cost for abilities is handled separately through the SE pool.
        }

        // ─── Activate Pillar ─────────────────────────────────────────
        private bool HandleActivatePillar(PlayerState player, int pillarIndex, int abilityIndex)
        {
            if (pillarIndex < 0 || pillarIndex >= player.Pillars.Count)
                return false;
            var pillar = player.Pillars[pillarIndex];
            if (pillar.Destroyed || pillar.AbilityUsedThisTurn)
                return false;
            if (pillar.Card.activatedAbilities == null || abilityIndex >= pillar.Card.activatedAbilities.Length)
                return false;
            var ability = pillar.Card.activatedAbilities[abilityIndex];
            if (pillar.Loyalty < ability.loyaltyCost)
                return false;
            pillar.Loyalty -= ability.loyaltyCost;
            pillar.AbilityUsedThisTurn = true;
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.PillarActivated, pillar.Card,
                $"{player.Name} activates {pillar.Card.cardName}.");
            AddLog($"{player.Name} activated {pillar.Card.cardName}'s {ability.abilityName}!", LogEntryType.Action);

            // Execute the ability via the effect resolver
            _effects.ActivatePillarAbility(State.CurrentPlayer, pillar, ability);

            // Clean up dead daemons from effects
            for (int pi = 0; pi < 2; pi++)
            {
                var dead = _effects.CleanupDead(pi);
                foreach (var d in dead)
                    ResolveDaemonDestroyed(pi, d);
            }
            CheckWinConditions();

            OnStateChanged?.Invoke(State);
            return true;
        }

        private bool HandleFuseDaemons(PlayerState player, int primaryIndex, int secondaryIndex)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (primaryIndex == secondaryIndex)
                return false;
            if (primaryIndex < 0 || primaryIndex >= player.Field.Count)
                return false;
            if (secondaryIndex < 0 || secondaryIndex >= player.Field.Count)
                return false;

            var primary = player.Field[primaryIndex];
            var secondary = player.Field[secondaryIndex];
            if (primary.IsFusionApex || secondary.IsFusionApex)
                return false;
            if (!CardsMatchForFusion(primary.Card, secondary.Card))
                return false;
            int fusionSealIndex = FindFusionSealIndex(player);
            if (fusionSealIndex < 0)
                return false;

            var fusionSeal = player.Hand[fusionSealIndex];
            var fusedCard = CreateFusedDaemonCard(primary.Card, secondary.Card);
            var fusedDaemon = new DaemonInstance
            {
                InstanceId = Guid.NewGuid().ToString(),
                Card = fusedCard,
                BaseAttack = fusedCard.attack,
                BaseAshe = fusedCard.ashe,
                CurrentAshe = Math.Min(fusedCard.ashe, primary.CurrentAshe + secondary.CurrentAshe + 2),
                MaxAshe = fusedCard.ashe,
                Attack = fusedCard.attack,
                AsheCost = fusedCard.asheCost,
                CanAttack = true,
                HasAttacked = false,
                IsFusionApex = true,
                FusionTurnsRemaining = 0,
            };

            int insertIndex = Math.Min(primaryIndex, secondaryIndex);
            int firstRemove = Math.Max(primaryIndex, secondaryIndex);
            int secondRemove = Math.Min(primaryIndex, secondaryIndex);

            player.Field.RemoveAt(firstRemove);
            player.Field.RemoveAt(secondRemove);
            player.Field.Insert(insertIndex, fusedDaemon);

            player.Hand.RemoveAt(fusionSealIndex);
            player.AshePile.Add(new CardInstance { InstanceId = primary.InstanceId, Card = primary.Card });
            player.AshePile.Add(new CardInstance { InstanceId = secondary.InstanceId, Card = secondary.Card });
            player.AshePile.Add(fusionSeal);

            AddLog($"{primary.Card.cardName} is fusion-sealed into {fusedCard.cardName} using {fusionSeal.Card.cardName}!", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        private bool HandleActivateInvoker(PlayerState player, int playerIndex)
        {
            if (State.Phase != GamePhase.Main)
                return false;
            if (player.InvokerAbilityUsedThisTurn)
                return false;

            int cost = GameConstants.InvokerUnleashCost;
            if (player.Will < cost)
                return false;

            player.Will -= cost;
            player.InvokerAbilityUsedThisTurn = true;
            AddRitualStep(playerIndex, RitualChainActionType.InvokerActivated, null,
                $"{player.Name} invokes a focused pulse.");

            int opponentIndex = 1 - playerIndex;
            int damage = GameConstants.InvokerUnleashDamage;
            int heal = GameConstants.InvokerUnleashHeal;

            // Two allied daemons sharing an element strengthen the focused pulse.
            bool hasFusionResonance = player.Field
                .GroupBy(d => d.Card.element)
                .Any(g => g.Count() >= 2);
            if (hasFusionResonance)
            {
                damage += GameConstants.InvokerFusionResonanceBonus;
                heal += GameConstants.InvokerFusionResonanceBonus;
                AddLog("Fusion resonance amplifies Invoker Pulse!", LogEntryType.Effect);
            }

            AddLog($"{player.Name} uses Invoker Pulse ({cost} SE).", LogEntryType.Action);

            var opponent = State.Players[opponentIndex];
            var pulseTarget = opponent.Field
                .Where(daemon => daemon != null && daemon.CurrentAshe > 0)
                .OrderByDescending(daemon => daemon.Attack + daemon.CurrentAshe)
                .FirstOrDefault();
            if (pulseTarget != null)
                pulseTarget.CurrentAshe = Math.Max(0, pulseTarget.CurrentAshe - damage);

            player.Invoker.Hp = Math.Min(player.Invoker.MaxHp, player.Invoker.Hp + heal);
            AddLog(pulseTarget != null
                ? $"Invoker Pulse deals {damage} to {pulseTarget.Card.cardName} and restores {heal} Invoker Life."
                : $"Invoker Pulse finds no enemy Daemon and restores {heal} Invoker Life.", LogEntryType.Effect);

            var deadOpponent = _effects.CleanupDead(opponentIndex);
            foreach (var dead in deadOpponent)
            {
                AddLog($"{dead.Card.cardName} was destroyed by Invoker Pulse!", LogEntryType.Combat);
                ResolveDaemonDestroyed(opponentIndex, dead);
            }

            CheckWinConditions();
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Phase Management ───────────────────────────────────────

        private bool EndTurn()
        {
            // Can end turn from Main (skip combat) or Combat
            if (State.Phase != GamePhase.Main && State.Phase != GamePhase.Combat)
                return false;
            var currentPlayer = State.Players[State.CurrentPlayer];
            // Reset daemon attack flags and enable CanAttack for next turn
            // Kill cooldown: a daemon that destroyed an enemy daemon this turn cannot attack next turn.
            foreach (var d in currentPlayer.Field)
            {
                if (d.IsBindAnchor)
                {
                    d.CanAttack = false;
                    d.HasAttacked = true;
                }
                else if (d.HasKilledThisTurn)
                {
                    d.KillCooldownTurnsRemaining = Math.Max(d.KillCooldownTurnsRemaining, 1);
                    d.CanAttack = false;
                    d.HasKilledThisTurn = false;
                }
                else if (d.KillCooldownTurnsRemaining > 0)
                {
                    d.KillCooldownTurnsRemaining--;
                    d.CanAttack = d.KillCooldownTurnsRemaining <= 0;
                }
                else
                {
                    d.CanAttack = true;
                }

                if (!d.IsBindAnchor)
                    d.HasAttacked = false;
            }
            // Reset pillar ability usage
            foreach (var p in currentPlayer.Pillars)
            {
                p.AbilityUsedThisTurn = false;
            }
            currentPlayer.InvokerAbilityUsedThisTurn = false;
            currentPlayer.SourceRaidUsedThisTurn = false;
            currentPlayer.SourcePlayedThisTurn = false;
            currentPlayer.WardReactionUsedThisTurn = false;
            foreach (var ward in currentPlayer.Wards)
                ward.UsedThisTurn = false;
            // Sanctuary only lasts for the turn it was played
            currentPlayer.NoCombatDestructionThisTurn = false;
            // Decrement mask durations and remove expired masks
            foreach (var d in currentPlayer.Field)
            {
                d.Masks.RemoveAll(m =>
                {
                    m.TurnsRemaining--;
                    return m.TurnsRemaining <= 0;
                });
            }

            // Fire turn-end effects for the player who just ended
            _effects.OnTurnEnd(State.CurrentPlayer);
            _triggers.Fire(GameTrigger.OnTurnEnd, State, State.CurrentPlayer);

            // Tick stat modifier durations (remove expired buffs/debuffs)
            foreach (var d in currentPlayer.Field)
                d.TickModifiers();

            // Spirit Ascendant Bond: expire ashe cards whose buff duration has run out
            for (int i = currentPlayer.AsheCards.Count - 1; i >= 0; i--)
            {
                var a = currentPlayer.AsheCards[i];
                if (a?.Card?.GetAffinityType() == CreatureType.Spirit && a.BuffTurnsRemaining > 0)
                {
                    a.BuffTurnsRemaining--;
                    if (a.BuffTurnsRemaining <= 0)
                    {
                        currentPlayer.AsheCards.RemoveAt(i);
                        currentPlayer.AshePile.Add(new CardInstance { InstanceId = a.InstanceId, Card = a.Card });
                        AddLog($"{a.Card.cardName} fades — spirit bond dissolved.", LogEntryType.Effect);
                    }
                }
            }

            // Domains now expire naturally instead of lasting the whole match.
            TickDomainDuration();

            // Poison/Burn damage between turns
            _effects.TickPoisonAndBurn(State.CurrentPlayer);

            // Clean up daemons killed by poison/burn/turn effects
            for (int pi = 0; pi < 2; pi++)
            {
                var dead = _effects.CleanupDead(pi);
                foreach (var d in dead)
                {
                    AddLog($"{d.Card.cardName} was destroyed!", LogEntryType.Combat);
                    ResolveDaemonDestroyed(pi, d);
                }
            }

            // Check win conditions after poison/burn kills
            if (State.GameOver) return true;
            CheckWinConditions();
            if (State.GameOver) return true;

            // Switch active player and increment turn count
            State.CurrentPlayer = 1 - State.CurrentPlayer;
            State.TurnNumber++;

            // Generate SE from the new player's active Source cards.
            var nextPlayer = State.Players[State.CurrentPlayer];
            foreach (var daemon in nextPlayer.Field)
                daemon.GuardInterceptUsedThisRound = false;
            nextPlayer.SourceRaidUsedThisTurn = false;
            nextPlayer.SourcePlayedThisTurn = false;
            nextPlayer.WardReactionUsedThisTurn = false;
            nextPlayer.InvokerPassiveUsedThisTurn = false;
            int se = GenerateSourceSpiritEnergy(nextPlayer);
            AddLog(se > 0
                ? $"Turn {State.TurnNumber} — {nextPlayer.Name}'s Sources generate {se} SE ({nextPlayer.Will} total)."
                : $"Turn {State.TurnNumber} — {nextPlayer.Name}'s turn. No active Sources. Set a Source in Prepare to make SE.", LogEntryType.System);
            ApplyInvokerStartTurnPassive(State.CurrentPlayer, nextPlayer);
            ResolvePendingEchoes(State.CurrentPlayer);

            State.Phase = GamePhase.Draw;

            // Fire turn-start effects for the new player
            _effects.OnTurnStart(State.CurrentPlayer);
            _triggers.Fire(GameTrigger.OnTurnStart, State, State.CurrentPlayer);
            ApplyStrategicDomainTurnStart(State.CurrentPlayer);
            ApplyCreatureTypeTurnStartPassives(nextPlayer);
            ApplySecondFormBindTurnStart(State.CurrentPlayer, nextPlayer);

            // Clean up any daemons killed by turn effects
            for (int pi = 0; pi < 2; pi++)
            {
                var dead = _effects.CleanupDead(pi);
                foreach (var d in dead)
                {
                    AddLog($"{d.Card.cardName} was destroyed!", LogEntryType.Combat);
                    ResolveDaemonDestroyed(pi, d);
                }
            }
            CheckWinConditions();

            OnStateChanged?.Invoke(State);
            return true;
        }

        private int BankSpiritEnergy(PlayerState player, int rollAmount)
        {
            player.Will = Math.Min(player.Will + rollAmount, GameConstants.MaxStoredWill);
            player.MaxWill = player.Will;
            _lastSERoll = rollAmount;
            return player.Will;
        }

        private int GenerateSourceSpiritEnergy(PlayerState player)
        {
            if (player == null)
                return 0;

            int sourcePulse = 0;
            int suppressedSources = 0;
            if (player.AsheCards != null)
            {
                foreach (var ashe in player.AsheCards)
                {
                    if (ashe?.Card == null)
                        continue;
                    if (!string.IsNullOrEmpty(ashe.AssignedDaemonInstanceId))
                        continue;
                    if (ashe.SuppressedTurnsRemaining > 0)
                    {
                        ashe.SuppressedTurnsRemaining--;
                        suppressedSources++;
                        continue;
                    }

                    sourcePulse += GameConstants.SourceSEPerTurn;
                }
            }

            int generated = sourcePulse;
            int storedTotal = generated > 0
                ? BankSpiritEnergy(player, generated)
                : player.Will;
            _lastSERoll = generated;
            if (suppressedSources > 0)
                AddLog($"{player.Name} has {suppressedSources} raided Source{(suppressedSources == 1 ? "" : "s")} offline this turn.", LogEntryType.System);
            OnSERolled?.Invoke(player.Name, generated, storedTotal);
            return generated;
        }

        private void ApplyInvokerStartTurnPassive(int playerIndex, PlayerState player)
        {
            if (State == null || player == null || player.InvokerCard == null || player.InvokerPassiveUsedThisTurn)
                return;

            PlayerState opponent = State.Players[1 - playerIndex];
            string invokerName = player.InvokerCard.cardName;
            switch (player.InvokerArchetype)
            {
                case CreatureType.Elemental:
                    AddStoredSpiritEnergy(player, 1);
                    AddLog($"Invoker passive — {invokerName}'s elemental channel adds +1 SE.", LogEntryType.Effect);
                    break;
                case CreatureType.Machine:
                    ApplyInvokerPassiveDrain(player, opponent, invokerName);
                    break;
                case CreatureType.Artificial:
                    ApplyInvokerPassiveShield(player, CreatureType.Artificial, 1,
                        $"{invokerName}'s construct field gives an Artificial daemon 1 Shield.");
                    break;
                case CreatureType.Spirit:
                    ApplyInvokerPassiveHeal(player, 1, $"{invokerName}'s spirit bond restores 1 Life to your weakest daemon.");
                    break;
                case CreatureType.Undead:
                    ApplyUndeadInvokerPassive(playerIndex, player, opponent, invokerName);
                    break;
                case CreatureType.Aesir:
                    ApplyInvokerPassiveShield(player, CreatureType.Aesir, 2,
                        $"{invokerName}'s oath field gives an Aesir daemon 1 Shield.");
                    break;
            }

            player.InvokerPassiveUsedThisTurn = true;
        }

        private void SuppressFirstEnemySource(PlayerState opponent, string invokerName)
        {
            var source = opponent?.AsheCards?
                .FirstOrDefault(card => card?.Card != null && string.IsNullOrEmpty(card.AssignedDaemonInstanceId) && card.SuppressedTurnsRemaining <= 0);
            if (source == null)
                return;

            source.SuppressedTurnsRemaining = Math.Max(source.SuppressedTurnsRemaining, 1);
            AddLog($"{invokerName}'s oath pressure consecrates an enemy Source for 1 turn.", LogEntryType.Effect);
        }

        private void ApplyInvokerPassiveDamage(int opponentIndex, PlayerState opponent, int amount, string log)
        {
            DaemonInstance target = GetWeakestDaemon(opponent);
            if (target != null)
            {
                target.CurrentAshe = Math.Max(0, target.CurrentAshe - amount);
                AddLog(log, LogEntryType.Effect);
                var dead = _effects.CleanupDead(opponentIndex);
                foreach (var daemon in dead)
                {
                    AddLog($"{daemon.Card.cardName} was destroyed by an Invoker passive!", LogEntryType.Combat);
                    ResolveDaemonDestroyed(opponentIndex, daemon);
                }
                CheckWinConditions();
                return;
            }

            opponent.Invoker.Hp = Math.Max(0, opponent.Invoker.Hp - amount);
            AddLog(log.Replace("the weakest enemy", "the enemy Invoker"), LogEntryType.Effect);
            CheckWinConditions();
        }

        private void ApplyUndeadInvokerPassive(int playerIndex, PlayerState player, PlayerState opponent, string invokerName)
        {
            DaemonInstance woundedUndead = player.Field
                .Where(daemon => daemon?.Card?.creatureType == CreatureType.Undead
                    && daemon.CurrentAshe > 0 && daemon.CurrentAshe < daemon.MaxAshe)
                .OrderBy(daemon => daemon.CurrentAshe)
                .FirstOrDefault();
            if (woundedUndead != null)
            {
                int restored = Math.Min(2, woundedUndead.MaxAshe - woundedUndead.CurrentAshe);
                woundedUndead.CurrentAshe += restored;
                AddLog($"{invokerName}'s grave bond restores {restored} Life to {woundedUndead.Card.cardName}.", LogEntryType.Effect);
                return;
            }

            ApplyInvokerPassiveDamage(1 - playerIndex, opponent, 1,
                $"{invokerName}'s grave pressure haunts the weakest enemy for 1 Life.");
        }

        private void ApplyInvokerPassiveFreeze(PlayerState opponent, string log)
        {
            DaemonInstance target = GetWeakestDaemon(opponent);
            if (target == null)
                return;

            target.Frozen = true;
            target.FrozenTurns = Math.Max(target.FrozenTurns, 1);
            AddLog(log, LogEntryType.Effect);
        }

        private void ApplyInvokerPassiveHeal(PlayerState player, int amount, string log)
        {
            DaemonInstance target = player.Field
                .Where(d => d != null && d.CurrentAshe > 0 && d.CurrentAshe < d.MaxAshe)
                .OrderBy(d => d.CurrentAshe)
                .FirstOrDefault();
            if (target != null)
            {
                target.CurrentAshe = Math.Min(target.MaxAshe, target.CurrentAshe + amount);
                AddLog(log, LogEntryType.Effect);
                return;
            }

            if (player.Invoker.Hp < player.Invoker.MaxHp)
            {
                player.Invoker.Hp = Math.Min(player.Invoker.MaxHp, player.Invoker.Hp + amount);
                AddLog(log.Replace("your weakest daemon", "your Invoker"), LogEntryType.Effect);
            }
        }

        private void ApplyInvokerPassiveShield(PlayerState player, CreatureType affinity, int maximumShield, string log)
        {
            DaemonInstance target = player.Field
                .Where(d => d?.Card != null && d.Card.creatureType == affinity
                    && d.CurrentAshe > 0 && d.ShieldAmount < maximumShield)
                .OrderBy(d => d.ShieldAmount)
                .ThenBy(d => d.CurrentAshe)
                .FirstOrDefault();
            if (target == null)
                return;

            target.ShieldAmount = Math.Min(maximumShield, target.ShieldAmount + 1);
            AddLog(log, LogEntryType.Effect);
        }

        private void ApplyInvokerPassiveDrain(PlayerState player, PlayerState opponent, string invokerName)
        {
            if (opponent.Will > 0)
            {
                opponent.Will = Math.Max(0, opponent.Will - 1);
                AddLog($"{invokerName}'s feedback lattice disrupts 1 enemy SE.", LogEntryType.Effect);
                return;
            }

            AddLog($"{invokerName}'s feedback lattice finds no enemy SE to disrupt.", LogEntryType.Effect);
        }

        private static DaemonInstance GetWeakestDaemon(PlayerState player)
        {
            if (player?.Field == null)
                return null;

            return player.Field
                .Where(d => d != null && d.CurrentAshe > 0)
                .OrderBy(d => d.CurrentAshe)
                .ThenBy(d => d.Attack)
                .FirstOrDefault();
        }

        private int AddStoredSpiritEnergy(PlayerState player, int amount)
        {
            if (player == null || amount <= 0)
                return 0;

            int before = player.Will;
            player.Will = Math.Min(player.Will + amount, GameConstants.MaxStoredWill);
            player.MaxWill = Math.Max(player.MaxWill, player.Will);
            return player.Will - before;
        }

        private void ApplySecondFormBindTurnStart(int ownerIndex, PlayerState player)
        {
            if (player?.Field == null)
                return;

            var anchors = player.Field
                .Where(d => d != null && d.IsBindAnchor && !string.IsNullOrWhiteSpace(d.BoundSecondFormInstanceId))
                .ToList();

            foreach (var anchor in anchors)
            {
                anchor.CanAttack = false;
                anchor.HasAttacked = true;
                anchor.CurrentAshe = Math.Max(0, anchor.CurrentAshe - 1);
                AddLog($"{anchor.Card.cardName} strains under the Bind and loses 1 Life.", LogEntryType.Effect);

                if (anchor.CurrentAshe <= 0)
                {
                    player.Field.Remove(anchor);
                    player.AshePile.Add(new CardInstance { InstanceId = anchor.InstanceId, Card = anchor.Card });
                    AddLog($"{anchor.Card.cardName} breaks as a Bind anchor.", LogEntryType.Combat);
                    ResolveDaemonDestroyed(ownerIndex, anchor);
                }
            }

            CheckSecondFormBindings(ownerIndex, player);
        }

        private void CheckSecondFormBindings(int ownerIndex, PlayerState owner)
        {
            if (owner?.Field == null)
                return;

            foreach (var secondForm in owner.Field.Where(d => d != null && d.IsSecondFormBound).ToList())
            {
                secondForm.BoundAnchorInstanceIds ??= new List<string>();
                secondForm.BoundAnchorInstanceIds = secondForm.BoundAnchorInstanceIds
                    .Where(id => owner.Field.Any(anchor => anchor != null
                        && anchor.InstanceId == id
                        && anchor.IsBindAnchor
                        && anchor.BoundSecondFormInstanceId == secondForm.InstanceId
                        && anchor.CurrentAshe > 0))
                    .ToList();

                if (secondForm.BoundAnchorInstanceIds.Count == 0)
                    CollapseSecondFormBind(ownerIndex, owner, secondForm, "all anchors are gone");
            }
        }

        private void CollapseSecondFormBind(int ownerIndex, PlayerState owner, DaemonInstance secondForm, string reason)
        {
            if (owner?.Field == null || secondForm == null || !owner.Field.Contains(secondForm))
                return;

            foreach (var anchor in owner.Field.Where(d => d != null && d.BoundSecondFormInstanceId == secondForm.InstanceId))
            {
                anchor.IsBindAnchor = false;
                anchor.BoundSecondFormInstanceId = null;
                anchor.HasAttacked = false;
                anchor.CanAttack = true;
            }

            owner.Field.Remove(secondForm);
            owner.AshePile.Add(new CardInstance { InstanceId = secondForm.InstanceId, Card = secondForm.Card });
            AddLog($"{secondForm.Card.cardName}'s Bind collapses; {reason}. It goes to the Void.", LogEntryType.Effect);
        }

        private void ReleaseSecondFormBindReferences(int ownerIndex, PlayerState owner, DaemonInstance daemon)
        {
            if (owner?.Field == null || daemon == null)
                return;

            if (daemon.IsSecondFormBound)
            {
                foreach (var anchor in owner.Field.Where(d => d != null && d.BoundSecondFormInstanceId == daemon.InstanceId))
                {
                    anchor.IsBindAnchor = false;
                    anchor.BoundSecondFormInstanceId = null;
                    anchor.HasAttacked = false;
                    anchor.CanAttack = true;
                }
                return;
            }

            if (!daemon.IsBindAnchor || string.IsNullOrWhiteSpace(daemon.BoundSecondFormInstanceId))
                return;

            var secondForm = owner.Field.FirstOrDefault(d => d != null && d.InstanceId == daemon.BoundSecondFormInstanceId && d.IsSecondFormBound);
            if (secondForm == null)
                return;

            secondForm.BoundAnchorInstanceIds ??= new List<string>();
            secondForm.BoundAnchorInstanceIds.RemoveAll(id => id == daemon.InstanceId);
            CheckSecondFormBindings(ownerIndex, owner);
        }

        private void AddRitualStep(int playerIndex, RitualChainActionType type, CardData sourceCard, string summary, bool allowsResponse = true)
        {
            if (State?.RitualChain == null)
                return;

            var entry = new RitualChainEntry
            {
                PlayerIndex = playerIndex,
                Type = type,
                SourceCard = sourceCard,
                SourceName = sourceCard != null ? sourceCard.cardName : null,
                Summary = summary,
                AllowsResponse = allowsResponse,
            };
            State.RitualChain.Add(entry);
            AddLog(summary, LogEntryType.System);

            if (type == RitualChainActionType.CardPlayed && sourceCard != null && HasKeyword(sourceCard, Keyword.Echo))
                ScheduleEcho(playerIndex, sourceCard);
        }

        private void ClearRitualChain(string reason = null)
        {
            if (State?.RitualChain == null || State.RitualChain.Count == 0)
                return;

            if (!string.IsNullOrWhiteSpace(reason))
                AddLog($"Action resolves: {reason}", LogEntryType.System);
            State.RitualChain.Clear();
        }

        private void ScheduleEcho(int ownerIndex, CardData sourceCard)
        {
            if (State?.PendingEchoes == null || sourceCard == null || !TryGetCardElement(sourceCard, out var element))
                return;

            State.PendingEchoes.Add(new EchoPendingEffect
            {
                OwnerIndex = ownerIndex,
                SourceCard = sourceCard,
                Element = element,
                TurnsRemaining = 1,
                Summary = $"{sourceCard.cardName} echoes as {element} attunement.",
            });
            AddLog($"{sourceCard.cardName} leaves an echo for next turn.", LogEntryType.Effect);
        }

        private void ResolvePendingEchoes(int ownerIndex)
        {
            if (State?.PendingEchoes == null)
                return;

            for (int i = State.PendingEchoes.Count - 1; i >= 0; i--)
            {
                var echo = State.PendingEchoes[i];
                if (echo.OwnerIndex != ownerIndex)
                    continue;

                echo.TurnsRemaining--;
                if (echo.TurnsRemaining > 0)
                    continue;

                var player = State.Players[ownerIndex];
                AddElementCommitment(player, echo.Element, echo.SourceCard);
                int gained = AddStoredSpiritEnergy(player, 1);
                AddLog($"Echo resolves: {echo.Summary} (+{gained} SE).", LogEntryType.Effect);
                State.PendingEchoes.RemoveAt(i);
            }
        }

        private void CommitElementForCard(PlayerState player, CardData card)
        {
            if (TryGetCardElement(card, out var element))
                AddElementCommitment(player, element, card);
        }

        private void AddElementCommitment(PlayerState player, Element element, CardData sourceCard)
        {
            if (player?.ElementCommitments == null)
                return;

            int index = (int)element;
            if (index < 0 || index >= player.ElementCommitments.Length)
                return;

            player.ElementCommitments[index]++;
            int count = player.ElementCommitments[index];
            AddLog($"{player.Name}'s {element} commitment rises to {count}.", LogEntryType.Effect);
            if (count == GameConstants.ElementCommitmentThreshold)
                AddLog($"{player.Name} is attuned to {element}. {element} daemons gain battle bonuses.", LogEntryType.Effect);
        }

        private static bool TryGetCardElement(CardData card, out Element element)
        {
            switch (card)
            {
                case DaemonCardData daemon:
                    element = daemon.element;
                    return true;
                case PillarCardData pillar:
                    element = pillar.element;
                    return true;
                case DomainCardData domain:
                    element = domain.effectElement;
                    return true;
                default:
                    element = default;
                    return false;
            }
        }

        private static bool HasElementCommitment(PlayerState player, Element element)
        {
            int index = (int)element;
            return player?.ElementCommitments != null
                && index >= 0
                && index < player.ElementCommitments.Length
                && player.ElementCommitments[index] >= GameConstants.ElementCommitmentThreshold;
        }

        private static bool HasKeyword(CardData card, Keyword keyword)
        {
            return card?.keywords != null && card.keywords.Contains(keyword);
        }

        private static bool ControlsMatchingElementSupport(PlayerState player, Element element)
        {
            if (player == null)
                return false;

            return player.Pillars != null && player.Pillars.Any(p => p?.Card != null && !p.Destroyed && p.Card.element == element);
        }

        private void ResolveDevourOnSummon(PlayerState player, DaemonInstance daemon)
        {
            if (player?.AsheCards == null || daemon?.Card == null || !HasKeyword(daemon.Card, Keyword.Devour))
                return;

            var sacrifice = player.AsheCards.FirstOrDefault(a => a != null && string.IsNullOrEmpty(a.AssignedDaemonInstanceId));
            if (sacrifice == null)
                return;

            player.AsheCards.Remove(sacrifice);
            player.AshePile.Add(new CardInstance { InstanceId = sacrifice.InstanceId, Card = sacrifice.Card });
            daemon.Modifiers.Add(StatModifier.FlatAttack(2, "Devour", -1));
            daemon.Modifiers.Add(StatModifier.FlatAshe(2, "Devour", -1));
            daemon.RecalculateStats();
            AddRitualStep(State.CurrentPlayer, RitualChainActionType.KeywordResponse, daemon.Card,
                $"{daemon.Card.cardName} devours {sacrifice.Card.cardName}.", false);
            AddLog($"{daemon.Card.cardName} devours {sacrifice.Card.cardName}: +2 ATK, +2 Life.", LogEntryType.Effect);
        }

        private static PillarArchetype ResolvePillarArchetype(PillarCardData pillar)
        {
            if (pillar == null)
                return PillarArchetype.Unknown;
            if (pillar.archetype != PillarArchetype.Unknown)
                return pillar.archetype;

            string text = $"{pillar.cardName} {pillar.passiveAbility} {pillar.passiveEffect?.passiveType} {pillar.onDestroyedEffect?.destroyType}".ToLowerInvariant();
            if (text.Contains("draw") || text.Contains("archive") || text.Contains("search"))
                return PillarArchetype.Archive;
            if (text.Contains("sacrifice") || text.Contains("altar") || text.Contains("drain"))
                return PillarArchetype.Altar;
            if (text.Contains("domain") || text.Contains("element") || text.Contains("buff"))
                return PillarArchetype.Domain;
            return PillarArchetype.Fortress;
        }

        private void ResolveMachineHarvest(PlayerState owner, DaemonInstance machine, string sourceName)
        {
            if (owner == null || machine == null || machine.Card.creatureType != CreatureType.Machine)
                return;

            int gainedSE = AddStoredSpiritEnergy(owner, GameConstants.MachineHarvestSEGain);
            int healed = Math.Min(GameConstants.MachineHarvestAsheGain, Math.Max(0, machine.MaxAshe - machine.CurrentAshe));
            if (healed > 0)
                machine.CurrentAshe += healed;

            if (gainedSE > 0 || healed > 0)
                AddLog($"{machine.Card.cardName} harvests {sourceName}: +{gainedSE} SE, +{healed} Life.", LogEntryType.Effect);
        }

        private int ResolveDaemonDestroyed(int ownerIndex, DaemonInstance daemon)
        {
            if (daemon == null || State == null || ownerIndex < 0 || ownerIndex >= State.Players.Length)
                return 0;

            _effects.OnDaemonDestroyed(ownerIndex, daemon);
            TryResolveUndeadConsume(daemon);

            var owner = State.Players[ownerIndex];
            ReleaseSecondFormBindReferences(ownerIndex, owner, daemon);
            int drain = daemon?.Card != null
                ? Math.Max(1, GameConstants.GetInvokerLifeLossForRarity(daemon.Card.rarity))
                : Math.Max(0, GameConstants.InvokerLifeLossPerDaemonDestroyed);
            if (drain <= 0 || owner?.Invoker == null || owner.Invoker.Hp <= 0)
                return 0;

            int absorbed = AbsorbDaemonBondDrain(ownerIndex, owner, daemon, drain);
            int invokerDrain = Math.Max(0, drain - absorbed);
            if (invokerDrain <= 0)
                return 0;

            owner.Invoker.Hp = Math.Max(0, owner.Invoker.Hp - invokerDrain);
            string rarity = daemon.Card != null ? daemon.Card.rarity.ToString() : "daemon";
            AddLog($"{owner.Name}'s Invoker loses {invokerDrain} life as {daemon.Card.cardName} falls ({rarity} bond).", LogEntryType.Combat);
            return invokerDrain;
        }

        private void TryResolveUndeadConsume(DaemonInstance defeated)
        {
            if (defeated?.Card == null || defeated.ConsumedShade || defeated.Hologram)
                return;
            if (State?.ActiveDomain?.Card == null || State.ActiveDomain.Card.effectType != DomainEffectType.UndeadConsume)
                return;

            int ownerIndex = State.ActiveDomain.Owner;
            if (ownerIndex < 0 || ownerIndex >= State.Players.Length)
                return;

            PlayerState owner = State.Players[ownerIndex];
            if (owner.Field.Count >= GameConstants.MaxFieldDaemons)
            {
                AddLog($"{State.ActiveDomain.Card.cardName} hungers, but {owner.Name}'s field is full.", LogEntryType.Effect);
                return;
            }

            var shade = CreateDaemonInstance(defeated.Card, Guid.NewGuid().ToString(), 1, 1, 0);
            shade.LaneIndex = ResolveSummonLane(owner, -1);
            shade.ConsumedShade = true;
            shade.CanAttack = true;
            owner.Field.Add(shade);
            AddLog($"{State.ActiveDomain.Card.cardName} consumes {defeated.Card.cardName}; an upside-down undead shade joins {owner.Name}'s field.", LogEntryType.Effect);
        }

        private static DaemonInstance CreateDaemonInstance(DaemonCardData card, string instanceId, int attackOverride, int lifeOverride, int asheCostOverride)
        {
            int attack = Math.Max(0, attackOverride);
            int life = Math.Max(1, lifeOverride);
            return new DaemonInstance
            {
                InstanceId = string.IsNullOrWhiteSpace(instanceId) ? Guid.NewGuid().ToString() : instanceId,
                Card = card,
                BaseAttack = attack,
                BaseAshe = life,
                CurrentAshe = life,
                MaxAshe = life,
                Attack = attack,
                AsheCost = Math.Max(0, asheCostOverride),
                CanAttack = true,
                HasAttacked = false,
            };
        }

        private int AbsorbDaemonBondDrain(int ownerIndex, PlayerState owner, DaemonInstance daemon, int drain)
        {
            return 0;
#pragma warning disable CS0162
            if (owner?.Pillars == null || daemon?.Card == null || drain <= 0)
                return 0;

            var pillar = owner.Pillars
                .Where(p => p?.Card != null && !p.Destroyed && p.CurrentHp > 0)
                .OrderByDescending(p => IsBondMatchedPillar(p, daemon))
                .ThenByDescending(p => p.CurrentHp)
                .FirstOrDefault();
            if (pillar == null)
                return 0;

            bool matched = IsBondMatchedPillar(pillar, daemon);
            var archetype = pillar.RuntimeArchetype != PillarArchetype.Unknown
                ? pillar.RuntimeArchetype
                : ResolvePillarArchetype(pillar.Card);
            int absorbLimit = GameConstants.PillarBondAbsorb
                + (matched ? GameConstants.MatchingPillarBondAbsorbBonus : 0)
                + (archetype == PillarArchetype.Fortress ? GameConstants.FortressPillarBondAbsorbBonus : 0);
            int absorbed = Math.Min(drain, Math.Min(absorbLimit, pillar.CurrentHp));
            if (absorbed <= 0)
                return 0;

            bool wasHidden = !pillar.Revealed;
            pillar.Revealed = true;
            pillar.CurrentHp = Math.Max(0, pillar.CurrentHp - absorbed);
            AddLog($"{pillar.Card.cardName} anchors {daemon.Card.cardName}'s fading bond, absorbing {absorbed} life loss.", LogEntryType.Effect);
            if (wasHidden)
                AddLog($"{owner.Name}'s {pillar.Card.cardName} is revealed as a bond anchor.", LogEntryType.Effect);

            ResolvePillarArchetypeBond(owner, pillar, archetype, wasHidden, matched);

            if (pillar.CurrentHp <= 0 && !pillar.Destroyed)
            {
                pillar.Destroyed = true;
                AddLog($"{pillar.Card.cardName} shatters under the bond strain.", LogEntryType.Combat);
                ResolvePillarDestroyed(ownerIndex, owner, pillar);
            }

            return absorbed;
#pragma warning restore CS0162
        }

        private void ResolvePillarDestroyed(int ownerIndex, PlayerState owner, PillarInstance pillar)
        {
            if (pillar == null)
                return;

            _effects.OnPillarDestroyed(ownerIndex, pillar);

            if (owner?.Invoker == null || owner.Invoker.Hp <= 0)
                return;

            int divisor = Math.Max(1, GameConstants.PillarDestroyedInvokerLifeLossDivisor);
            int lifeLoss = Math.Max(1, (owner.Invoker.MaxHp + divisor - 1) / divisor);
            int actualLoss = Math.Min(owner.Invoker.Hp, lifeLoss);
            if (actualLoss <= 0)
                return;

            owner.Invoker.Hp = Math.Max(0, owner.Invoker.Hp - actualLoss);
            string pillarName = pillar.Card != null ? pillar.Card.cardName : "a Pillar";
            AddLog($"{owner.Name}'s Invoker loses {actualLoss} life as {pillarName} collapses.", LogEntryType.Combat);
            CheckWinConditions();
        }

        private void ResolvePillarArchetypeBond(PlayerState owner, PillarInstance pillar, PillarArchetype archetype, bool wasHidden, bool matched)
        {
            if (owner == null || pillar?.Card == null)
                return;

            switch (archetype)
            {
                case PillarArchetype.Altar:
                    int gained = AddStoredSpiritEnergy(owner, GameConstants.AltarPillarBondSEGain);
                    if (gained > 0)
                        AddLog($"{pillar.Card.cardName} converts bond strain into +{gained} SE.", LogEntryType.Effect);
                    break;
                case PillarArchetype.Archive:
                    if (wasHidden && owner.Hand.Count < GameConstants.MaxHandSize && owner.Deck.Count > 0)
                    {
                        DrawCard(owner);
                        AddLog($"{pillar.Card.cardName} opens its archive: draw 1.", LogEntryType.Effect);
                    }
                    break;
                case PillarArchetype.Domain:
                    if (matched)
                        AddElementCommitment(owner, pillar.Card.element, pillar.Card);
                    break;
            }
        }

        private static bool IsBondMatchedPillar(PillarInstance pillar, DaemonInstance daemon)
        {
            return pillar?.Card != null
                && daemon?.Card != null
                && (pillar.Card.element == daemon.Card.element
                    || pillar.Card.creatureType == daemon.Card.creatureType);
        }

        private void ApplyCreatureTypeTurnStartPassives(PlayerState player)
        {
            if (player?.Field == null)
                return;

            foreach (var daemon in player.Field)
            {
                if (daemon?.Card == null)
                    continue;

                if (daemon.Card.creatureType == CreatureType.Spirit)
                {
                    int turnPair = Math.Max(0, State.TurnNumber - 1) / 2;
                    int cultivate = turnPair % GameConstants.SpiritCultivationIntervalTurnPairs == 0
                        ? Math.Min(GameConstants.SpiritCultivationAshePerTurn, Math.Max(0, daemon.MaxAshe - daemon.CurrentAshe))
                        : 0;
                    if (cultivate > 0)
                    {
                        daemon.CurrentAshe += cultivate;
                        AddLog($"{daemon.Card.cardName} cultivates +{cultivate} Life.", LogEntryType.Effect);
                    }
                }

                if (daemon.Card.creatureType == CreatureType.Undead)
                {
                    // Undead rise above curve, then decay on alternating turn pairs.
                    int turnPair = Math.Max(0, State.TurnNumber - 1) / 2;
                    if (turnPair % GameConstants.UndeadDecayIntervalTurnPairs == 1)
                    {
                        daemon.CurrentAshe = Math.Max(1, daemon.CurrentAshe - GameConstants.UndeadDecayAshePerTurn);
                        AddLog($"{daemon.Card.cardName} decays by {GameConstants.UndeadDecayAshePerTurn} Life.", LogEntryType.Effect);
                    }
                }

            }

            DaemonInstance rebuildingArtificial = player.Field
                .Where(daemon => daemon?.Card?.creatureType == CreatureType.Artificial
                    && daemon.CurrentAshe > 0 && daemon.CurrentAshe < daemon.MaxAshe)
                .OrderBy(daemon => daemon.CurrentAshe)
                .FirstOrDefault();
            if (rebuildingArtificial != null)
            {
                int regen = Math.Min(GameConstants.ArtificialRegenPerTurn,
                    rebuildingArtificial.MaxAshe - rebuildingArtificial.CurrentAshe);
                rebuildingArtificial.CurrentAshe += regen;
                AddLog($"{rebuildingArtificial.Card.cardName} rebuilds +{regen} Life.", LogEntryType.Effect);
            }

            // Ashe card upkeep: type-specific effects each turn
            if (player.AsheCards?.Count > 0)
            {
                var opponent = State.Players[1 - State.CurrentPlayer];
                foreach (var asheCard in player.AsheCards)
                {
                    if (asheCard?.Card == null || asheCard.AssignedDaemonInstanceId == null)
                        continue;

                    var daemon = player.Field.Find(d => d.InstanceId == asheCard.AssignedDaemonInstanceId);
                    if (daemon == null)
                    {
                        asheCard.AssignedDaemonInstanceId = null;
                        continue;
                    }

                    CreatureType affinity = asheCard.Card.GetAffinityType();
                    if (affinity == CreatureType.Elemental)
                    {
                        // Elemental support now waives the daemon's SE attack cost entirely.
                        continue;
                    }
                    else if (affinity == CreatureType.Machine)
                    {
                        // Flux Siphon: steal SE from the opposing reserve for each enemy daemon in play.
                        int enemyDaemonCount = opponent.Field.Count;
                        int potentialDrain = enemyDaemonCount * Math.Max(1, asheCard.Card.sePerTurn);
                        int totalDrained = Math.Min(opponent.Will, potentialDrain);
                        if (totalDrained > 0)
                        {
                            opponent.Will -= totalDrained;
                            int gained = AddStoredSpiritEnergy(player, totalDrained);
                            AddLog($"{asheCard.Card.cardName} siphons {gained} SE from {opponent.Name}'s reserve through {enemyDaemonCount} enemy daemons!", LogEntryType.Effect);
                        }
                    }
                    // Artificial (Force Field): shield is damage-triggered — see damage resolution
                    // Undead (Last Rite): resurrection is death-triggered — see combat resolution
                    // Spirit (Ascendant Bond): stat modifiers tick via TickModifiers at turn end
                }
            }
        }

        private int ResolveDomainDuration(DomainCardData domain)
        {
            if (domain != null && domain.duration > 0)
                return domain.duration;

            // If card data does not define duration, roll a short weather window.
            return _rng.Next(2, 5);
        }

        private void TickDomainDuration()
        {
            if (State?.ActiveDomain?.Card == null)
                return;

            State.ActiveDomain.TurnsRemaining--;
            if (State.ActiveDomain.TurnsRemaining > 0)
                return;

            AddLog($"Domain fades: {State.ActiveDomain.Card.cardName}.", LogEntryType.Effect);
            State.ActiveDomain = null;
        }

        private static string DescribeDomainEffect(DomainCardData domain)
        {
            if (domain == null)
                return "No effect";

            return domain.effectType switch
            {
                DomainEffectType.AtkBuffAll => $"All friendly daemons gain +{domain.effectValue} ATK",
                DomainEffectType.ElementAtkBuff => $"{domain.effectElement} daemons gain +{domain.effectValue} ATK",
                DomainEffectType.DamageAllEnd => $"End step deals {domain.effectValue} damage to all daemons",
                DomainEffectType.Protection => $"All friendly daemons gain protection {domain.effectValue}",
                DomainEffectType.ExtraDraw => $"Owner draws +{domain.effectValue}",
                DomainEffectType.PillarHeal => $"Heal all pillars by {domain.effectValue}",
                DomainEffectType.PillarRestore => $"Restore a fallen pillar",
                DomainEffectType.PoisonAll => $"Poison all enemies ({domain.effectValue})",
                DomainEffectType.BurnAll => $"Burn all enemies ({domain.effectValue})",
                DomainEffectType.WillDrain => $"Drain {domain.effectValue} stored SE",
                DomainEffectType.FreezeAll => "Freeze all enemies",
                DomainEffectType.EntangleAll => "Entangle all enemies",
                DomainEffectType.SilenceAll => "Silence all enemies",
                DomainEffectType.DebuffAtkAll => $"All enemies lose {domain.effectValue} ATK",
                DomainEffectType.WeakenAll => $"All enemies are weakened ({domain.effectValue})",
                DomainEffectType.StealWill => $"Steal {domain.effectValue} stored SE",
                DomainEffectType.DamageReductionAll => $"All friendly daemons reduce damage by {domain.effectValue}",
                DomainEffectType.HasteAll => "All friendly daemons gain haste",
                DomainEffectType.PetrifyCycle => "Enemy daemons petrify every other turn",
                DomainEffectType.MachineGlitch => "Enemy Machine and Artificial daemons may glitch and deal no damage",
                DomainEffectType.RelicLock => "Enemy cannot equip Relics",
                DomainEffectType.SpiritDrain => $"Enemy Spirits drain {Math.Max(1, domain.effectValue)} SE each turn",
                DomainEffectType.TypeNull => "Elemental advantage is suppressed",
                DomainEffectType.ArtificialCorrupt => $"Enemy Artificial daemons lose {Math.Max(1, domain.effectValue)} Life each turn",
                DomainEffectType.UndeadConsume => "Defeated daemons return as 1/1 undead shades for the Domain owner",
                DomainEffectType.MachinePropagate => "Nanobots drain enemy Life and convert it into SE",
                DomainEffectType.SpiritDefile => "Enemy daemons become Darkened and may fail control checks",
                DomainEffectType.ArtificialConstruct => "Create a half-power hologram copy each owner turn",
                _ => domain.effectType.ToString(),
            };
        }

        private static string DescribeMaskEffect(MaskCardData mask)
        {
            if (mask == null)
                return "No effect";

            if (mask.summonsLegendaryDaemon && mask.legendaryDaemonToSummon != null)
                return $"Summon {mask.legendaryDaemonToSummon.cardName} from hand or deck";

            if (mask.grantsRangedAttacks || mask.overridesAttackPattern)
            {
                DaemonAttackPattern pattern = mask.grantsRangedAttacks ? DaemonAttackPattern.Ranged : mask.grantedAttackPattern;
                string letter = pattern switch
                {
                    DaemonAttackPattern.Ranged => "R",
                    DaemonAttackPattern.Sweep => "S",
                    DaemonAttackPattern.Guard => "G",
                    _ => "D",
                };
                string patternDetail = $"Attack letter becomes {letter}";
                if (pattern == DaemonAttackPattern.Sweep && mask.splashStatusValue > 0)
                    patternDetail += $"; splash applies {mask.splashStatusEffect}";
                return mask.duration > 0 ? $"{patternDetail} for {mask.duration} turns" : patternDetail;
            }

            string detail = mask.effectType switch
            {
                MaskEffectType.AtkBoost => $"+{mask.effectValue} ATK",
                MaskEffectType.AsheBoost => $"+{mask.effectValue} Life",
                MaskEffectType.Stealth => "Stealth",
                MaskEffectType.Haste => "Haste",
                MaskEffectType.Thorns => $"Thorns {mask.effectValue}",
                MaskEffectType.Entangle => "Entangle",
                MaskEffectType.Poison => $"Poison {mask.effectValue}",
                MaskEffectType.Burn => $"Burn {mask.effectValue}",
                MaskEffectType.DamageReduction => $"Damage reduction {mask.effectValue}",
                _ => mask.effectType.ToString(),
            };

            return mask.duration > 0 ? $"{detail} for {mask.duration} turns" : detail;
        }

        private static string DescribePillarEffect(PillarCardData pillar)
        {
            if (pillar == null)
                return "No effect";

            if (!string.IsNullOrWhiteSpace(pillar.passiveAbility))
                return pillar.passiveAbility.Trim();

            if (pillar.passiveEffect != null)
                return $"Passive: {pillar.passiveEffect.passiveType} {pillar.passiveEffect.value}";

            return "Revealed";
        }

        private int _lastSERoll;
        /// <summary>The Source income from the most recent turn start, for UI animation.</summary>
        public int LastSERoll => _lastSERoll;

        // ─── Utilities ─────────────────────────────────────────────
        private void ShuffleDeck(PlayerState player)
        {
            var deck = player.Deck;
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }

        private static int FindFusionSealIndex(PlayerState player)
        {
            if (player?.Hand == null)
                return -1;

            for (int i = 0; i < player.Hand.Count; i++)
            {
                var card = player.Hand[i].Card;
                if (card != null && card.category == CardCategory.Seal && card.cardId == "fusion_seal")
                    return i;
            }

            return -1;
        }

        private static bool CardsMatchForFusion(DaemonCardData first, DaemonCardData second)
        {
            if (first == null || second == null)
                return false;

            if (!string.IsNullOrEmpty(first.cardId) && !string.IsNullOrEmpty(second.cardId))
                return string.Equals(first.cardId, second.cardId, StringComparison.Ordinal);

            return string.Equals(first.cardName, second.cardName, StringComparison.Ordinal);
        }

        private static bool CardsShareDeckIdentity(CardInstance first, CardInstance second)
        {
            if (first?.Card == null || second?.Card == null)
                return false;

            if (!string.IsNullOrEmpty(first.Card.cardId) && !string.IsNullOrEmpty(second.Card.cardId))
                return string.Equals(first.Card.cardId, second.Card.cardId, StringComparison.Ordinal);

            return string.Equals(first.Card.cardName, second.Card.cardName, StringComparison.Ordinal);
        }

        private static DaemonCardData CreateFusedDaemonCard(DaemonCardData primary, DaemonCardData secondary)
        {
            var fused = UnityEngine.ScriptableObject.CreateInstance<DaemonCardData>();
            string baseId = !string.IsNullOrEmpty(primary.cardId) ? primary.cardId : primary.cardName;
            fused.cardId = $"{baseId}_fusion";
            fused.cardName = $"{primary.cardName} Fusion";
            fused.category = CardCategory.Daemon;
            fused.rarity = primary.rarity;
            fused.artwork = primary.artwork;
            fused.fullArt = primary.fullArt;
            fused.description = $"Fusion-sealed from two {primary.cardName} daemons.";
            fused.flavorText = primary.flavorText;
            fused.willCost = primary.willCost;
            fused.element = primary.element;
            fused.creatureType = primary.creatureType;
            fused.attack = primary.attack + secondary.attack + 1;
            fused.ashe = primary.ashe + secondary.ashe + 2;
            fused.asheCost = primary.asheCost + secondary.asheCost;
            fused.evolvesTo = null;
            fused.evolutionCost = 0;
            fused.ability = primary.ability;
            return fused;
        }

        private void AddLog(string message, LogEntryType type)
        {
            var entry = new LogEntry
            {
                Turn = State.TurnNumber,
                Player = State.CurrentPlayer,
                Message = message,
                Type = type,
            };
            State.LastAction = message;
            State.Log.Add(entry);
            OnLogEntry?.Invoke(entry);
        }

        /// <summary>
        /// Checks the win conditions at any time. A player wins by reducing the
        /// opposing Invoker's HP to zero. Pillars no longer decide story-card battles.
        /// </summary>
        public void CheckWinConditions()
        {
            for (int i = 0; i < 2; i++)
            {
                // Invoker at 0 HP
                if (State.Players[i].Invoker.Hp <= 0)
                {
                    State.Winner = 1 - i;
                    State.GameOver = true;
                    AddLog($"{State.Players[1 - i].Name} wins!", LogEntryType.System);
                    OnGameOver?.Invoke(1 - i, "Invoker defeated!");
                    return;
                }
            }
        }
    }
}
