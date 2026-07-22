// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Network State Projector
//  Converts authoritative wire snapshots into renderable
//  local GameState objects for the existing battle UI.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DualCraft.Networking
{
    using Battle;
    using Cards;
    using Core;

    public static class NetworkStateProjector
    {
        public static GameState ToLocalGameState(SerializableGameState source, int localSeat, CardDatabase db)
        {
            if (source == null || source.Players == null || source.Players.Length < 2)
                return null;

            if (!TryResolvePrivateHand(source, localSeat, out localSeat, out string validationError))
            {
                Debug.LogError($"[NetworkStateProjector] Refusing incomplete private state: {validationError}");
                return null;
            }

            int remoteSeat = localSeat == 0 ? 1 : 0;
            var state = new GameState
            {
                RoomId = source.RoomId,
                CurrentPlayer = source.CurrentPlayer == localSeat ? 0 : 1,
                Phase = Enum.TryParse<GamePhase>(source.Phase, true, out var phase) ? phase : GamePhase.Main,
                TurnNumber = source.TurnNumber,
                GameOver = source.GameOver,
                Winner = source.Winner >= 0
                    ? source.Winner == localSeat ? 0 : 1
                    : null,
                Log = BuildLog(source, localSeat),
            };

            SerializableCardSpec[] privateSpecs = source.HasViewerPrivateState ? source.ViewerHandCards : null;
            string[] privateIds = source.HasViewerPrivateState ? source.ViewerHandCardIds : null;
            state.Players[0] = BuildPlayer(source.Players[localSeat], true, db, privateIds, privateSpecs);
            state.Players[1] = BuildPlayer(source.Players[remoteSeat], false, db, null, null);

            if (!string.IsNullOrEmpty(source.ActiveDomainId) && db.GetCard(source.ActiveDomainId) is DomainCardData domain)
            {
                state.ActiveDomain = new ActiveDomain
                {
                    Card = domain,
                    Owner = source.ActiveDomainOwner == localSeat ? 0 : 1,
                    TurnsRemaining = 0,
                };
            }

            return state;
        }

        /// <summary>
        /// A player's private hand is the authoritative seat marker. This repairs a
        /// mislabeled snapshot instead of rendering the joining player's hand as the
        /// opponent's hidden-card placeholders.
        /// </summary>
        public static int ResolveCardOwningSeat(SerializableGameState source, int requestedSeat)
        {
            return TryResolvePrivateHand(source, requestedSeat, out int seat, out _) ? seat : Mathf.Clamp(requestedSeat, 0, 1);
        }

        public static bool TryResolvePrivateHand(SerializableGameState source, int requestedSeat, out int resolvedSeat, out string error)
        {
            resolvedSeat = Mathf.Clamp(requestedSeat, 0, 1);
            error = null;
            if (source?.Players == null || source.Players.Length < 2)
            {
                error = "snapshot has no two-player state";
                return false;
            }

            if (source.HasViewerPrivateState)
            {
                if (source.ViewerPlayerIndex < 0 || source.ViewerPlayerIndex >= source.Players.Length)
                {
                    error = $"invalid viewer seat {source.ViewerPlayerIndex}";
                    return false;
                }

                resolvedSeat = source.ViewerPlayerIndex;
                return ValidatePrivateHandCount(source.Players[resolvedSeat], source.ViewerHandCardIds, source.ViewerHandCards, out error);
            }

            int other = 1 - resolvedSeat;
            bool requestedHasData = HasPrivateHandData(source.Players[resolvedSeat]);
            bool otherHasData = HasPrivateHandData(source.Players[other]);
            if (!requestedHasData && otherHasData)
            {
                Debug.LogWarning($"[NetworkStateProjector] Legacy snapshot labeled seat {resolvedSeat}, but private card data belongs to seat {other}. Correcting local seat.");
                resolvedSeat = other;
            }

            SerializablePlayerState owner = source.Players[resolvedSeat];
            return ValidatePrivateHandCount(owner, owner?.HandCardIds, owner?.HandCards, out error);
        }

        private static bool ValidatePrivateHandCount(SerializablePlayerState owner, string[] ids, SerializableCardSpec[] specs, out string error)
        {
            int expected = owner?.HandCount ?? -1;
            int idCount = ids?.Length ?? 0;
            int specCount = specs?.Length ?? 0;
            bool idsComplete = idCount == expected && (expected == 0 || ids.All(id => !string.IsNullOrWhiteSpace(id)));
            bool specsComplete = specCount == expected && (expected == 0 || specs.All(spec => spec != null && !string.IsNullOrWhiteSpace(spec.CardId)));
            if (expected >= 0 && (idsComplete || specsComplete))
            {
                error = null;
                return true;
            }

            error = $"owner hand identities are incomplete (expected {expected}, ids {idCount}, specs {specCount})";
            return false;
        }

        private static bool HasPrivateHandData(SerializablePlayerState player)
        {
            return player != null && ((player.HandCards != null && player.HandCards.Length > 0)
                || (player.HandCardIds != null && player.HandCardIds.Length > 0));
        }

        private static PlayerState BuildPlayer(SerializablePlayerState source, bool isLocal, CardDatabase db,
            string[] privateIds, SerializableCardSpec[] privateSpecs)
        {
            var player = new PlayerState
            {
                Id = source.Id,
                Name = source.Name,
                Invoker = new InvokerState { Hp = source.InvokerHp, MaxHp = source.InvokerMaxHp },
                Will = source.Will,
                MaxWill = source.MaxWill,
                Field = BuildField(source.Field, db),
                Pillars = BuildPillars(source.Pillars, db),
                AsheCards = BuildAsheCards(source.AsheCards, db),
                Hand = BuildHand(source, isLocal, db, privateIds, privateSpecs),
                Deck = BuildPlaceholders(source.DeckCount),
                SealZone = BuildSealPlaceholders(source.SealCount),
                Wards = BuildWardPlaceholders(source.WardCount),
                AshePile = BuildPlaceholders(source.AshePileCount),
                SourcePlayedThisTurn = source.SourcePlayedThisTurn,
                SourceRaidUsedThisTurn = source.SourceRaidUsedThisTurn,
            };

            return player;
        }

        private static List<CardInstance> BuildHand(SerializablePlayerState source, bool isLocal, CardDatabase db,
            string[] privateIds, SerializableCardSpec[] privateSpecs)
        {
            var hand = new List<CardInstance>();
            SerializableCardSpec[] specs = privateSpecs ?? source.HandCards;
            string[] ids = privateIds ?? source.HandCardIds;
            if (isLocal && specs != null && specs.Length > 0)
            {
                for (int i = 0; i < specs.Length; i++)
                {
                    var spec = specs[i];
                    hand.Add(new CardInstance
                    {
                        InstanceId = $"hand-{i}",
                        Card = ResolveCard(db, spec?.CardId, spec),
                    });
                }
            }
            else if (isLocal && ids != null)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    hand.Add(new CardInstance
                    {
                        InstanceId = $"hand-{i}",
                        Card = ResolveCard(db, ids[i], null),
                    });
                }
            }
            else
            {
                hand.AddRange(BuildPlaceholders(source.HandCount));
            }

            return hand;
        }

        private static List<DaemonInstance> BuildField(SerializableDaemon[] source, CardDatabase db)
        {
            var field = new List<DaemonInstance>();
            if (source == null) return field;

            foreach (var daemon in source)
            {
                var card = ResolveCard(db, daemon.CardId, daemon.Card) as DaemonCardData;
                var instance = new DaemonInstance
                {
                    InstanceId = daemon.InstanceId,
                    Card = card,
                    BaseAttack = card != null ? card.attack : daemon.Attack,
                    BaseAshe = card != null ? card.ashe : daemon.MaxAshe,
                    CurrentAshe = daemon.CurrentAshe,
                    MaxAshe = daemon.MaxAshe,
                    Attack = daemon.Attack,
                    AsheCost = daemon.AsheCost > 0 ? daemon.AsheCost : card != null ? card.asheCost : 0,
                    LaneIndex = daemon.LaneIndex,
                    CanAttack = daemon.CanAttack,
                    HasAttacked = daemon.HasAttacked,
                    Frozen = daemon.Frozen,
                    Stealthed = daemon.Stealthed,
                    Entangled = daemon.Entangled,
                    HasTaunt = daemon.HasTaunt,
                    ShieldAmount = daemon.ShieldAmount,
                    ThornsDamage = daemon.ThornsDamage,
                    Silenced = daemon.Silenced,
                    SilencedTurns = daemon.SilencedTurns,
                    Marked = daemon.Marked,
                    MarkedBonusDamage = daemon.MarkedBonusDamage,
                    MarkedTurns = daemon.MarkedTurns,
                    Fractured = daemon.Fractured,
                    FracturedTurns = daemon.FracturedTurns,
                    Haunted = daemon.Haunted,
                    HauntedLifeLoss = daemon.HauntedLifeLoss,
                    HauntedTurns = daemon.HauntedTurns,
                    Corrupted = daemon.Corrupted,
                    CorruptedTurns = daemon.CorruptedTurns,
                    Overloaded = daemon.Overloaded,
                    OverloadAttackBonus = daemon.OverloadAttackBonus,
                    OverloadBacklash = daemon.OverloadBacklash,
                    OverloadedTurns = daemon.OverloadedTurns,
                    Taxed = daemon.Taxed,
                    TaxedExtraCost = daemon.TaxedExtraCost,
                    TaxedTurns = daemon.TaxedTurns,
                    Sundered = daemon.Sundered,
                    SunderedTurns = daemon.SunderedTurns,
                    IsSecondFormBound = daemon.IsSecondFormBound,
                    BoundAnchorInstanceIds = daemon.BoundAnchorInstanceIds != null ? new List<string>(daemon.BoundAnchorInstanceIds) : new List<string>(),
                    IsBindAnchor = daemon.IsBindAnchor,
                    BoundSecondFormInstanceId = daemon.BoundSecondFormInstanceId,
                    Masks = BuildMasks(daemon.MaskIds, db),
                };
                field.Add(instance);
            }

            return field;
        }

        private static List<MaskInstance> BuildMasks(string[] maskIds, CardDatabase db)
        {
            var masks = new List<MaskInstance>();
            if (maskIds == null) return masks;

            foreach (var id in maskIds)
            {
                if (db.GetCard(id) is MaskCardData mask)
                    masks.Add(new MaskInstance { Card = mask, TurnsRemaining = 0 });
            }

            return masks;
        }

        private static List<PillarInstance> BuildPillars(SerializablePillar[] source, CardDatabase db)
        {
            var pillars = new List<PillarInstance>();
            if (source == null) return pillars;

            foreach (var pillar in source)
            {
                var card = ResolveCard(db, pillar.CardId, pillar.Card) as PillarCardData;
                pillars.Add(new PillarInstance
                {
                    InstanceId = pillar.InstanceId,
                    Card = card,
                    CurrentHp = pillar.CurrentHp,
                    MaxHp = pillar.MaxHp,
                    Loyalty = pillar.Loyalty,
                    Destroyed = pillar.Destroyed,
                    Revealed = pillar.Revealed,
                    RuntimeArchetype = card != null ? card.archetype : PillarArchetype.Unknown,
                });
            }

            return pillars;
        }

        private static List<AsheCardInstance> BuildAsheCards(SerializableAsheCard[] source, CardDatabase db)
        {
            var cards = new List<AsheCardInstance>();
            if (source == null) return cards;

            foreach (var ashe in source)
            {
                cards.Add(new AsheCardInstance
                {
                    InstanceId = ashe.InstanceId,
                    Card = ResolveCard(db, ashe.CardId, ashe.Card) as AsheCardData,
                    AssignedDaemonInstanceId = ashe.AssignedDaemonInstanceId,
                    ShieldRemaining = ashe.ShieldRemaining,
                    BuffTurnsRemaining = ashe.BuffTurnsRemaining,
                    SuppressedTurnsRemaining = ashe.SuppressedTurnsRemaining,
                });
            }

            return cards;
        }

        private static CardData ResolveCard(CardDatabase db, string cardId, SerializableCardSpec spec)
        {
            CardData resolved = null;
            if (!string.IsNullOrWhiteSpace(cardId))
                resolved = db.GetCard(cardId);
            if (resolved != null)
            {
                HydrateArtwork(resolved, cardId);
                return resolved;
            }

            CardData fallback = CreateFallbackCard(spec, cardId);
            HydrateArtwork(fallback, spec?.CardId ?? cardId);
            return fallback;
        }

        /// <summary>
        /// Sprite references are intentionally not serialized over the network. Rebind
        /// them from the receiving build so projected cards render in every UI path,
        /// including paths that read CardData.artwork directly instead of CardVisual's
        /// Resources fallback.
        /// </summary>
        private static void HydrateArtwork(CardData card, string networkCardId)
        {
            if (card == null || card.artwork != null || card.fullArt != null)
                return;

            string cardId = !string.IsNullOrWhiteSpace(card.cardId)
                ? card.cardId.Trim()
                : networkCardId?.Trim();
            if (string.IsNullOrEmpty(cardId))
                return;

            Sprite art = LoadArtworkSprite(cardId);
            string transmittedId = networkCardId?.Trim();
            if (art == null && !string.IsNullOrEmpty(transmittedId)
                && !string.Equals(cardId, transmittedId, StringComparison.Ordinal))
            {
                art = LoadArtworkSprite(transmittedId);
            }

            if (art == null)
                return;

            card.artwork = art;
            card.fullArt = art;
        }

        private static Sprite LoadArtworkSprite(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return null;

            string id = cardId.Trim();
            Sprite sprite = Resources.Load<Sprite>($"CardArt/Clean/{id}")
                ?? Resources.Load<Sprite>($"CardArt/{id}")
                ?? Resources.Load<Sprite>($"Meshy/{id}")
                ?? Resources.Load<Sprite>($"CardArt/Meshy/{id}");
            if (sprite != null)
                return sprite;

            Texture2D texture = Resources.Load<Texture2D>($"CardArt/Clean/{id}")
                ?? Resources.Load<Texture2D>($"CardArt/{id}")
                ?? Resources.Load<Texture2D>($"Meshy/{id}")
                ?? Resources.Load<Texture2D>($"CardArt/Meshy/{id}");
            return texture != null
                ? Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f))
                : null;
        }

        private static CardData CreateFallbackCard(SerializableCardSpec spec, string cardId)
        {
            if (spec == null && string.IsNullOrWhiteSpace(cardId))
                return null;

            string categoryName = spec?.Category ?? "";
            CardData card = categoryName switch
            {
                nameof(CardCategory.Daemon) => CreateFallbackDaemon(spec),
                nameof(CardCategory.AsheCard) => CreateFallbackSource(spec),
                nameof(CardCategory.Hex) => CreateFallbackHex(spec),
                nameof(CardCategory.Dispel) => CreateFallbackDispel(spec),
                nameof(CardCategory.Domain) => CreateFallbackDomain(spec),
                nameof(CardCategory.Relic) => CreateFallbackRelic(spec),
                _ => ScriptableObject.CreateInstance<CardData>(),
            };

            card.cardId = !string.IsNullOrWhiteSpace(spec?.CardId) ? spec.CardId : cardId;
            card.cardName = !string.IsNullOrWhiteSpace(spec?.Name) ? spec.Name : card.cardId;
            card.category = ParseEnum(spec?.Category, CardCategory.Daemon);
            card.rarity = ParseEnum(spec?.Rarity, Rarity.Common);
            card.description = spec?.Description ?? "";
            card.flavorText = spec?.FlavorText ?? "";
            card.willCost = spec?.Cost ?? -1;
            return card;
        }

        private static DaemonCardData CreateFallbackDaemon(SerializableCardSpec spec)
        {
            var card = ScriptableObject.CreateInstance<DaemonCardData>();
            card.element = ParseEnum(spec?.Element, Element.Flame);
            card.creatureType = ParseEnum(spec?.CreatureType, CreatureType.Elemental);
            card.attack = Math.Max(1, spec?.Attack ?? 1);
            card.ashe = Math.Max(1, spec?.Life ?? 1);
            card.asheCost = Math.Max(0, spec?.AttackCost ?? 0);
            card.rangedAttack = spec?.Ranged ?? false;
            card.attackPattern = ParseEnum(spec?.AttackPattern, card.rangedAttack ? DaemonAttackPattern.Ranged : DaemonAttackPattern.Direct);
            return card;
        }

        private static AsheCardData CreateFallbackSource(SerializableCardSpec spec)
        {
            var card = ScriptableObject.CreateInstance<AsheCardData>();
            card.targetCreatureType = ParseEnum(spec?.CreatureType, CreatureType.Elemental);
            card.sePerTurn = Math.Max(1, spec?.SourceSePerTurn ?? 2);
            return card;
        }

        private static HexCardData CreateFallbackHex(SerializableCardSpec spec)
        {
            var card = ScriptableObject.CreateInstance<HexCardData>();
            card.effectElement = ParseEnum(spec?.Element, Element.Dark);
            return card;
        }

        private static DispelCardData CreateFallbackDispel(SerializableCardSpec spec)
        {
            var card = ScriptableObject.CreateInstance<DispelCardData>();
            card.responseElement = ParseEnum(spec?.Element, Element.Light);
            card.responseCreatureType = ParseEnum(spec?.CreatureType, CreatureType.Spirit);
            card.canCounterAttack = spec?.CanCounterAttack ?? false;
            card.canCounterHex = spec?.CanCounterHex ?? false;
            card.canCounterDomain = spec?.CanCounterDomain ?? false;
            return card;
        }

        private static DomainCardData CreateFallbackDomain(SerializableCardSpec spec)
        {
            var card = ScriptableObject.CreateInstance<DomainCardData>();
            card.effectElement = ParseEnum(spec?.Element, Element.Light);
            return card;
        }

        private static MaskCardData CreateFallbackRelic(SerializableCardSpec spec)
        {
            return ScriptableObject.CreateInstance<MaskCardData>();
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out T parsed)
                ? parsed
                : fallback;
        }

        private static List<CardInstance> BuildPlaceholders(int count)
        {
            var cards = new List<CardInstance>();
            for (int i = 0; i < count; i++)
                cards.Add(new CardInstance { InstanceId = $"hidden-{i}" });
            return cards;
        }

        private static List<SealInstance> BuildSealPlaceholders(int count)
        {
            var seals = new List<SealInstance>();
            for (int i = 0; i < count; i++)
                seals.Add(new SealInstance { InstanceId = $"seal-{i}" });
            return seals;
        }

        private static List<WardInstance> BuildWardPlaceholders(int count)
        {
            var wards = new List<WardInstance>();
            var ids = WardCatalog.StarterWardIds.ToArray();
            for (int i = 0; i < count; i++)
            {
                string id = ids.Length > 0 ? ids[Mathf.Clamp(i, 0, ids.Length - 1)] : "ward-aegis";
                wards.Add(new WardInstance { WardId = id });
            }
            return wards;
        }

        private static List<LogEntry> BuildLog(SerializableGameState source, int localSeat)
        {
            var log = new List<LogEntry>();
            if (source.RecentLog == null) return log;

            foreach (var entry in source.RecentLog)
            {
                log.Add(new LogEntry
                {
                    Turn = entry.Turn,
                    Player = entry.Player == localSeat ? 0 : 1,
                    Message = entry.Message,
                    Type = Enum.TryParse<LogEntryType>(entry.Type, true, out var type) ? type : LogEntryType.System,
                });
            }

            return log;
        }
    }
}
