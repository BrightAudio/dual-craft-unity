// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Full JSON Card Importer
//  Creates ScriptableObjects from all_cards.json
//  Handles all 7 card types: Daemon, Pillar, Domain,
//  Mask, Seal, Dispel, Conjuror + Decks
// ═══════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace DualCraft.Editor
{
    using Cards;
    using Core;
    using Data;

    public class CardImporter : EditorWindow
    {
        private string _jsonPath = "";
        private bool _overwriteExisting = false;
        private Vector2 _scrollPos;
        private string _logOutput = "";

        [MenuItem("Dual Craft/Import Cards from JSON")]
        public static void ShowWindow()
        {
            GetWindow<CardImporter>("Card Importer");
        }

        private void OnGUI()
        {
            GUILayout.Label("Import Card Database", EditorStyles.boldLabel);
            GUILayout.Space(10);

            _jsonPath = EditorGUILayout.TextField("JSON File Path", _jsonPath);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse..."))
            {
                _jsonPath = EditorUtility.OpenFilePanel("Select all_cards.json", "", "json");
            }
            if (GUILayout.Button("Use Default (Resources)"))
            {
                _jsonPath = Path.Combine(Application.dataPath, "Resources/CardData/all_cards.json");
            }
            EditorGUILayout.EndHorizontal();

            _overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing", _overwriteExisting);

            GUILayout.Space(10);

            if (GUILayout.Button("Import All Cards + Decks", GUILayout.Height(30)))
            {
                if (File.Exists(_jsonPath))
                {
                    ImportAll(_jsonPath);
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", $"File not found:\n{_jsonPath}", "OK");
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("Log:", EditorStyles.miniLabel);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(200));
            EditorGUILayout.TextArea(_logOutput, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Log(string msg)
        {
            _logOutput += msg + "\n";
            Debug.Log($"[CardImporter] {msg}");
        }

        private void ImportAll(string path)
        {
            _logOutput = "";
            string json = File.ReadAllText(path);
            Log($"Loaded {json.Length} bytes from {path}");

            var db = CardDatabaseJson.FromJson(json);
            Log($"Parsed {db.totalCards} cards, {db.decks.Length} decks");

            string cardPath = "Assets/Resources/CardData/Cards";
            string deckPath = "Assets/Resources/CardData/Decks";
            EnsureFolder(cardPath);
            EnsureFolder(deckPath);

            // Track created assets for database
            var daemons = new List<DaemonCardData>();
            var pillars = new List<PillarCardData>();
            var domains = new List<DomainCardData>();
            var masks = new List<MaskCardData>();
            var seals = new List<SealCardData>();
            var dispels = new List<DispelCardData>();
            var conjurors = new List<ConjurorCardData>();

            int created = 0, skipped = 0;

            foreach (var entry in db.cards)
            {
                string assetFile = $"{cardPath}/{entry.id}.asset";

                if (!_overwriteExisting && File.Exists(Path.Combine(Directory.GetCurrentDirectory(), assetFile)))
                {
                    skipped++;
                    continue;
                }

                switch (entry.category)
                {
                    case "daemon":
                        var daemon = CreateDaemon(entry);
                        AssetDatabase.CreateAsset(daemon, assetFile);
                        daemons.Add(daemon);
                        break;
                    case "pillar":
                        var pillar = CreatePillar(entry);
                        AssetDatabase.CreateAsset(pillar, assetFile);
                        pillars.Add(pillar);
                        break;
                    case "domain":
                        var domain = CreateDomain(entry);
                        AssetDatabase.CreateAsset(domain, assetFile);
                        domains.Add(domain);
                        break;
                    case "mask":
                        var mask = CreateMask(entry);
                        AssetDatabase.CreateAsset(mask, assetFile);
                        masks.Add(mask);
                        break;
                    case "seal":
                        var seal = CreateSeal(entry);
                        AssetDatabase.CreateAsset(seal, assetFile);
                        seals.Add(seal);
                        break;
                    case "dispel":
                        var dispel = CreateDispel(entry);
                        AssetDatabase.CreateAsset(dispel, assetFile);
                        dispels.Add(dispel);
                        break;
                    case "conjuror":
                        var conjuror = CreateConjuror(entry);
                        AssetDatabase.CreateAsset(conjuror, assetFile);
                        conjurors.Add(conjuror);
                        break;
                    default:
                        Log($"Unknown category: {entry.category} for {entry.id}");
                        continue;
                }
                created++;
            }

            Log($"Cards: {created} created, {skipped} skipped");

            // Create CardDatabase SO
            var cardDb = ScriptableObject.CreateInstance<CardDatabase>();
            cardDb.daemons = daemons.ToArray();
            cardDb.pillars = pillars.ToArray();
            cardDb.domains = domains.ToArray();
            cardDb.masks = masks.ToArray();
            cardDb.seals = seals.ToArray();
            cardDb.dispels = dispels.ToArray();
            cardDb.conjurors = conjurors.ToArray();
            AssetDatabase.CreateAsset(cardDb, "Assets/Resources/CardData/CardDatabase.asset");
            Log($"CardDatabase created with {created} cards total");

            // Create Deck SOs
            int deckCount = 0;
            foreach (var deckEntry in db.decks)
            {
                var deck = CreateDeck(deckEntry);
                string safeName = deckEntry.name.Replace("'", "").Replace(" ", "_");
                AssetDatabase.CreateAsset(deck, $"{deckPath}/{safeName}.asset");
                deckCount++;
            }
            Log($"Decks: {deckCount} created");

            // Wire up evolution references (second pass)
            WireEvolutions(db.cards, cardPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = $"Import complete!\n{created} cards, {deckCount} decks\n" +
                $"Daemons: {daemons.Count}, Pillars: {pillars.Count}, Domains: {domains.Count}\n" +
                $"Masks: {masks.Count}, Seals: {seals.Count}, Dispels: {dispels.Count}, Conjurors: {conjurors.Count}";
            Log(summary);
            EditorUtility.DisplayDialog("Import Complete", summary, "OK");
        }

        // ─── Card Creators ───────────────────────────────

        private DaemonCardData CreateDaemon(CardJsonEntry e)
        {
            var d = ScriptableObject.CreateInstance<DaemonCardData>();
            SetBaseFields(d, e);
            d.element = ParseElement(e.element);
            d.creatureType = ParseCreatureType(e.creatureType);
            d.ashe = e.ashe;
            d.attack = e.attack;
            d.asheCost = e.asheCost;
            d.evolutionCost = e.evolutionCost;
            if (e.ability != null && !string.IsNullOrEmpty(e.ability.name))
            {
                d.ability = new AbilityData
                {
                    abilityName = e.ability.name,
                    description = e.ability.description,
                    type = ParseAbilityType(e.ability.type),
                    effectKey = e.ability.effect,
                };
            }
            return d;
        }

        private PillarCardData CreatePillar(CardJsonEntry e)
        {
            var p = ScriptableObject.CreateInstance<PillarCardData>();
            SetBaseFields(p, e);
            p.element = ParseElement(e.element);
            p.creatureType = ParseCreatureType(e.creatureType);
            p.hp = e.hp;
            p.loyalty = e.loyalty;
            p.passiveAbility = e.passiveAbility ?? "";

            if (e.passiveEffect != null)
            {
                p.passiveEffect = new PillarPassiveData
                {
                    passiveType = e.passiveEffect.type ?? "",
                    element = ParseElement(e.passiveEffect.element),
                    creatureType = ParseCreatureType(e.passiveEffect.creatureType),
                    value = e.passiveEffect.value,
                };
            }

            if (e.onDestroyedEffect != null)
            {
                p.onDestroyedEffect = new PillarDestroyData
                {
                    destroyType = e.onDestroyedEffect.type ?? "",
                    value = e.onDestroyedEffect.value,
                    element = ParseElement(e.onDestroyedEffect.element),
                };
            }

            if (e.activatedAbilities != null)
            {
                p.activatedAbilities = e.activatedAbilities
                    .Select(a => new ActivatedAbilityData
                    {
                        abilityName = a.name,
                        description = a.description,
                        loyaltyCost = a.loyaltyCost,
                        effectKey = a.effect,
                    }).ToArray();
            }

            return p;
        }

        private DomainCardData CreateDomain(CardJsonEntry e)
        {
            var d = ScriptableObject.CreateInstance<DomainCardData>();
            SetBaseFields(d, e);
            if (e.effect != null)
            {
                d.effectType = ParseDomainEffectType(e.effect.type);
                d.effectValue = e.effect.value;
                d.effectElement = ParseElement(e.effect.element);
            }
            return d;
        }

        private MaskCardData CreateMask(CardJsonEntry e)
        {
            var m = ScriptableObject.CreateInstance<MaskCardData>();
            SetBaseFields(m, e);
            m.duration = e.duration;
            if (e.effect != null)
            {
                m.effectType = ParseMaskEffectType(e.effect.type);
                m.effectValue = e.effect.value;
            }
            return m;
        }

        private SealCardData CreateSeal(CardJsonEntry e)
        {
            var s = ScriptableObject.CreateInstance<SealCardData>();
            SetBaseFields(s, e);
            s.trigger = ParseSealTrigger(e.trigger);
            if (e.effect != null)
            {
                s.effectType = ParseSealEffectType(e.effect.type);
                s.effectValue = e.effect.value;
            }
            return s;
        }

        private DispelCardData CreateDispel(CardJsonEntry e)
        {
            var d = ScriptableObject.CreateInstance<DispelCardData>();
            SetBaseFields(d, e);
            d.target = ParseDispelTarget(e.target);
            if (e.counterEffect != null)
            {
                d.counterEffect = new DispelCounterEffectData
                {
                    effectType = e.counterEffect.type ?? "",
                    value = e.counterEffect.value,
                };
            }
            return d;
        }

        private ConjurorCardData CreateConjuror(CardJsonEntry e)
        {
            var c = ScriptableObject.CreateInstance<ConjurorCardData>();
            SetBaseFields(c, e);
            c.element = ParseElement(e.element);
            c.loyalty = e.loyalty;
            if (e.abilities != null)
            {
                c.abilities = e.abilities
                    .Select(a => new ConjurorAbilityData
                    {
                        abilityName = a.name,
                        description = a.description,
                        loyaltyCost = a.loyaltyCost,
                        effectKey = a.effect,
                    }).ToArray();
            }
            return c;
        }

        private DeckData CreateDeck(DeckJsonEntry e)
        {
            var d = ScriptableObject.CreateInstance<DeckData>();
            d.deckName = e.name;
            d.element = ParseElement(e.element);
            d.description = e.description ?? "";
            d.isStarter = e.isStarter;

            // Convert flat card ID array to DeckEntry[] with counts
            if (e.cards != null)
            {
                var counts = new Dictionary<string, int>();
                foreach (var id in e.cards)
                {
                    counts[id] = counts.ContainsKey(id) ? counts[id] + 1 : 1;
                }
                d.cards = counts.Select(kv => new DeckEntry { count = kv.Value }).ToArray();
                // Note: card references will be null until wired up in Unity
            }

            if (e.pillars != null)
            {
                var pCounts = new Dictionary<string, int>();
                foreach (var id in e.pillars)
                {
                    pCounts[id] = pCounts.ContainsKey(id) ? pCounts[id] + 1 : 1;
                }
                d.pillars = pCounts.Select(kv => new DeckEntry { count = kv.Value }).ToArray();
            }

            return d;
        }

        // ─── Helpers ─────────────────────────────────────

        private void SetBaseFields(CardData card, CardJsonEntry e)
        {
            card.cardId = e.id;
            card.cardName = e.name;
            card.category = ParseCategory(e.category);
            card.rarity = ParseRarity(e.rarity);
            card.description = e.description ?? "";
            card.flavorText = e.flavorText ?? "";
        }

        private void WireEvolutions(CardJsonEntry[] cards, string cardPath)
        {
            foreach (var entry in cards)
            {
                if (string.IsNullOrEmpty(entry.evolvesTo)) continue;

                string srcPath = $"{cardPath}/{entry.id}.asset";
                string tgtPath = $"{cardPath}/{entry.evolvesTo}.asset";

                var src = AssetDatabase.LoadAssetAtPath<DaemonCardData>(srcPath);
                var tgt = AssetDatabase.LoadAssetAtPath<DaemonCardData>(tgtPath);

                if (src != null && tgt != null)
                {
                    src.evolvesTo = tgt;
                    EditorUtility.SetDirty(src);
                    Log($"Wired evolution: {entry.id} -> {entry.evolvesTo}");
                }
            }
        }

        private void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        // ─── Enum Parsers ────────────────────────────────

        private Element ParseElement(string s)
        {
            if (string.IsNullOrEmpty(s)) return Element.Flame;
            return s.ToLower() switch
            {
                "flame" => Element.Flame,
                "ice" => Element.Ice,
                "water" => Element.Water,
                "earth" => Element.Earth,
                "air" => Element.Air,
                "light" => Element.Light,
                "dark" => Element.Dark,
                "nature" => Element.Nature,
                _ => Element.Flame,
            };
        }

        private CreatureType ParseCreatureType(string s)
        {
            if (string.IsNullOrEmpty(s)) return CreatureType.Elemental;
            return s.ToLower() switch
            {
                "elemental" => CreatureType.Elemental,
                "machine" => CreatureType.Machine,
                "artificial" => CreatureType.Artificial,
                "spirit" => CreatureType.Spirit,
                "undead" => CreatureType.Undead,
                _ => CreatureType.Elemental,
            };
        }

        private CardCategory ParseCategory(string s)
        {
            return s.ToLower() switch
            {
                "daemon" => CardCategory.Daemon,
                "pillar" => CardCategory.Pillar,
                "domain" => CardCategory.Domain,
                "mask" => CardCategory.Mask,
                "seal" => CardCategory.Seal,
                "dispel" => CardCategory.Dispel,
                "conjuror" => CardCategory.Conjuror,
                _ => CardCategory.Daemon,
            };
        }

        private Rarity ParseRarity(string s)
        {
            return s.ToLower() switch
            {
                "common" => Rarity.Common,
                "rare" => Rarity.Rare,
                "epic" => Rarity.Epic,
                "legendary" => Rarity.Legendary,
                _ => Rarity.Common,
            };
        }

        private AbilityType ParseAbilityType(string s)
        {
            if (string.IsNullOrEmpty(s)) return AbilityType.Passive;
            return s.ToLower() switch
            {
                "passive" => AbilityType.Passive,
                "on-summon" => AbilityType.OnSummon,
                "on-destroy" => AbilityType.OnDestroy,
                _ => AbilityType.Passive,
            };
        }

        private SealTrigger ParseSealTrigger(string s)
        {
            if (string.IsNullOrEmpty(s)) return SealTrigger.OnAttack;
            return s.ToLower() switch
            {
                "on-attack" => SealTrigger.OnAttack,
                "on-summon" => SealTrigger.OnSummon,
                "on-daemon-destroy" => SealTrigger.OnDaemonDestroy,
                "on-spell" => SealTrigger.OnSpell,
                _ => SealTrigger.OnAttack,
            };
        }

        private DispelTarget ParseDispelTarget(string s)
        {
            if (string.IsNullOrEmpty(s)) return DispelTarget.Any;
            return s.ToLower() switch
            {
                "domain" => DispelTarget.Domain,
                "mask" => DispelTarget.Mask,
                "seal" => DispelTarget.Seal,
                "any" => DispelTarget.Any,
                _ => DispelTarget.Any,
            };
        }

        private DomainEffectType ParseDomainEffectType(string s)
        {
            if (string.IsNullOrEmpty(s)) return DomainEffectType.AtkBuffAll;
            return s.ToLower().Replace("-", "").Replace("_", "") switch
            {
                "atkbuffall" => DomainEffectType.AtkBuffAll,
                "damageallend" => DomainEffectType.DamageAllEnd,
                "protection" => DomainEffectType.Protection,
                "elementatkbuff" => DomainEffectType.ElementAtkBuff,
                "extradraw" => DomainEffectType.ExtraDraw,
                "pillarrestore" => DomainEffectType.PillarRestore,
                "pillarheal" => DomainEffectType.PillarHeal,
                _ => DomainEffectType.AtkBuffAll,
            };
        }

        private MaskEffectType ParseMaskEffectType(string s)
        {
            if (string.IsNullOrEmpty(s)) return MaskEffectType.AtkBoost;
            return s.ToLower().Replace("-", "").Replace("_", "") switch
            {
                "atkboost" => MaskEffectType.AtkBoost,
                "asheboost" => MaskEffectType.AsheBoost,
                "haste" => MaskEffectType.Haste,
                "stealth" => MaskEffectType.Stealth,
                "thorns" => MaskEffectType.Thorns,
                "entangle" => MaskEffectType.Entangle,
                _ => MaskEffectType.AtkBoost,
            };
        }

        private SealEffectType ParseSealEffectType(string s)
        {
            if (string.IsNullOrEmpty(s)) return SealEffectType.Drain;
            return s.ToLower().Replace("-", "").Replace("_", "") switch
            {
                "drain" => SealEffectType.Drain,
                "destroy" => SealEffectType.Destroy,
                "negate" => SealEffectType.Negate,
                "counterspell" => SealEffectType.CounterSpell,
                "healconjuror" => SealEffectType.HealConjuror,
                _ => SealEffectType.Drain,
            };
        }
    }
}
#endif
