#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DualCraft.Editor
{
    using Battle;
    using Cards;
    using Core;
    using Net = Networking;

    public static class MultiplayerSyncVerifier
    {
        public static void Run()
        {
            try
            {
                CardDatabase db = Resources.Load<CardDatabase>("CardData/CardDatabase");
                Require(db != null, "CardDatabase asset not found.");
                db.Initialize();

                DeckData[] decks =
                {
                    BuildSimulationDeck(db, CreatureType.Elemental, Element.Flame, "Sim Flame"),
                    BuildSimulationDeck(db, CreatureType.Machine, Element.Earth, "Sim Machine"),
                };

                var messages = new List<(int Seat, Net.NetEnvelope Envelope)>();
                var room = new Net.AuthoritativeRoom("sim-cross-platform", new Net.RoomSettings { GameMode = "standard" });
                room.OnSendToPlayer += (seat, envelope) => messages.Add((seat, envelope));
                room.AddPlayer(new Net.PlayerSession { PlayerId = "mac-host", PlayerName = "Mac Host", DeckId = decks[0].deckName });
                room.AddPlayer(new Net.PlayerSession { PlayerId = "win-guest", PlayerName = "Windows Guest", DeckId = decks[1].deckName });
                room.StartGame(db, decks[0], decks[1]);

                Net.GameStateSnapshot hostSnapshot = LastSnapshotFor(messages, 0);
                Net.GameStateSnapshot guestSnapshot = LastSnapshotFor(messages, 1);
                Require(hostSnapshot?.State != null, "Host did not receive initial state.");
                Require(guestSnapshot?.State != null, "Guest did not receive initial state.");
                Require(hostSnapshot.ServerSequence == guestSnapshot.ServerSequence, "Initial state sequence differs by seat.");
                AssertPlayable(Net.NetworkStateProjector.ToLocalGameState(hostSnapshot.State, hostSnapshot.YourPlayerIndex, db), "host");
                AssertPlayable(Net.NetworkStateProjector.ToLocalGameState(guestSnapshot.State, guestSnapshot.YourPlayerIndex, db), "guest");

                messages.Clear();
                room.ProcessAction(0, Net.SerializableAction.FromGameAction(new DrawCardAction()), 1);
                Net.ActionConfirmed guestConfirm = LastConfirmedFor(messages, 1);
                Require(guestConfirm?.State != null, "Guest did not receive confirmed state after host action.");
                Require(guestConfirm.ServerSequence > guestSnapshot.ServerSequence, "Confirmed action did not advance server sequence.");
                AssertPlayable(Net.NetworkStateProjector.ToLocalGameState(guestConfirm.State, 1, db), "guest after host action");

                VerifyFallbackCardSpec(db);

                Debug.Log("[MultiplayerSyncVerifier] PASS: simulated host/guest snapshots, action sync, and fallback cards are playable.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultiplayerSyncVerifier] FAIL: {ex}");
                EditorApplication.Exit(1);
            }
        }

        private static void VerifyFallbackCardSpec(CardDatabase db)
        {
            var state = new Net.SerializableGameState
            {
                RoomId = "fallback-card-spec",
                CurrentPlayer = 0,
                Phase = GamePhase.Main.ToString(),
                TurnNumber = 1,
                Winner = -1,
                Players = new[]
                {
                    new Net.SerializablePlayerState
                    {
                        Id = "p0",
                        Name = "Local",
                        InvokerHp = GameConstants.InvokerMaxHp,
                        InvokerMaxHp = GameConstants.InvokerMaxHp,
                        HandCount = 1,
                        DeckCount = 50,
                        HandCards = new[]
                        {
                            new Net.SerializableCardSpec
                            {
                                CardId = "windows-only-daemon",
                                Name = "Windows Only Daemon",
                                Category = CardCategory.Daemon.ToString(),
                                Rarity = Rarity.Rare.ToString(),
                                Element = Element.Flame.ToString(),
                                CreatureType = CreatureType.Machine.ToString(),
                                Description = "Fallback daemon from network snapshot.",
                                Attack = 4,
                                Life = 9,
                                AttackCost = 2,
                                AttackPattern = DaemonAttackPattern.Direct.ToString(),
                            },
                        },
                        Field = Array.Empty<Net.SerializableDaemon>(),
                        Pillars = Array.Empty<Net.SerializablePillar>(),
                        AsheCards = Array.Empty<Net.SerializableAsheCard>(),
                    },
                    new Net.SerializablePlayerState
                    {
                        Id = "p1",
                        Name = "Remote",
                        InvokerHp = GameConstants.InvokerMaxHp,
                        InvokerMaxHp = GameConstants.InvokerMaxHp,
                        DeckCount = 50,
                        Field = Array.Empty<Net.SerializableDaemon>(),
                        Pillars = Array.Empty<Net.SerializablePillar>(),
                        AsheCards = Array.Empty<Net.SerializableAsheCard>(),
                    },
                },
                RecentLog = new List<Net.SerializableLogEntry>(),
            };

            GameState projected = Net.NetworkStateProjector.ToLocalGameState(state, 0, db);
            Require(projected?.Players[0]?.Hand.Count == 1, "Fallback state did not project local hand.");
            Require(projected.Players[0].Hand[0].Card is DaemonCardData, "Fallback card was not recreated as a daemon.");
            Require(projected.Players[0].Hand[0].Card.cardName == "Windows Only Daemon", "Fallback card lost its network name.");
        }

        private static DeckData BuildSimulationDeck(CardDatabase db, CreatureType creatureType, Element element, string name)
        {
            var cards = new List<CardData>();
            AddCards(cards, db.GetCardsByType<DaemonCardData>()
                .Where(card => card.creatureType == creatureType || card.element == element)
                .Cast<CardData>(), GameConstants.DeckDaemonCount);
            AddCards(cards, db.GetCardsByType<AsheCardData>()
                .Where(card => card.GetAffinityType() == creatureType || card.targetElement == element)
                .Cast<CardData>(), GameConstants.DeckAsheCount);
            AddCards(cards, db.GetCardsByType<HexCardData>().Cast<CardData>(), GameConstants.DeckHexCount);
            AddCards(cards, db.GetCardsByType<DispelCardData>().Cast<CardData>(), GameConstants.DeckDispelCount);
            AddCards(cards, db.GetCardsByType<MaskCardData>().Cast<CardData>(), GameConstants.DeckRelicCount);
            AddCards(cards, db.GetCardsByType<DomainCardData>().Cast<CardData>(), GameConstants.DeckDomainCount);

            Require(cards.Count == GameConstants.DeckSize, $"{name} simulation deck is {cards.Count}/{GameConstants.DeckSize} cards.");

            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = name;
            deck.element = element;
            deck.primaryCreatureType = creatureType;
            deck.cards = cards.GroupBy(card => card)
                .Select(group => new DeckEntry { card = group.Key, count = group.Count() })
                .ToArray();
            deck.pillars = Array.Empty<DeckEntry>();
            return deck;
        }

        private static void AddCards(List<CardData> target, IEnumerable<CardData> preferred, int count)
        {
            CardData[] pool = preferred.Where(card => card != null).Distinct().ToArray();
            Require(pool.Length > 0, $"No cards available for simulation package of {count}.");

            for (int i = 0; i < count; i++)
                target.Add(pool[i % pool.Length]);
        }

        private static Net.GameStateSnapshot LastSnapshotFor(List<(int Seat, Net.NetEnvelope Envelope)> messages, int seat)
        {
            Net.NetEnvelope envelope = messages.LastOrDefault(m => m.Seat == seat && m.Envelope.Type == nameof(Net.GameStateSnapshot)).Envelope;
            return envelope == null ? null : Net.JsonUtility.FromJson<Net.GameStateSnapshot>(envelope.Payload);
        }

        private static Net.ActionConfirmed LastConfirmedFor(List<(int Seat, Net.NetEnvelope Envelope)> messages, int seat)
        {
            Net.NetEnvelope envelope = messages.LastOrDefault(m => m.Seat == seat && m.Envelope.Type == nameof(Net.ActionConfirmed)).Envelope;
            return envelope == null ? null : Net.JsonUtility.FromJson<Net.ActionConfirmed>(envelope.Payload);
        }

        private static void AssertPlayable(GameState state, string label)
        {
            Require(state != null, $"{label} state is null.");
            Require(state.Players[0] != null && state.Players[1] != null, $"{label} players missing.");
            Require(state.Players[0].Hand.Count > 0, $"{label} local hand is empty.");
            Require(state.Players[0].Hand.All(card => card.Card != null), $"{label} local hand has blank cards.");
            Require(state.Players[0].Field.All(daemon => daemon.Card != null), $"{label} local field has blank daemons.");
            Require(state.Players[1].Field.All(daemon => daemon.Card != null), $"{label} opponent field has blank daemons.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
#endif
