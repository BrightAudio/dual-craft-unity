using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DualCraft.Editor
{
    using Battle;
    using Cards;
    using Core;
    using Networking;

    public static class MultiplayerSmokeRunner
    {
        [MenuItem("Dual Craft/Tests/Run Multiplayer Smoke")]
        public static void RunMultiplayerSmoke()
        {
            var db = Resources.Load<CardDatabase>("CardData/CardDatabase");
            if (db == null)
                throw new Exception("CardDatabase not found at Resources/CardData/CardDatabase");

            db.Initialize();
            AssertActionRoundTrips();

            var elements = db.GetCardsByType<DaemonCardData>()
                .Select(card => card.element)
                .Distinct()
                .Take(2)
                .ToArray();
            if (elements.Length < 2)
                throw new Exception("Need at least two daemon elements for multiplayer smoke.");

            var deck0 = BuildSmokeDeck(db, elements[0], "Online Smoke A");
            var deck1 = BuildSmokeDeck(db, elements[1], "Online Smoke B");
            var room = new AuthoritativeRoom("SMOKE", new RoomSettings { TurnTimerSeconds = 90, GameMode = "standard" });
            var outbound = new List<(int Seat, NetEnvelope Envelope)>();
            room.OnSendToPlayer += (seat, envelope) => outbound.Add((seat, envelope));

            room.AddPlayer(new PlayerSession { PlayerId = "p0", PlayerName = "Host", DeckId = "deck0" });
            room.AddPlayer(new PlayerSession { PlayerId = "p1", PlayerName = "Guest", DeckId = "deck1" });
            room.StartGame(db, deck0, deck1);

            int snapshots = outbound.Count(msg => msg.Envelope.Type == nameof(Networking.GameStateSnapshot));
            if (snapshots != 2)
                throw new Exception($"Expected 2 initial snapshots, got {snapshots}.");

            AssertGuestCardDataSurvivesRelay(outbound, db);

            if (room.Core?.State == null || room.Core.State.Phase != GamePhase.Draw)
                throw new Exception("Room did not start in Draw phase.");

            outbound.Clear();
            room.ProcessAction(room.Core.State.CurrentPlayer, SerializableAction.FromGameAction(new DrawCardAction()), 1);
            int confirmations = outbound.Count(msg => msg.Envelope.Type == nameof(ActionConfirmed));
            if (confirmations != 2)
                throw new Exception($"Expected 2 confirmations after draw, got {confirmations}.");

            Debug.Log("[MultiplayerSmoke] PASS room start, Relay packet transport, complete guest card data, and action confirmation.");
        }

        private static void AssertGuestCardDataSurvivesRelay(List<(int Seat, NetEnvelope Envelope)> outbound, CardDatabase db)
        {
            NetEnvelope sourceEnvelope = outbound
                .Last(msg => msg.Seat == 1 && msg.Envelope.Type == nameof(Networking.GameStateSnapshot))
                .Envelope;
            byte[] serialized = Encoding.UTF8.GetBytes(UnityEngine.JsonUtility.ToJson(sourceEnvelope));
            IReadOnlyList<byte[]> packets = RelayManager.BuildTransportPackets(serialized);
            byte[] rebuilt = RelayManager.ReassembleTransportPackets(packets);
            if (!serialized.SequenceEqual(rebuilt))
                throw new Exception("Relay packet transport changed the guest snapshot bytes.");

            NetEnvelope receivedEnvelope = UnityEngine.JsonUtility.FromJson<NetEnvelope>(Encoding.UTF8.GetString(rebuilt));
            var snapshot = Networking.JsonUtility.FromJson<Networking.GameStateSnapshot>(receivedEnvelope.Payload);
            if (snapshot?.State?.Players == null || snapshot.YourPlayerIndex < 0
                || snapshot.YourPlayerIndex >= snapshot.State.Players.Length)
                throw new Exception("Guest snapshot was invalid after Relay transport.");

            SerializablePlayerState guest = snapshot.State.Players[snapshot.YourPlayerIndex];
            if (guest.HandCards == null || guest.HandCards.Length != guest.HandCount)
                throw new Exception($"Guest card specs missing after Relay transport ({guest.HandCards?.Length ?? 0}/{guest.HandCount}).");
            if (guest.HandCards.Any(card => card == null
                || string.IsNullOrWhiteSpace(card.CardId)
                || string.IsNullOrWhiteSpace(card.Name)
                || string.IsNullOrWhiteSpace(card.Category)))
                throw new Exception("Guest hand contains blank card data after Relay transport.");

            GameState projected = NetworkStateProjector.ToLocalGameState(snapshot.State, snapshot.YourPlayerIndex, db);
            if (projected?.Players[0]?.Hand == null || projected.Players[0].Hand.Count != guest.HandCount
                || projected.Players[0].Hand.Any(card => card?.Card == null))
                throw new Exception("Guest could not reconstruct its transported cards.");
            if (projected.Players[0].Hand.Any(card => card.Card.artwork == null && card.Card.fullArt == null))
                throw new Exception("Guest reconstructed a card without local artwork.");

            Debug.Log($"[MultiplayerSmoke] Guest snapshot {serialized.Length} bytes transported in {packets.Count} packet(s), {guest.HandCount} complete hand cards.");
        }

        private static void AssertActionRoundTrips()
        {
            AssertRoundTrip(new PlayAsheCardAction { HandIndex = 2, TargetDaemonFieldIndex = -1 }, action =>
            {
                var typed = action as PlayAsheCardAction;
                return typed != null && typed.HandIndex == 2 && typed.TargetDaemonFieldIndex == -1;
            });

            AssertRoundTrip(new AssignAsheCardAction { AsheCardBoardIndex = 1, TargetDaemonFieldIndex = 3 }, action =>
            {
                var typed = action as AssignAsheCardAction;
                return typed != null && typed.AsheCardBoardIndex == 1 && typed.TargetDaemonFieldIndex == 3;
            });

            AssertRoundTrip(new AttackAsheCardAction { AttackerFieldIndex = 4, AsheCardBoardIndex = 2 }, action =>
            {
                var typed = action as AttackAsheCardAction;
                return typed != null && typed.AttackerFieldIndex == 4 && typed.AsheCardBoardIndex == 2;
            });

            AssertRoundTrip(new AttackAction
            {
                AttackerIndex = 1,
                Target = TargetType.Daemon,
                TargetIndex = 2,
                ResponseDispelHandIndex = 5,
            }, action =>
            {
                var typed = action as AttackAction;
                return typed != null
                    && typed.AttackerIndex == 1
                    && typed.Target == TargetType.Daemon
                    && typed.TargetIndex == 2
                    && typed.ResponseDispelHandIndex == 5;
            });
        }

        private static void AssertRoundTrip(GameAction action, Func<GameAction, bool> predicate)
        {
            var roundTripped = SerializableAction.FromGameAction(action).ToGameAction();
            if (!predicate(roundTripped))
                throw new Exception($"Action round-trip failed for {action.Type}.");
        }

        private static DeckData BuildSmokeDeck(CardDatabase db, Element element, string deckName)
        {
            var cards = new List<CardData>();
            AddCards(cards, db.GetCardsByType<DaemonCardData>().Where(card => card.element == element).OrderBy(card => card.GetWillCost()).Cast<CardData>(), GameConstants.DeckDaemonCount);
            AddCards(cards, db.GetCardsByType<AsheCardData>().Where(card => card.matchType != AsheMatchType.Element || card.targetElement == element).Cast<CardData>(), GameConstants.DeckAsheCount);
            AddCards(cards, db.GetCardsByType<MaskCardData>().Cast<CardData>(), GameConstants.DeckRelicCount);
            AddCards(cards, db.GetCardsByType<DomainCardData>().Cast<CardData>(), GameConstants.DeckDomainCount);
            AddCards(cards, db.GetCardsByType<HexCardData>().Cast<CardData>(), GameConstants.DeckHexCount);
            AddCards(cards, db.GetCardsByType<DispelCardData>().Cast<CardData>(), GameConstants.DeckDispelCount);

            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = deckName;
            deck.element = element;
            deck.cards = cards
                .GroupBy(card => card)
                .Select(group => new DeckEntry { card = group.Key, count = group.Count() })
                .ToArray();
            deck.pillars = Array.Empty<DeckEntry>();
            return deck;
        }

        private static void AddCards(List<CardData> output, IEnumerable<CardData> source, int count)
        {
            if (count <= 0)
                return;

            var cards = source.Where(card => card != null).ToArray();
            if (cards.Length == 0)
                throw new Exception($"Cannot build multiplayer smoke deck: missing cards for a {count}-card package.");

            for (int i = 0; i < count; i++)
                output.Add(cards[i % cards.Length]);
        }
    }
}
