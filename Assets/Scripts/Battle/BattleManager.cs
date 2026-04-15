// ═══════════════════════════════════════════════════════
// DUAL CRAFT — Battle Manager (Game Engine Core)
// Handles turn flow, action processing, win conditions
// ═══════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DualCraft.Battle
{
    using Core;
    using Cards;
    using Effects;

    /// <summary>
    /// Core engine for the Duel Craft TCG.  Manages the game state, enforces
    /// phase order, processes player actions, resolves combat, and checks
    /// victory conditions.  This implementation incorporates the rules
    /// described in the Dual Craft TCG rules summary: summoning sickness,
    /// attack ordering (Daemons → Pillars → Conjuror), dice‑based damage
    /// modifiers, elemental/creature matchups, will (mana) progression and
    /// max will caps, and pillar activation.
    /// </summary>
    public class BattleManager
    {
        public GameState State { get; private set; }
        public event Action<GameState> OnStateChanged;
        public event Action<LogEntry> OnLogEntry;
        public event Action<int, string> OnGameOver;
        /// <summary>
        /// Fired when combat requires a dice roll.  The UI layer can hook
        /// into this event to animate the roll and display the result.
        /// Parameters: attacker name, minimum success threshold, callback
        /// delivering roll value and whether the roll met the threshold.
        /// </summary>
        public event Action<string, int, Action<int, bool>> OnDiceRollRequested;

        private readonly CardDatabase _cardDb;
        private EffectResolver _effects;
        public EffectResolver Effects => _effects;

        public BattleManager(CardDatabase cardDb)
        {
            _cardDb = cardDb;
        }

        /// <summary>
        /// Initializes a new game.  Builds player states from deck data,
        /// shuffles each deck, draws starting hands, and triggers the initial
        /// state change event.  Conjurors start at full health and players
        /// begin with 1 will, which increases by one each turn up to a
        /// maximum defined in GameConstants.
        /// </summary>
        public void InitGame(string p1Name, DeckData p1Deck, string p2Name, DeckData p2Deck)
        {
            State = new GameState
            {
                RoomId = Guid.NewGuid().ToString(),
                CurrentPlayer = 0,
                Phase = GamePhase.Draw,
                TurnNumber = 1,
            };
            State.Players[0] = CreatePlayerState("p1", p1Name, p1Deck);
            State.Players[1] = CreatePlayerState("p2", p2Name, p2Deck);
            // Shuffle decks
            ShuffleDeck(State.Players[0]);
            ShuffleDeck(State.Players[1]);
            // Draw starting hands
            for (int i = 0; i < GameConstants.StartingHandSize; i++)
            {
                DrawCard(State.Players[0]);
                DrawCard(State.Players[1]);
            }
            // Initialize the effect resolver
            _effects = new EffectResolver(State);
            _effects.OnEffectLog += msg => AddLog(msg, LogEntryType.Effect);

            AddLog("Game started!", LogEntryType.System);
            OnStateChanged?.Invoke(State);
        }

        private PlayerState CreatePlayerState(string id, string name, DeckData deckData)
        {
            var player = new PlayerState
            {
                Id = id,
                Name = name,
                Conjuror = new ConjurorState
                {
                    Hp = GameConstants.ConjurorMaxHp,
                    MaxHp = GameConstants.ConjurorMaxHp,
                },
                Will = GameConstants.StartingWill,
                MaxWill = GameConstants.StartingWill,
            };
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
            // Place pillars from deck data; pillars start face down and are not drawn from hand
            if (deckData.pillars != null)
            {
                foreach (var entry in deckData.pillars)
                {
                    if (entry.card is PillarCardData pillar)
                    {
                        for (int i = 0; i < entry.count; i++)
                        {
                            player.Pillars.Add(new PillarInstance
                            {
                                InstanceId = Guid.NewGuid().ToString(),
                                Card = pillar,
                                CurrentHp = pillar.hp,
                                MaxHp = pillar.hp,
                                Loyalty = pillar.loyalty,
                                Destroyed = false,
                                Revealed = false,
                            });
                        }
                    }
                }
            }
            return player;
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
            return action switch
            {
                DrawCardAction => HandleDraw(player),
                PlayDaemonAction pda => HandlePlayDaemon(player, pda.HandIndex),
                PlayDomainAction pdo => HandlePlayDomain(player, playerIndex, pdo.HandIndex),
                PlayMaskAction pma => HandlePlayMask(player, pma.HandIndex, pma.TargetDaemonIndex),
                SetSealAction ssa => HandleSetSeal(player, ssa.HandIndex),
                PlayDispelAction pdi => HandlePlayDispel(player, opponent, playerIndex, pdi),
                AttackAction aa => HandleAttack(player, opponent, playerIndex, aa),
                ActivatePillarAction apa => HandleActivatePillar(player, apa.PillarIndex, apa.AbilityIndex),
                NextPhaseAction => AdvancePhase(),
                EndTurnAction => EndTurn(),
                _ => false,
            };
        }

        // ─── Draw Phase ───────────────────────────────────────────────
        private bool HandleDraw(PlayerState player)
        {
            if (State.Phase != GamePhase.Draw)
                return false;
            DrawCard(player);
            // Transition to Main 1 after drawing one card
            State.Phase = GamePhase.Main1;
            OnStateChanged?.Invoke(State);
            return true;
        }

        private void DrawCard(PlayerState player)
        {
            if (player.Deck.Count == 0)
                return;
            if (player.Hand.Count >= GameConstants.MaxHandSize)
                return;
            var card = player.Deck[0];
            player.Deck.RemoveAt(0);
            player.Hand.Add(card);
        }

        // ─── Play Daemon ─────────────────────────────────────────────
        private bool HandlePlayDaemon(PlayerState player, int handIndex)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;
            if (player.Field.Count >= GameConstants.MaxFieldDaemons)
                return false;
            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not DaemonCardData daemon)
                return false;
            int cost = daemon.GetWillCost();
            // Apply cost reduction from effects
            if (player.CostReduction > 0)
            {
                int reduction = Mathf.Min(cost, player.CostReduction);
                cost -= reduction;
                player.CostReduction -= reduction;
            }
            if (player.Will < cost)
                return false;
            player.Will -= cost;
            player.Hand.RemoveAt(handIndex);
            // Create runtime instance with summoning sickness
            var instance = new DaemonInstance
            {
                InstanceId = cardInstance.InstanceId,
                Card = daemon,
                CurrentAshe = daemon.ashe,
                MaxAshe = daemon.ashe,
                Attack = daemon.attack,
                AsheCost = daemon.asheCost,
                CanAttack = false, // cannot attack until next turn
                HasAttacked = false,
            };
            player.Field.Add(instance);
            AddLog($"{player.Name} summoned {daemon.cardName}!", LogEntryType.Action);

            // Fire OnSummon effects
            _effects.OnDaemonSummoned(State.CurrentPlayer, instance);

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
                _effects.OnDaemonDestroyed(1 - State.CurrentPlayer, dead);

            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Play Domain ─────────────────────────────────────────────
        private bool HandlePlayDomain(PlayerState player, int playerIndex, int handIndex)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;
            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not DomainCardData domain)
                return false;
            int cost = domain.GetWillCost();
            if (player.Will < cost)
                return false;
            player.Will -= cost;
            player.Hand.RemoveAt(handIndex);
            State.ActiveDomain = new ActiveDomain { Card = domain, Owner = playerIndex };
            AddLog($"{player.Name} played {domain.cardName}!", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Play Mask ───────────────────────────────────────────────
        private bool HandlePlayMask(PlayerState player, int handIndex, int targetIndex)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;
            if (targetIndex < 0 || targetIndex >= player.Field.Count)
                return false;
            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not MaskCardData mask)
                return false;
            int cost = mask.GetWillCost();
            if (player.Will < cost)
                return false;
            player.Will -= cost;
            player.Hand.RemoveAt(handIndex);
            player.Field[targetIndex].Masks.Add(new MaskInstance
            {
                Card = mask,
                TurnsRemaining = mask.duration,
            });
            AddLog($"{player.Name} equipped {mask.cardName} to {player.Field[targetIndex].Card.cardName}!", LogEntryType.Action);

            // Apply mask effect immediately
            var targetDaemon = player.Field[targetIndex];
            if (mask.effectType == MaskEffectType.Haste)
                targetDaemon.CanAttack = true;
            _effects.ApplyMaskEffect(State.CurrentPlayer, targetDaemon, mask);

            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Set Seal ────────────────────────────────────────────────
        private bool HandleSetSeal(PlayerState player, int handIndex)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2)
                return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count)
                return false;
            if (player.SealZone.Count >= GameConstants.MaxSeals)
                return false;
            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not SealCardData seal)
                return false;
            int cost = seal.GetWillCost();
            if (player.Will < cost)
                return false;
            player.Will -= cost;
            player.Hand.RemoveAt(handIndex);
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
        private bool HandlePlayDispel(PlayerState player, PlayerState opponent, int playerIndex, PlayDispelAction action)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2)
                return false;
            if (action.HandIndex < 0 || action.HandIndex >= player.Hand.Count)
                return false;
            var cardInstance = player.Hand[action.HandIndex];
            if (cardInstance.Card is not DispelCardData dispel)
                return false;
            int cost = dispel.GetWillCost();
            if (player.Will < cost)
                return false;
            player.Will -= cost;
            player.Hand.RemoveAt(action.HandIndex);
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
                    }
                    break;
            }

            // Apply counter effect if dispel has one
            if (dispel.counterEffect != null && !string.IsNullOrEmpty(dispel.counterEffect.effectType))
            {
                switch (dispel.counterEffect.effectType.ToLowerInvariant())
                {
                    case "damage-owner":
                        opponent.Conjuror.Hp -= dispel.counterEffect.value;
                        AddLog($"Counter effect: {dispel.counterEffect.value} damage!", LogEntryType.Effect);
                        break;
                    case "draw-cards":
                        for (int i = 0; i < dispel.counterEffect.value; i++)
                            DrawCard(player);
                        AddLog($"Counter effect: draw {dispel.counterEffect.value}!", LogEntryType.Effect);
                        break;
                    case "heal-conjuror":
                        player.Conjuror.Hp = Mathf.Min(
                            player.Conjuror.Hp + dispel.counterEffect.value, player.Conjuror.MaxHp);
                        AddLog($"Counter effect: heal {dispel.counterEffect.value}!", LogEntryType.Effect);
                        break;
                }
            }

            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Attack ────────────────────────────────────────────────
        private bool HandleAttack(PlayerState player, PlayerState opponent, int playerIndex, AttackAction action)
        {
            if (State.Phase != GamePhase.Battle)
                return false;
            if (action.AttackerIndex < 0 || action.AttackerIndex >= player.Field.Count)
                return false;
            var attacker = player.Field[action.AttackerIndex];
            if (!attacker.CanAttack || attacker.HasAttacked || attacker.Frozen || attacker.Entangled)
                return false;

            // Fire OnAttack effects and check seals
            _effects.OnDaemonAttacking(playerIndex, attacker);
            if (_effects.ActionNegated)
            {
                AddLog("Attack was negated by a Seal!", LogEntryType.Effect);
                attacker.HasAttacked = true;
                OnStateChanged?.Invoke(State);
                return true;
            }
            // ─── Enforce attack order: Daemons → Pillars → Conjuror ───
            bool opponentHasDaemons = opponent.Field.Count > 0;
            bool opponentHasPillars = opponent.Pillars.Exists(p => !p.Destroyed);
            switch (action.Target)
            {
                case TargetType.Pillar:
                    if (opponentHasDaemons)
                    {
                        AddLog("Cannot attack Pillars while enemy Daemons are on the field!", LogEntryType.System);
                        return false;
                    }
                    break;
                case TargetType.Conjuror:
                    if (opponentHasPillars)
                    {
                        AddLog("Cannot attack the Conjuror while enemy Pillars still stand!", LogEntryType.System);
                        return false;
                    }
                    break;
            }
            // Roll a 6-sided die for damage modifier
            int diceRoll = UnityEngine.Random.Range(1, 7);
            float diceMod = diceRoll switch
            {
                6 => 1.5f, // Critical hit
                5 => 1.25f, // Strong hit
                4 => 1.1f, // Solid hit
                3 => 1.0f, // Normal hit
                2 => 0.85f, // Weak hit
                1 => 0.7f, // Glancing blow
                _ => 1.0f,
            };
            int baseDamage = attacker.Attack;
            // Notify UI about the dice roll; callback currently unused
            bool diceSuccess = diceRoll >= 4;
            string rollDesc = diceRoll >= 5 ? "CRITICAL!" : diceRoll >= 4 ? "Hit!" : "Weak...";
            OnDiceRollRequested?.Invoke(attacker.Card.cardName, 4, (roll, success) => { /* UI hook */ });
            switch (action.Target)
            {
                case TargetType.Daemon:
                    if (action.TargetIndex < 0 || action.TargetIndex >= opponent.Field.Count)
                        return false;
                    var targetDaemon = opponent.Field[action.TargetIndex];
                    // Element and creature matchups
                    float elemMult = ElementSystem.GetElementMatchup(attacker.Card.element, targetDaemon.Card.element);
                    float creatMult = ElementSystem.GetCreatureMatchup(attacker.Card.creatureType, targetDaemon.Card.creatureType);
                    int finalDamage = Mathf.RoundToInt(baseDamage * elemMult * creatMult * diceMod);
                    // Apply shield absorption
                    if (targetDaemon.ShieldAmount > 0)
                    {
                        int absorbed = Mathf.Min(finalDamage, targetDaemon.ShieldAmount);
                        targetDaemon.ShieldAmount -= absorbed;
                        finalDamage -= absorbed;
                        if (absorbed > 0)
                            AddLog($"Shield absorbs {absorbed} damage!", LogEntryType.Effect);
                    }
                    targetDaemon.CurrentAshe -= finalDamage;
                    string diceTag = diceRoll >= 5 ? " ★" : diceRoll <= 2 ? " ↓" : "";
                    AddLog($"{attacker.Card.cardName} attacks {targetDaemon.Card.cardName} for {finalDamage} [{diceRoll}]{diceTag}!", LogEntryType.Combat);

                    // Thorns: reflect damage back to attacker
                    if (targetDaemon.ThornsDamage > 0)
                    {
                        attacker.CurrentAshe -= targetDaemon.ThornsDamage;
                        AddLog($"Thorns reflect {targetDaemon.ThornsDamage} damage!", LogEntryType.Effect);
                    }

                    // Fire OnDamaged effects
                    if (finalDamage > 0)
                        _effects.OnDaemonDamaged(1 - playerIndex, targetDaemon, finalDamage);

                    if (targetDaemon.CurrentAshe <= 0)
                    {
                        opponent.Field.RemoveAt(action.TargetIndex);
                        opponent.AshePile.Add(new CardInstance { InstanceId = targetDaemon.InstanceId, Card = targetDaemon.Card });
                        AddLog($"{targetDaemon.Card.cardName} was destroyed!", LogEntryType.Combat);
                        _effects.OnDaemonDestroyed(1 - playerIndex, targetDaemon);
                    }
                    // Check if attacker died from thorns
                    if (attacker.CurrentAshe <= 0)
                    {
                        int atkIdx = player.Field.IndexOf(attacker);
                        if (atkIdx >= 0)
                        {
                            player.Field.RemoveAt(atkIdx);
                            player.AshePile.Add(new CardInstance { InstanceId = attacker.InstanceId, Card = attacker.Card });
                            AddLog($"{attacker.Card.cardName} was destroyed by thorns!", LogEntryType.Combat);
                            _effects.OnDaemonDestroyed(playerIndex, attacker);
                        }
                    }
                    break;
                case TargetType.Pillar:
                    if (action.TargetIndex < 0 || action.TargetIndex >= opponent.Pillars.Count)
                        return false;
                    var pillar = opponent.Pillars[action.TargetIndex];
                    if (pillar.Destroyed)
                        return false;
                    // Reveal face‑down pillar on first attack
                    if (!pillar.Revealed)
                    {
                        pillar.Revealed = true;
                        AddLog($"Pillar revealed: {pillar.Card.cardName}!", LogEntryType.Effect);
                    }
                    int pillarDmg = Mathf.RoundToInt(baseDamage * diceMod);
                    pillar.CurrentHp -= pillarDmg;
                    AddLog($"{attacker.Card.cardName} attacks Pillar {pillar.Card.cardName} for {pillarDmg} [{diceRoll}]!", LogEntryType.Combat);
                    if (pillar.CurrentHp <= 0)
                    {
                        pillar.Destroyed = true;
                        AddLog($"Pillar {pillar.Card.cardName} was destroyed!", LogEntryType.Combat);
                        _effects.OnPillarDestroyed(1 - playerIndex, pillar);
                    }
                    break;
                case TargetType.Conjuror:
                    int conjDmg = Mathf.RoundToInt(baseDamage * diceMod);
                    opponent.Conjuror.Hp -= conjDmg;
                    AddLog($"{attacker.Card.cardName} strikes the Conjuror for {conjDmg} [{diceRoll}]!", LogEntryType.Combat);
                    if (opponent.Conjuror.Hp <= 0)
                    {
                        opponent.Conjuror.Hp = 0;
                        State.Winner = playerIndex;
                        State.GameOver = true;
                        AddLog($"{player.Name} wins!", LogEntryType.System);
                        OnGameOver?.Invoke(playerIndex, $"{player.Name} wins!");
                    }
                    break;
            }
            attacker.HasAttacked = true;
            OnStateChanged?.Invoke(State);
            return true;
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
            AddLog($"{player.Name} activated {pillar.Card.cardName}'s {ability.abilityName}!", LogEntryType.Action);

            // Execute the ability via the effect resolver
            _effects.ActivatePillarAbility(State.CurrentPlayer, pillar, ability);

            // Clean up dead daemons from effects
            for (int pi = 0; pi < 2; pi++)
            {
                var dead = _effects.CleanupDead(pi);
                foreach (var d in dead)
                    _effects.OnDaemonDestroyed(pi, d);
            }

            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Phase Management ───────────────────────────────────────
        private bool AdvancePhase()
        {
            State.Phase = State.Phase switch
            {
                GamePhase.Draw => GamePhase.Main1,
                GamePhase.Main1 => GamePhase.Battle,
                GamePhase.Battle => GamePhase.Main2,
                GamePhase.Main2 => GamePhase.End,
                _ => GamePhase.End,
            };
            // When entering the battle phase, reset the HasAttacked flag on daemons
            if (State.Phase == GamePhase.Battle)
            {
                var player = State.Players[State.CurrentPlayer];
                foreach (var d in player.Field)
                {
                    d.HasAttacked = false;
                }
            }
            OnStateChanged?.Invoke(State);
            return true;
        }

        private bool EndTurn()
        {
            var currentPlayer = State.Players[State.CurrentPlayer];
            // Reset daemon attack flags and enable CanAttack for next turn
            foreach (var d in currentPlayer.Field)
            {
                d.CanAttack = true;
                d.HasAttacked = false;
            }
            // Reset pillar ability usage
            foreach (var p in currentPlayer.Pillars)
            {
                p.AbilityUsedThisTurn = false;
            }
            // Decrement mask durations and remove expired masks
            foreach (var d in currentPlayer.Field)
            {
                d.Masks.RemoveAll(m =>
                {
                    m.TurnsRemaining--;
                    return m.TurnsRemaining <= 0;
                });
            }
            // Switch active player and increment turn count
            State.CurrentPlayer = 1 - State.CurrentPlayer;
            State.TurnNumber++;
            // Increase the new player's max will up to the cap and set current will to max
            var nextPlayer = State.Players[State.CurrentPlayer];
            if (nextPlayer.MaxWill < GameConstants.MaxWill)
                nextPlayer.MaxWill++;
            nextPlayer.Will = nextPlayer.MaxWill;

            // Fire turn-end effects for the player who just ended
            _effects.OnTurnEnd(1 - State.CurrentPlayer);

            State.Phase = GamePhase.Draw;

            // Fire turn-start effects for the new player
            _effects.OnTurnStart(State.CurrentPlayer);

            // Clean up any daemons killed by turn effects
            for (int pi = 0; pi < 2; pi++)
            {
                var dead = _effects.CleanupDead(pi);
                foreach (var d in dead)
                    _effects.OnDaemonDestroyed(pi, d);
            }

            AddLog($"Turn {State.TurnNumber} — {nextPlayer.Name}'s turn.", LogEntryType.System);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Utilities ─────────────────────────────────────────────
        private void ShuffleDeck(PlayerState player)
        {
            var deck = player.Deck;
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
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
            State.Log.Add(entry);
            OnLogEntry?.Invoke(entry);
        }

        /// <summary>
        /// Checks the win conditions at any time (e.g., after combat or pillar destruction).
        /// A player wins if they destroy all four of the opponent's pillars or reduce the
        /// opposing Conjuror's HP to zero.  If a win is detected, the State.GameOver
        /// flag is set and the OnGameOver event is fired.
        /// </summary>
        public void CheckWinConditions()
        {
            for (int i = 0; i < 2; i++)
            {
                // All pillars destroyed
                if (State.Players[i].Pillars.All(p => p.Destroyed))
                {
                    State.Winner = 1 - i;
                    State.GameOver = true;
                    AddLog($"{State.Players[1 - i].Name} wins by destroying all pillars!", LogEntryType.System);
                    OnGameOver?.Invoke(1 - i, "All pillars destroyed!");
                    return;
                }
                // Conjuror at 0 HP
                if (State.Players[i].Conjuror.Hp <= 0)
                {
                    State.Winner = 1 - i;
                    State.GameOver = true;
                    AddLog($"{State.Players[1 - i].Name} wins!", LogEntryType.System);
                    OnGameOver?.Invoke(1 - i, "Conjuror defeated!");
                    return;
                }
            }
        }
    }
}