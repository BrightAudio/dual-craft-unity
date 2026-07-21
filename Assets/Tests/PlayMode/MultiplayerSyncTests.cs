using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                .Where(deck => deck != null)
                .Select(deck => deck.CreatePlayableRuntimeCopy())
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
            AssertProjectedCardsHaveArtwork(hostView, "host");
            AssertProjectedCardsHaveArtwork(guestView, "guest");

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

        [Test]
        public void Projector_ResolvesNetworkCardIds_CaseInsensitively_WithArtwork()
        {
            CardData expected = _db.GetAllCards().First(card => card != null && card.artwork != null);
            string networkId = $"  {expected.cardId.ToUpperInvariant()}  ";
            var sourceState = CreateStateWithLocalHandId(networkId);

            var projected = Net.NetworkStateProjector.ToLocalGameState(sourceState, 0, _db);

            CardData actual = projected.Players[0].Hand.Single().Card;
            Assert.AreSame(expected, actual, "Network ID should resolve to the local card asset despite casing/whitespace.");
            Assert.IsTrue(actual.artwork != null || actual.fullArt != null, "Resolved multiplayer card should retain local artwork.");
        }

        [Test]
        public void GuestSnapshot_SurvivesActualRelayPacketization_WithCompleteCardData()
        {
            var decks = Resources.LoadAll<DeckData>("CardData/Decks")
                .Where(deck => deck != null)
                .Select(deck => deck.CreatePlayableRuntimeCopy())
                .Where(deck => deck != null && deck.IsValid)
                .Take(2)
                .ToArray();
            Assert.GreaterOrEqual(decks.Length, 2, "Need two valid decks for Relay packet verification.");

            var messages = new List<(int Seat, Net.NetEnvelope Envelope)>();
            var room = new Net.AuthoritativeRoom("relay-packet-card-data", new Net.RoomSettings { GameMode = "standard" });
            room.OnSendToPlayer += (seat, envelope) => messages.Add((seat, envelope));
            room.AddPlayer(new Net.PlayerSession { PlayerId = "mac-host", PlayerName = "Mac Host", DeckId = decks[0].deckName });
            room.AddPlayer(new Net.PlayerSession { PlayerId = "win-guest", PlayerName = "Windows Guest", DeckId = decks[1].deckName });
            room.StartGame(_db, decks[0], decks[1]);

            Net.GameStateSnapshot guestSnapshot = LastSnapshotFor(messages, 1);
            Assert.IsNotNull(guestSnapshot?.State, "Guest snapshot missing before transport.");
            var envelope = Net.NetEnvelope.Create(guestSnapshot, "server");
            byte[] serialized = Encoding.UTF8.GetBytes(UnityEngine.JsonUtility.ToJson(envelope));
            IReadOnlyList<byte[]> packets = Net.RelayManager.BuildTransportPackets(serialized);
            byte[] rebuilt = Net.RelayManager.ReassembleTransportPackets(packets);

            CollectionAssert.AreEqual(serialized, rebuilt, "Relay packetization changed the guest snapshot payload.");
            var receivedEnvelope = UnityEngine.JsonUtility.FromJson<Net.NetEnvelope>(Encoding.UTF8.GetString(rebuilt));
            var receivedSnapshot = Net.JsonUtility.FromJson<Net.GameStateSnapshot>(receivedEnvelope.Payload);
            Net.SerializablePlayerState guestWireState = receivedSnapshot.State.Players[receivedSnapshot.YourPlayerIndex];
            Assert.AreEqual(guestWireState.HandCount, guestWireState.HandCards.Length, "Guest wire hand lost card specs.");
            Assert.IsTrue(guestWireState.HandCards.All(card => card != null
                && !string.IsNullOrWhiteSpace(card.CardId)
                && !string.IsNullOrWhiteSpace(card.Name)
                && !string.IsNullOrWhiteSpace(card.Category)), "Guest wire hand contains blank card data.");

            GameState projected = Net.NetworkStateProjector.ToLocalGameState(receivedSnapshot.State, receivedSnapshot.YourPlayerIndex, _db);
            AssertPlayableProjectedView(projected, "packetized guest");
            AssertProjectedCardsHaveArtwork(projected, "packetized guest");
        }

        [Test]
        public void Projector_RepairsMislabeledGuestSeat_InsteadOfShowingHiddenPlaceholders()
        {
            CardData expected = _db.GetAllCards().First(card => card != null && card.artwork != null);
            var sourceState = CreateStateWithLocalHandId(expected.cardId);

            // Simulate the failure seen in the live join: seat metadata says 1 while
            // the private hand payload clearly belongs to seat 0.
            GameState projected = Net.NetworkStateProjector.ToLocalGameState(sourceState, 1, _db);

            Assert.AreEqual(1, projected.Players[0].Hand.Count);
            Assert.IsNotNull(projected.Players[0].Hand[0].Card, "Guest hand must not become a hidden placeholder.");
            Assert.AreEqual(expected.cardId, projected.Players[0].Hand[0].Card.cardId);
            Assert.IsTrue(projected.Players[0].Hand[0].Card.artwork != null || projected.Players[0].Hand[0].Card.fullArt != null);
        }

        private static Net.SerializableGameState CreateStateWithLocalHandId(string cardId)
        {
            return new Net.SerializableGameState
            {
                CurrentPlayer = 0,
                Phase = GamePhase.Main.ToString(),
                Winner = -1,
                Players = new[]
                {
                    new Net.SerializablePlayerState
                    {
                        Name = "Local",
                        InvokerHp = GameConstants.InvokerMaxHp,
                        InvokerMaxHp = GameConstants.InvokerMaxHp,
                        HandCount = 1,
                        HandCardIds = new[] { cardId },
                        Field = System.Array.Empty<Net.SerializableDaemon>(),
                        Pillars = System.Array.Empty<Net.SerializablePillar>(),
                        AsheCards = System.Array.Empty<Net.SerializableAsheCard>(),
                    },
                    new Net.SerializablePlayerState
                    {
                        Name = "Remote",
                        InvokerHp = GameConstants.InvokerMaxHp,
                        InvokerMaxHp = GameConstants.InvokerMaxHp,
                        Field = System.Array.Empty<Net.SerializableDaemon>(),
                        Pillars = System.Array.Empty<Net.SerializablePillar>(),
                        AsheCards = System.Array.Empty<Net.SerializableAsheCard>(),
                    },
                },
                RecentLog = new List<Net.SerializableLogEntry>(),
            };
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

        private static void AssertProjectedCardsHaveArtwork(GameState view, string label)
        {
            IEnumerable<CardData> visibleCards = view.Players[0].Hand.Select(card => card.Card)
                .Concat(view.Players[0].Field.Select(daemon => daemon.Card))
                .Concat(view.Players[1].Field.Select(daemon => daemon.Card));
            Assert.IsTrue(visibleCards.Where(card => card != null)
                .All(card => card.artwork != null || card.fullArt != null),
                $"{label} projected cards should have local artwork references.");
        }
    }
}
