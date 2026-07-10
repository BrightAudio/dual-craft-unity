using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DualCraft.Tests.PlayMode
{
    using Battle;
    using Cards;
    using Core;
    using Net = Networking;

    public class MultiplayerSyncTests
    {
        private CardDatabase _db;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _db = Resources.Load<CardDatabase>("CardData/CardDatabase");
            Assert.IsNotNull(_db, "CardDatabase asset not found.");
            _db.Initialize();
            yield return null;
        }

        [Test]
        public void AuthoritativeSnapshots_ProjectForBothSeats_WithPlayableCards()
        {
            var decks = Resources.LoadAll<DeckData>("CardData/Decks")
                .Where(deck => deck != null && deck.IsValid)
                .Take(2)
                .ToArray();
            Assert.GreaterOrEqual(decks.Length, 2, "Need two valid decks for multiplayer sync simulation.");

            var messages = new List<(int Seat, Net.NetEnvelope Envelope)>();
            var room = new Net.AuthoritativeRoom("sim-cross-platform", new Net.RoomSettings { GameMode = "standard" });
            room.OnSendToPlayer += (seat, envelope) => messages.Add((seat, envelope));
            room.AddPlayer(new Net.PlayerSession { PlayerId = "mac-host", PlayerName = "Mac Host", DeckId = decks[0].deckName });
            room.AddPlayer(new Net.PlayerSession { PlayerId = "win-guest", PlayerName = "Windows Guest", DeckId = decks[1].deckName });

            room.StartGame(_db, decks[0], decks[1]);

            var hostSnapshot = LastSnapshotFor(messages, 0);
            var guestSnapshot = LastSnapshotFor(messages, 1);
            Assert.IsNotNull(hostSnapshot?.State, "Host should receive initial state.");
            Assert.IsNotNull(guestSnapshot?.State, "Guest should receive initial state.");
            Assert.AreEqual(hostSnapshot.ServerSequence, guestSnapshot.ServerSequence, "Initial sequence should match for both seats.");

            var hostView = Net.NetworkStateProjector.ToLocalGameState(hostSnapshot.State, hostSnapshot.YourPlayerIndex, _db);
            var guestView = Net.NetworkStateProjector.ToLocalGameState(guestSnapshot.State, guestSnapshot.YourPlayerIndex, _db);
            AssertPlayableProjectedView(hostView, "host");
            AssertPlayableProjectedView(guestView, "guest");

            messages.Clear();
            room.ProcessAction(0, Net.SerializableAction.FromGameAction(new DrawCardAction()), 1);

            var guestConfirm = LastConfirmedFor(messages, 1);
            Assert.IsNotNull(guestConfirm?.State, "Guest should receive state after host action.");
            Assert.Greater(guestConfirm.ServerSequence, guestSnapshot.ServerSequence, "Confirmed action should advance server sequence.");
            var guestAfterAction = Net.NetworkStateProjector.ToLocalGameState(guestConfirm.State, 1, _db);
            AssertPlayableProjectedView(guestAfterAction, "guest after host draw");
            Assert.IsTrue(guestAfterAction.Log.Any(entry => entry.Message.Contains("draws") || entry.Message.Contains("turn", System.StringComparison.OrdinalIgnoreCase)),
                "Projected guest log should explain the latest action/turn state.");
        }

        [Test]
        public void Projector_UsesNetworkCardSpec_WhenLocalBuildLacksCardAsset()
        {
            var sourceState = new Net.SerializableGameState
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
                        Field = System.Array.Empty<Net.SerializableDaemon>(),
                        Pillars = System.Array.Empty<Net.SerializablePillar>(),
                        AsheCards = System.Array.Empty<Net.SerializableAsheCard>(),
                    },
                    new Net.SerializablePlayerState
                    {
                        Id = "p1",
                        Name = "Remote",
                        InvokerHp = GameConstants.InvokerMaxHp,
                        InvokerMaxHp = GameConstants.InvokerMaxHp,
                        DeckCount = 50,
                        Field = System.Array.Empty<Net.SerializableDaemon>(),
                        Pillars = System.Array.Empty<Net.SerializablePillar>(),
                        AsheCards = System.Array.Empty<Net.SerializableAsheCard>(),
                    },
                },
                RecentLog = new List<Net.SerializableLogEntry>(),
            };

            var projected = Net.NetworkStateProjector.ToLocalGameState(sourceState, 0, _db);
            Assert.IsNotNull(projected);
            Assert.AreEqual(1, projected.Players[0].Hand.Count);
            Assert.IsInstanceOf<DaemonCardData>(projected.Players[0].Hand[0].Card);
            Assert.AreEqual("Windows Only Daemon", projected.Players[0].Hand[0].Card.cardName);
            Assert.AreEqual("Fallback daemon from network snapshot.", projected.Players[0].Hand[0].Card.description);
        }

        private static Net.GameStateSnapshot LastSnapshotFor(List<(int Seat, Net.NetEnvelope Envelope)> messages, int seat)
        {
            var envelope = messages.LastOrDefault(m => m.Seat == seat && m.Envelope.Type == nameof(Net.GameStateSnapshot)).Envelope;
            return envelope == null ? null : Net.JsonUtility.FromJson<Net.GameStateSnapshot>(envelope.Payload);
        }

        private static Net.ActionConfirmed LastConfirmedFor(List<(int Seat, Net.NetEnvelope Envelope)> messages, int seat)
        {
            var envelope = messages.LastOrDefault(m => m.Seat == seat && m.Envelope.Type == nameof(Net.ActionConfirmed)).Envelope;
            return envelope == null ? null : Net.JsonUtility.FromJson<Net.ActionConfirmed>(envelope.Payload);
        }

        private static void AssertPlayableProjectedView(GameState view, string label)
        {
            Assert.IsNotNull(view, $"{label} projected state should not be null.");
            Assert.IsNotNull(view.Players[0], $"{label} local player missing.");
            Assert.IsNotNull(view.Players[1], $"{label} opponent player missing.");
            Assert.Greater(view.Players[0].Hand.Count, 0, $"{label} local hand should be visible.");
            Assert.IsTrue(view.Players[0].Hand.All(card => card.Card != null), $"{label} local hand has blank cards.");
            Assert.IsTrue(view.Players[0].Field.All(daemon => daemon.Card != null), $"{label} local field has blank daemons.");
            Assert.IsTrue(view.Players[1].Field.All(daemon => daemon.Card != null), $"{label} opponent field has blank daemons.");
            Assert.GreaterOrEqual(view.Players[0].Invoker.Hp, 0, $"{label} local invoker HP invalid.");
            Assert.GreaterOrEqual(view.Players[1].Invoker.Hp, 0, $"{label} opponent invoker HP invalid.");
        }
    }
}
