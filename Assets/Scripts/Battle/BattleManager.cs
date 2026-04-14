// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Battle Manager (Game Engine Core)
//  Handles turn flow, action processing, win conditions
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DualCraft.Battle
{
    using Core;
    using Cards;

    public class BattleManager
    {
        public GameState State { get; private set; }
        public event Action<GameState> OnStateChanged;
        public event Action<LogEntry> OnLogEntry;
        public event Action<int, string> OnGameOver;

        /// <summary>Fired when combat needs a dice roll. (attackerName, threshold, callback(roll, success))</summary>
        public event Action<string, int, Action<int, bool>> OnDiceRollRequested;

        private readonly CardDatabase _cardDb;

        public BattleManager(CardDatabase cardDb)
        {
            _cardDb = cardDb;
        }

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

            // Build deck from DeckData entries
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

            // Place pillars
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
                            });
                        }
                    }
                }
            }

            return player;
        }

        // ─── Action Processing ────────────────────────────
        public bool ProcessAction(int playerIndex, GameAction action)
        {
            if (State.GameOver) return false;
            if (playerIndex != State.CurrentPlayer) return false;

            var player = State.Players[playerIndex];
            var opponent = State.Players[1 - playerIndex];

            switch (action)
            {
                case DrawCardAction:
                    return HandleDraw(player);

                case PlayDaemonAction pda:
                    return HandlePlayDaemon(player, pda.HandIndex);

                case PlayDomainAction pdo:
                    return HandlePlayDomain(player, playerIndex, pdo.HandIndex);

                case PlayMaskAction pma:
                    return HandlePlayMask(player, pma.HandIndex, pma.TargetDaemonIndex);

                case SetSealAction ssa:
                    return HandleSetSeal(player, ssa.HandIndex);

                case PlayDispelAction pdi:
                    return HandlePlayDispel(player, opponent, playerIndex, pdi);

                case AttackAction aa:
                    return HandleAttack(player, opponent, playerIndex, aa);

                case ActivatePillarAction apa:
                    return HandleActivatePillar(player, apa.PillarIndex, apa.AbilityIndex);

                case NextPhaseAction:
                    return AdvancePhase();

                case EndTurnAction:
                    return EndTurn();

                default:
                    return false;
            }
        }

        // ─── Draw ─────────────────────────────────────────
        private bool HandleDraw(PlayerState player)
        {
            if (State.Phase != GamePhase.Draw) return false;
            DrawCard(player);
            State.Phase = GamePhase.Main1;
            OnStateChanged?.Invoke(State);
            return true;
        }

        private void DrawCard(PlayerState player)
        {
            if (player.Deck.Count == 0) return;
            if (player.Hand.Count >= GameConstants.MaxHandSize) return;

            var card = player.Deck[0];
            player.Deck.RemoveAt(0);
            player.Hand.Add(card);
        }

        // ─── Play Daemon ──────────────────────────────────
        private bool HandlePlayDaemon(PlayerState player, int handIndex)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2) return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count) return false;
            if (player.Field.Count >= GameConstants.MaxFieldDaemons) return false;

            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not DaemonCardData daemon) return false;

            int cost = daemon.GetWillCost();
            if (player.Will < cost) return false;

            player.Will -= cost;
            player.Hand.RemoveAt(handIndex);

            var instance = new DaemonInstance
            {
                InstanceId = cardInstance.InstanceId,
                Card = daemon,
                CurrentAshe = daemon.ashe,
                MaxAshe = daemon.ashe,
                Attack = daemon.attack,
                AsheCost = daemon.asheCost,
                CanAttack = false, // summoning sickness
                HasAttacked = false,
            };

            player.Field.Add(instance);
            AddLog($"{player.Name} summoned {daemon.cardName}!", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Play Domain ──────────────────────────────────
        private bool HandlePlayDomain(PlayerState player, int playerIndex, int handIndex)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2) return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count) return false;

            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not DomainCardData domain) return false;

            int cost = domain.GetWillCost();
            if (player.Will < cost) return false;

            player.Will -= cost;
            player.Hand.RemoveAt(handIndex);
            State.ActiveDomain = new ActiveDomain { Card = domain, Owner = playerIndex };

            AddLog($"{player.Name} played {domain.cardName}!", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Play Mask ────────────────────────────────────
        private bool HandlePlayMask(PlayerState player, int handIndex, int targetIndex)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2) return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count) return false;
            if (targetIndex < 0 || targetIndex >= player.Field.Count) return false;

            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not MaskCardData mask) return false;

            int cost = mask.GetWillCost();
            if (player.Will < cost) return false;

            player.Will -= cost;
            player.Hand.RemoveAt(handIndex);

            player.Field[targetIndex].Masks.Add(new MaskInstance
            {
                Card = mask,
                TurnsRemaining = mask.duration,
            });

            AddLog($"{player.Name} equipped {mask.cardName} to {player.Field[targetIndex].Card.cardName}!", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Set Seal ─────────────────────────────────────
        private bool HandleSetSeal(PlayerState player, int handIndex)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2) return false;
            if (handIndex < 0 || handIndex >= player.Hand.Count) return false;
            if (player.SealZone.Count >= GameConstants.MaxSeals) return false;

            var cardInstance = player.Hand[handIndex];
            if (cardInstance.Card is not SealCardData seal) return false;

            int cost = seal.GetWillCost();
            if (player.Will < cost) return false;

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

        // ─── Play Dispel ──────────────────────────────────
        private bool HandlePlayDispel(PlayerState player, PlayerState opponent, int playerIndex, PlayDispelAction action)
        {
            if (State.Phase != GamePhase.Main1 && State.Phase != GamePhase.Main2) return false;
            if (action.HandIndex < 0 || action.HandIndex >= player.Hand.Count) return false;

            var cardInstance = player.Hand[action.HandIndex];
            if (cardInstance.Card is not DispelCardData dispel) return false;

            int cost = dispel.GetWillCost();
            if (player.Will < cost) return false;

            player.Will -= cost;
            player.Hand.RemoveAt(action.HandIndex);

            AddLog($"{player.Name} cast {dispel.cardName}!", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Attack ───────────────────────────────────────
        private bool HandleAttack(PlayerState player, PlayerState opponent, int playerIndex, AttackAction action)
        {
            if (State.Phase != GamePhase.Battle) return false;
            if (action.AttackerIndex < 0 || action.AttackerIndex >= player.Field.Count) return false;

            var attacker = player.Field[action.AttackerIndex];
            if (!attacker.CanAttack || attacker.HasAttacked || attacker.Frozen) return false;

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
                6 => 1.5f,   // Critical hit
                5 => 1.25f,  // Strong hit
                4 => 1.1f,   // Solid hit
                3 => 1.0f,   // Normal hit
                2 => 0.85f,  // Weak hit
                1 => 0.7f,   // Glancing blow
                _ => 1.0f,
            };

            int baseDamage = attacker.Attack;

            // Notify UI about the dice roll for animation
            bool diceSuccess = diceRoll >= 4;
            string rollDesc = diceRoll >= 5 ? "CRITICAL!" : diceRoll >= 4 ? "Hit!" : "Weak...";
            OnDiceRollRequested?.Invoke(attacker.Card.cardName, 4,
                (roll, success) => { /* UI callback for animation */ });

            switch (action.Target)
            {
                case TargetType.Daemon:
                    if (action.TargetIndex < 0 || action.TargetIndex >= opponent.Field.Count) return false;
                    var target = opponent.Field[action.TargetIndex];

                    // Element matchup
                    float elemMult = ElementSystem.GetElementMatchup(attacker.Card.element, target.Card.element);
                    float creatMult = ElementSystem.GetCreatureMatchup(attacker.Card.creatureType, target.Card.creatureType);
                    int finalDamage = Mathf.RoundToInt(baseDamage * elemMult * creatMult * diceMod);

                    target.CurrentAshe -= finalDamage;

                    string diceTag = diceRoll >= 5 ? " ★" : diceRoll <= 2 ? " ↓" : "";
                    AddLog($"{attacker.Card.cardName} attacks {target.Card.cardName} for {finalDamage} [🎲{diceRoll}]{diceTag}!", LogEntryType.Combat);

                    if (target.CurrentAshe <= 0)
                    {
                        opponent.Field.RemoveAt(action.TargetIndex);
                        opponent.AshePile.Add(new CardInstance { InstanceId = target.InstanceId, Card = target.Card });
                        AddLog($"{target.Card.cardName} was destroyed!", LogEntryType.Combat);
                    }
                    break;

                case TargetType.Pillar:
                    if (action.TargetIndex < 0 || action.TargetIndex >= opponent.Pillars.Count) return false;
                    var pillar = opponent.Pillars[action.TargetIndex];
                    if (pillar.Destroyed) return false;

                    // Reveal face-down pillar on first attack
                    if (!pillar.Revealed)
                    {
                        pillar.Revealed = true;
                        AddLog($"Pillar revealed: {pillar.Card.cardName}!", LogEntryType.Effect);
                    }

                    int pillarDmg = Mathf.RoundToInt(baseDamage * diceMod);
                    pillar.CurrentHp -= pillarDmg;
                    AddLog($"{attacker.Card.cardName} attacks Pillar {pillar.Card.cardName} for {pillarDmg} [🎲{diceRoll}]!", LogEntryType.Combat);

                    if (pillar.CurrentHp <= 0)
                    {
                        pillar.Destroyed = true;
                        AddLog($"Pillar {pillar.Card.cardName} was destroyed!", LogEntryType.Combat);
                    }
                    break;

                case TargetType.Conjuror:
                    int conjDmg = Mathf.RoundToInt(baseDamage * diceMod);
                    opponent.Conjuror.Hp -= conjDmg;
                    AddLog($"{attacker.Card.cardName} strikes the Conjuror for {conjDmg} [🎲{diceRoll}]!", LogEntryType.Combat);

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

        // ─── Activate Pillar ──────────────────────────────
        private bool HandleActivatePillar(PlayerState player, int pillarIndex, int abilityIndex)
        {
            if (pillarIndex < 0 || pillarIndex >= player.Pillars.Count) return false;

            var pillar = player.Pillars[pillarIndex];
            if (pillar.Destroyed || pillar.AbilityUsedThisTurn) return false;
            if (pillar.Card.activatedAbilities == null || abilityIndex >= pillar.Card.activatedAbilities.Length) return false;

            var ability = pillar.Card.activatedAbilities[abilityIndex];
            if (pillar.Loyalty < ability.loyaltyCost) return false;

            pillar.Loyalty -= ability.loyaltyCost;
            pillar.AbilityUsedThisTurn = true;

            AddLog($"{player.Name} activated {pillar.Card.cardName}'s {ability.abilityName}!", LogEntryType.Action);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Phase Management ─────────────────────────────
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

            if (State.Phase == GamePhase.Battle)
            {
                // Enable attacks for daemons that aren't freshly summoned
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

            // Reset daemon attack flags and enable CanAttack
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

            // Decrement mask durations
            foreach (var d in currentPlayer.Field)
            {
                d.Masks.RemoveAll(m =>
                {
                    m.TurnsRemaining--;
                    return m.TurnsRemaining <= 0;
                });
            }

            // Switch player
            State.CurrentPlayer = 1 - State.CurrentPlayer;
            State.TurnNumber++;

            // Increment max will (cap at MAX_WILL)
            var nextPlayer = State.Players[State.CurrentPlayer];
            if (nextPlayer.MaxWill < GameConstants.MaxWill)
                nextPlayer.MaxWill++;
            nextPlayer.Will = nextPlayer.MaxWill;

            State.Phase = GamePhase.Draw;

            AddLog($"Turn {State.TurnNumber} — {nextPlayer.Name}'s turn.", LogEntryType.System);
            OnStateChanged?.Invoke(State);
            return true;
        }

        // ─── Utilities ────────────────────────────────────
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

        // ─── Win Condition Check ──────────────────────────
        public void CheckWinConditions()
        {
            for (int i = 0; i < 2; i++)
            {
                // All 4 pillars destroyed
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
