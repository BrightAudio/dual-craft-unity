// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — PlayMode Tests: Game Rules & Constants
//  Verifies balance constraints and game configuration
// ═══════════════════════════════════════════════════════

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DualCraft.Tests.PlayMode
{
    using Battle;
    using Cards;
    using Core;

    public class GameRulesTests
    {
        private CardDatabase _db;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _db = Resources.Load<CardDatabase>("CardData/CardDatabase");
            _db.Initialize();
            yield return null;
        }

        [Test]
        public void GameConstants_AreReasonable()
        {
            Assert.Greater(GameConstants.InvokerMaxHp, 0);
            Assert.Greater(GameConstants.StartingHandSize, 0);
            Assert.LessOrEqual(GameConstants.StartingHandSize, GameConstants.MaxHandSize);
            Assert.Greater(GameConstants.DeckSize, GameConstants.StartingHandSize);
            Assert.Greater(GameConstants.MaxWill, GameConstants.StartingWill);
            Assert.Greater(GameConstants.SuperEffectiveMult, 1f);
            Assert.Less(GameConstants.WeakMult, 1f);
        }

        [Test]
        public void AllCards_HaveValidWillCost()
        {
            foreach (var card in _db.GetAllCards())
            {
                int cost = card.GetWillCost();
                Assert.GreaterOrEqual(cost, 0,
                    $"{card.cardId} has negative will cost: {cost}");
                Assert.LessOrEqual(cost, GameConstants.MaxWill,
                    $"{card.cardId} has cost ({cost}) exceeding MaxWill ({GameConstants.MaxWill})");
            }
        }

        [Test]
        public void RarityDistribution_IsBalanced()
        {
            var all = _db.GetAllCards();
            var byRarity = all.GroupBy(c => c.rarity)
                .ToDictionary(g => g.Key, g => g.Count());

            Debug.Log("[RulesTest] Rarity distribution:");
            foreach (var kv in byRarity.OrderBy(x => x.Key))
                Debug.Log($"  {kv.Key}: {kv.Value}");

            // Commons should outnumber legendaries
            if (byRarity.ContainsKey(Rarity.Common) && byRarity.ContainsKey(Rarity.Legendary))
            {
                Assert.Greater(byRarity[Rarity.Common], byRarity[Rarity.Legendary],
                    "Common cards should outnumber Legendary cards");
            }
        }

        [Test]
        public void ElementBalance_EachHasMinCards()
        {
            foreach (Element elem in System.Enum.GetValues(typeof(Element)))
            {

                var elemCards = _db.GetCardsByElement(elem);
                Assert.Greater(elemCards.Count, 0,
                    $"Element {elem} has no cards at all");
                Debug.Log($"[RulesTest] {elem}: {elemCards.Count} element-specific cards");
            }
        }

        [Test]
        public void Invokers_OfferAtLeastOneChoicePerRepresentedElement()
        {
            var invokers = _db.GetCardsByType<InvokerCardData>();
            var byElement = invokers.GroupBy(c => c.element).ToList();

            foreach (var group in byElement)
            {
                Assert.GreaterOrEqual(group.Count(), 1,
                    $"Element {group.Key} should offer at least one Invoker choice.");
            }

            Debug.Log($"[RulesTest] Invokers: {invokers.Count} total, {byElement.Count} elements");
        }

        [Test]
        public void RarityBasedDaemonDeathLoss_UsesExpectedMapping()
        {
            Assert.AreEqual(1, GameConstants.GetInvokerLifeLossForRarity(Rarity.Common));
            Assert.AreEqual(2, GameConstants.GetInvokerLifeLossForRarity(Rarity.Rare));
            Assert.AreEqual(4, GameConstants.GetInvokerLifeLossForRarity(Rarity.Epic));
            Assert.AreEqual(8, GameConstants.GetInvokerLifeLossForRarity(Rarity.Legendary));
        }

        [Test]
        public void ResponseDispels_AreUniversalInsideTheirPrintedWindow()
        {
            var attacker = ScriptableObject.CreateInstance<DaemonCardData>();
            attacker.element = Element.Flame;
            attacker.creatureType = CreatureType.Elemental;

            var dispel = ScriptableObject.CreateInstance<DispelCardData>();
            dispel.target = DispelTarget.Any;
            dispel.canCounterAttack = true;
            dispel.matchAttackElement = true;
            dispel.responseElement = Element.Water;

            Assert.IsTrue(BattleManager.CanDispelCounterAttack(dispel, attacker));

            dispel.canCounterHex = true;
            dispel.canCounterDomain = false;
            Assert.IsTrue(BattleManager.CanDispelCounterHex(dispel));
            Assert.IsFalse(BattleManager.CanDispelCounterDomain(dispel));
        }

        [Test]
        public void DestroyedPillars_DoNotEndStoryCardBattle()
        {
            var engine = CreateRulesTestBattle();
            foreach (var pillar in engine.State.Players[1].Pillars)
                pillar.Destroyed = true;

            engine.CheckWinConditions();

            Assert.IsFalse(engine.State.GameOver, "Pillars should no longer be a win condition.");
            Assert.IsNull(engine.State.Winner);
        }

        [Test]
        public void DestroyedDaemon_DrainsOwnerInvokerLife()
        {
            var engine = CreateRulesTestBattle();
            var p1 = engine.State.Players[0];
            var p2 = engine.State.Players[1];
            var cards = _db.GetCardsByType<DaemonCardData>()
                .Where(card => card != null && !card.rangedAttack && card.attackPattern != DaemonAttackPattern.Ranged)
                .Take(2)
                .ToArray();
            Assert.GreaterOrEqual(cards.Length, 2, "Need at least two daemons for combat rule test.");

            engine.State.CurrentPlayer = 0;
            engine.State.Phase = GamePhase.Combat;
            p1.Will = GameConstants.MaxStoredWill;
            p1.Field.Clear();
            p2.Field.Clear();
            p1.Field.Add(CreateDaemon(cards[0], attack: 99, ashe: 10, canAttack: true));
            p2.Field.Add(CreateDaemon(cards[1], attack: 1, ashe: 1, canAttack: false));
            int beforeHp = p2.Invoker.Hp;

            bool resolved = engine.ProcessAction(0, new AttackAction
            {
                AttackerIndex = 0,
                Target = TargetType.Daemon,
                TargetIndex = 0,
            });

            Assert.IsTrue(resolved);
            Assert.AreEqual(0, p2.Field.Count, "Defending daemon should be destroyed.");
            Assert.AreEqual(beforeHp - GameConstants.GetInvokerLifeLossForRarity(cards[1].rarity), p2.Invoker.Hp,
                "Invoker damage should use the destroyed daemon's rarity.");
        }

        [Test]
        public void InvokerAttack_BypassesPillarsWhenNoDaemonsDefend()
        {
            var engine = CreateRulesTestBattle();
            var p1 = engine.State.Players[0];
            var p2 = engine.State.Players[1];
            var attackerCard = _db.GetCardsByType<DaemonCardData>().FirstOrDefault();
            Assert.IsNotNull(attackerCard, "Need a daemon for invoker attack rule test.");

            engine.State.CurrentPlayer = 0;
            engine.State.Phase = GamePhase.Combat;
            p1.Will = GameConstants.MaxStoredWill;
            p1.Field.Clear();
            p2.Field.Clear();
            p2.Wards.Clear();
            p1.Field.Add(CreateDaemon(attackerCard, attack: 5, ashe: 10, canAttack: true));
            int beforeHp = p2.Invoker.Hp;
            int intactPillars = p2.Pillars.Count(p => !p.Destroyed);

            bool resolved = engine.ProcessAction(0, new AttackAction
            {
                AttackerIndex = 0,
                Target = TargetType.Invoker,
                TargetIndex = 0,
            });

            Assert.IsTrue(resolved);
            Assert.AreEqual(beforeHp - 5, p2.Invoker.Hp);
            Assert.AreEqual(intactPillars, p2.Pillars.Count(p => !p.Destroyed));
        }

        [Test]
        public void RangedDaemon_CannotAttackStraightAhead()
        {
            var engine = CreateRulesTestBattle();
            var p1 = engine.State.Players[0];
            var p2 = engine.State.Players[1];
            var ranged = CreatePatternCard("Test Ranged", DaemonAttackPattern.Ranged);
            var target = CreatePatternCard("Test Target", DaemonAttackPattern.Direct);
            engine.State.CurrentPlayer = 0;
            engine.State.Phase = GamePhase.Combat;
            p1.Will = GameConstants.MaxStoredWill;
            p1.Field.Clear();
            p2.Field.Clear();
            var attacker = CreateDaemon(ranged, 6, 6, true);
            attacker.LaneIndex = 2;
            var defender = CreateDaemon(target, 2, 8, false);
            defender.LaneIndex = 2;
            p1.Field.Add(attacker);
            p2.Field.Add(defender);

            bool resolved = engine.ProcessAction(0, new AttackAction
            {
                AttackerIndex = 0,
                Target = TargetType.Daemon,
                TargetIndex = 0,
            });

            Assert.IsFalse(resolved);
            Assert.AreEqual(8, defender.CurrentAshe);
        }

        [Test]
        public void InvalidDaemonTarget_DoesNotSpendSpiritEnergyOrConsumeAttack()
        {
            var engine = CreateRulesTestBattle();
            var player = engine.State.Players[0];
            var opponent = engine.State.Players[1];
            var attackerCard = CreatePatternCard("Target Validator", DaemonAttackPattern.Direct);

            engine.State.CurrentPlayer = 0;
            engine.State.Phase = GamePhase.Combat;
            player.Will = 5;
            player.Field.Clear();
            opponent.Field.Clear();
            player.Field.Add(CreateDaemon(attackerCard, attack: 4, ashe: 6, canAttack: true));

            bool resolved = engine.ProcessAction(0, new AttackAction
            {
                AttackerIndex = 0,
                Target = TargetType.Daemon,
                TargetIndex = 0,
            });

            Assert.IsFalse(resolved);
            Assert.AreEqual(5, player.Will, "A stale network target must not spend SE.");
            Assert.IsFalse(player.Field[0].HasAttacked, "A rejected target must not consume the attack.");
        }

        [Test]
        public void Guard_InterceptsOneAdjacentInvokerAttackPerRound()
        {
            var engine = CreateRulesTestBattle();
            var p1 = engine.State.Players[0];
            var p2 = engine.State.Players[1];
            var direct = CreatePatternCard("Test Direct", DaemonAttackPattern.Direct);
            var guardCard = CreatePatternCard("Test Guard", DaemonAttackPattern.Guard);
            engine.State.CurrentPlayer = 0;
            engine.State.Phase = GamePhase.Combat;
            p1.Will = GameConstants.MaxStoredWill;
            p1.Field.Clear();
            p2.Field.Clear();
            p2.Wards.Clear();
            var first = CreateDaemon(direct, 3, 6, true);
            first.LaneIndex = 2;
            var second = CreateDaemon(direct, 3, 6, true);
            second.LaneIndex = 2;
            var guard = CreateDaemon(guardCard, 1, 10, false);
            guard.LaneIndex = 1;
            p1.Field.Add(first);
            p1.Field.Add(second);
            p2.Field.Add(guard);
            int invokerLife = p2.Invoker.Hp;

            bool firstResolved = engine.ProcessAction(0, new AttackAction
            {
                AttackerIndex = 0,
                Target = TargetType.Invoker,
                TargetIndex = 0,
            });
            bool secondResolved = engine.ProcessAction(0, new AttackAction
            {
                AttackerIndex = 1,
                Target = TargetType.Invoker,
                TargetIndex = 0,
            });

            Assert.IsTrue(firstResolved);
            Assert.IsTrue(secondResolved);
            Assert.AreEqual(7, guard.CurrentAshe, "The first hit should be redirected into Guard.");
            Assert.IsTrue(guard.GuardInterceptUsedThisRound);
            Assert.AreEqual(invokerLife - 3, p2.Invoker.Hp, "The second hit should reach the Invoker.");
        }

        [Test]
        public void SecondForm_CannotBeSummonedNormally_AndBindsThroughFaceDownAnchor()
        {
            var baseForm = _db.GetCardsByType<DaemonCardData>()
                .FirstOrDefault(card => card != null && card.evolvesTo != null);
            Assert.IsNotNull(baseForm, "Need a base daemon linked to a Second Form.");
            var secondForm = baseForm.evolvesTo;

            var engine = CreateRulesTestBattle();
            var player = engine.State.Players[0];
            engine.State.CurrentPlayer = 0;
            engine.State.Phase = GamePhase.Main;
            player.Will = GameConstants.MaxStoredWill;
            player.Field.Clear();
            player.Hand.Clear();
            int requiredAnchors = secondForm.rarity switch
            {
                Rarity.Legendary => 3,
                Rarity.Epic => 2,
                _ => 1,
            };
            for (int i = 0; i < requiredAnchors; i++)
            {
                var anchor = CreateDaemon(baseForm, baseForm.attack, baseForm.ashe, true);
                anchor.LaneIndex = i;
                player.Field.Add(anchor);
            }
            var unselectedAnchor = CreateDaemon(baseForm, baseForm.attack, baseForm.ashe, true);
            unselectedAnchor.LaneIndex = requiredAnchors;
            player.Field.Add(unselectedAnchor);
            player.Hand.Add(new CardInstance
            {
                InstanceId = System.Guid.NewGuid().ToString(),
                Card = secondForm,
            });

            Assert.IsFalse(engine.ProcessAction(0, new PlayDaemonAction { HandIndex = 0, TargetLane = 1 }),
                "Second Forms must never enter play through the normal summon action.");
            int[] chosenAnchors = Enumerable.Range(0, requiredAnchors).ToArray();
            Assert.IsTrue(engine.ProcessAction(0, new EvolveAction
            {
                FieldIndex = chosenAnchors[0],
                ConsumeIndex = 0,
                AnchorFieldIndices = chosenAnchors,
            }),
                "A linked Second Form should Bind through its matching base daemon.");

            Assert.AreEqual(requiredAnchors + 2, player.Field.Count);
            Assert.AreEqual(requiredAnchors, player.Field.Count(daemon => daemon.IsBindAnchor && daemon.Card == baseForm),
                "Only the explicitly chosen anchors should be bound.");
            Assert.IsFalse(unselectedAnchor.IsBindAnchor, "The authority must not auto-select an extra eligible daemon.");
            Assert.IsTrue(player.Field.Any(daemon => daemon.IsSecondFormBound && daemon.Card == secondForm));

            while (player.Field.Any(daemon => daemon.IsBindAnchor))
            {
                int anchorIndex = player.Field.FindIndex(daemon => daemon.IsBindAnchor);
                Assert.IsTrue(engine.ProcessAction(0, new SacrificeDaemonAction { FieldIndex = anchorIndex }));
            }

            Assert.IsFalse(player.Field.Any(daemon => daemon.IsSecondFormBound),
                "Sacrificing the final anchor must collapse the Second Form.");
            Assert.IsTrue(player.AshePile.Any(card => card.Card == secondForm));
        }

        [Test]
        public void InvokerPulse_DamagesOnlyOneEnemyDaemon()
        {
            var engine = CreateRulesTestBattle();
            var player = engine.State.Players[0];
            var opponent = engine.State.Players[1];
            var cards = _db.GetCardsByType<DaemonCardData>().Where(card => card != null).Take(2).ToArray();
            Assert.GreaterOrEqual(cards.Length, 2);

            engine.State.CurrentPlayer = 0;
            engine.State.Phase = GamePhase.Main;
            player.Will = GameConstants.InvokerUnleashCost;
            player.Invoker.Hp = Mathf.Max(1, player.Invoker.MaxHp - GameConstants.InvokerUnleashHeal);
            player.Field.Clear();
            opponent.Field.Clear();
            opponent.Field.Add(CreateDaemon(cards[0], 8, 10, false));
            opponent.Field.Add(CreateDaemon(cards[1], 2, 10, false));

            Assert.IsTrue(engine.ProcessAction(0, new ActivateInvokerAction()));
            Assert.AreEqual(10 - GameConstants.InvokerUnleashDamage, opponent.Field[0].CurrentAshe,
                "Pulse should hit the strongest enemy daemon.");
            Assert.AreEqual(10, opponent.Field[1].CurrentAshe,
                "Pulse must not wipe or damage the rest of the board.");
            Assert.AreEqual(player.Invoker.MaxHp, player.Invoker.Hp);
        }

        private BattleManager CreateRulesTestBattle()
        {
            var elements = _db.GetCardsByType<PillarCardData>()
                .Select(card => card.element)
                .Distinct()
                .Take(2)
                .ToArray();
            Assert.GreaterOrEqual(elements.Length, 2, "Need at least two elements with pillars for rules test.");

            var engine = new BattleManager(_db);
            engine.InitGame(
                "Tester A",
                BuildRulesTestDeck(elements[0], "Rules A"),
                "Tester B",
                BuildRulesTestDeck(elements[1], "Rules B"));
            return engine;
        }

        private DeckData BuildRulesTestDeck(Element element, string deckName)
        {
            var daemon = _db.GetCardsByType<DaemonCardData>().FirstOrDefault(card => card.element == element)
                ?? _db.GetCardsByType<DaemonCardData>().First();
            var pillar = _db.GetCardsByType<PillarCardData>().First(card => card.element == element);

            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = deckName;
            deck.element = element;
            deck.cards = new[]
            {
                new DeckEntry { card = daemon, count = GameConstants.DeckSize },
            };
            deck.pillars = new[]
            {
                new DeckEntry { card = pillar, count = GameConstants.PillarCount },
            };
            return deck;
        }

        private static DaemonInstance CreateDaemon(DaemonCardData card, int attack, int ashe, bool canAttack)
        {
            return new DaemonInstance
            {
                InstanceId = System.Guid.NewGuid().ToString(),
                Card = card,
                BaseAttack = attack,
                BaseAshe = ashe,
                Attack = attack,
                CurrentAshe = ashe,
                MaxAshe = ashe,
                AsheCost = 0,
                CanAttack = canAttack,
            };
        }

        private static DaemonCardData CreatePatternCard(string name, DaemonAttackPattern pattern)
        {
            var card = ScriptableObject.CreateInstance<DaemonCardData>();
            card.cardId = $"test-{name.ToLowerInvariant().Replace(' ', '-')}";
            card.cardName = name;
            card.category = CardCategory.Daemon;
            card.rarity = Rarity.Common;
            card.element = Element.Air;
            card.creatureType = CreatureType.Elemental;
            card.attackPattern = pattern;
            card.rangedAttack = pattern == DaemonAttackPattern.Ranged;
            card.willCost = 0;
            card.asheCost = 0;
            return card;
        }
    }
}
