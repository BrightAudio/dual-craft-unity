// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Automated Project Setup
//  Creates all scenes, prefabs, imports cards, and
//  configures build settings. Run via batch mode:
//  Unity -batchmode -executeMethod DualCraft.Editor.ProjectSetup.RunFullSetup
// ═══════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace DualCraft.Editor
{
    using Cards;
    using Core;
    using Data;
    using UI;
    using Audio;

    public static class ProjectSetup
    {
        // Theme colors matching web version
        static readonly Color BgDark = new(0.043f, 0.043f, 0.043f, 1f);       // #0B0B0B
        static readonly Color Gold = new(0.784f, 0.663f, 0.416f, 1f);         // #C8A96A
        static readonly Color Cream = new(0.961f, 0.941f, 0.910f, 1f);        // #F5F0E8
        static readonly Color DarkPanel = new(0.08f, 0.08f, 0.08f, 0.95f);    // Dark panel bg
        static readonly Color ButtonBg = new(0.12f, 0.12f, 0.12f, 1f);

        [MenuItem("Dual Craft/Run Full Project Setup")]
        public static void RunFullSetup()
        {
            Debug.Log("[ProjectSetup] Starting full project setup...");

            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Resources/CardData/Cards");
            EnsureFolder("Assets/Resources/CardData/Decks");

            // Step 1: Create prefabs
            Debug.Log("[ProjectSetup] Creating prefabs...");
            CreateCardPrefab();
            CreateLogEntryPrefab();

            // Step 2: Import cards from JSON
            Debug.Log("[ProjectSetup] Importing cards from JSON...");
            ImportCardsFromJson();

            // Step 3: Create scenes
            Debug.Log("[ProjectSetup] Creating scenes...");
            CreateMainMenuScene();
            CreateBattleScene();
            CreateCollectionScene();
            CreateDeckBuilderScene();
            CreatePackOpeningScene();

            // Step 4: Configure build settings
            Debug.Log("[ProjectSetup] Configuring build settings...");
            ConfigureBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ProjectSetup] === SETUP COMPLETE ===");
        }

        // ═══════════════════════════════════════════════════
        //  PREFAB CREATION
        // ═══════════════════════════════════════════════════

        static void CreateCardPrefab()
        {
            if (AssetExists("Assets/Prefabs/CardPrefab.prefab")) return;

            var cardGO = new GameObject("CardPrefab");

            // Official Duel Craft card proportions
            var rt = cardGO.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 280);

            // ─── Glow effect (behind everything, hover only) ──
            var glow = CreateUIChild(cardGO, "GlowEffect", Vector2.zero, new Vector2(212, 292));
            var glowImg = glow.AddComponent<Image>();
            glowImg.color = new Color(0.784f, 0.663f, 0.416f, 0.3f);
            glow.SetActive(false);

            // ─── Outer frame (black border — 3px each side) ───
            var outer = CreateUIChild(cardGO, "OuterFrame", Vector2.zero, new Vector2(200, 280));
            var outerImg = outer.AddComponent<Image>();
            outerImg.color = new Color(0.06f, 0.06f, 0.06f, 1f);

            // ─── Card back image (hidden by default) ──────
            var back = CreateUIChild(cardGO, "CardBack", Vector2.zero, new Vector2(194, 274));
            var backImg = back.AddComponent<Image>();
            backImg.color = new Color(0.08f, 0.06f, 0.12f);
            backImg.preserveAspect = true;
            back.SetActive(false);

            // ─── Card front container ─────────────────────
            var front = CreateUIChild(cardGO, "CardFront", Vector2.zero, new Vector2(200, 280));

            // ─── Artwork (edge-to-edge inside black frame, upper 60%) ───
            var artArea = CreateUIChild(front, "Artwork", new Vector2(0, 32), new Vector2(194, 196));
            var artImg = artArea.AddComponent<Image>();
            artImg.color = new Color(0.15f, 0.15f, 0.2f, 1f);
            artImg.preserveAspect = false;

            // ─── Name banner (overlaps top of artwork, semi-transparent white) ───
            var nameBanner = CreateUIChild(front, "NameBanner", new Vector2(0, 119), new Vector2(170, 24));
            var nameBannerImg = nameBanner.AddComponent<Image>();
            nameBannerImg.color = new Color(1f, 1f, 1f, 0.92f);

            var nameGO = CreateUIChild(nameBanner, "NameText", Vector2.zero, new Vector2(162, 24));
            var nameText = nameGO.AddComponent<TextMeshProUGUI>();
            nameText.text = "Card Name";
            nameText.fontSize = 11;
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color = new Color(0.08f, 0.08f, 0.08f, 1f);

            // ─── Description box (below art, visible dark border) ─
            var descBoxBorder = CreateUIChild(front, "DescBoxBorder", new Vector2(0, -72), new Vector2(188, 68));
            var descBorderImg = descBoxBorder.AddComponent<Image>();
            descBorderImg.color = new Color(0.18f, 0.18f, 0.18f, 0.85f);

            var descBox = CreateUIChild(descBoxBorder, "DescBox", Vector2.zero, new Vector2(184, 64));
            var descBoxImg = descBox.AddComponent<Image>();
            descBoxImg.color = new Color(0.96f, 0.95f, 0.92f, 1f);

            var descGO = CreateUIChild(descBox, "DescriptionText", Vector2.zero, new Vector2(176, 58));
            var descText = descGO.AddComponent<TextMeshProUGUI>();
            descText.text = "Card description text goes here.";
            descText.fontSize = 7;
            descText.alignment = TextAlignmentOptions.TopLeft;
            descText.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            descText.enableWordWrapping = true;
            descText.overflowMode = TextOverflowModes.Ellipsis;

            // ─── Cost circle (bottom-left, energy cost badge) ─
            var costBorder = CreateUIChild(front, "CostBorder", new Vector2(-73, -118), new Vector2(30, 30));
            var costBorderImg = costBorder.AddComponent<Image>();
            costBorderImg.color = new Color(0.12f, 0.25f, 0.45f, 0.9f);

            var costCircle = CreateUIChild(costBorder, "CostCircle", Vector2.zero, new Vector2(26, 26));
            var costCircleImg = costCircle.AddComponent<Image>();
            costCircleImg.color = new Color(0.18f, 0.35f, 0.65f, 1f);

            var costGO = CreateUIChild(costCircle, "CostText", Vector2.zero, new Vector2(26, 26));
            var costTextTMP = costGO.AddComponent<TextMeshProUGUI>();
            costTextTMP.text = "2";
            costTextTMP.fontSize = 13;
            costTextTMP.fontStyle = FontStyles.Bold;
            costTextTMP.alignment = TextAlignmentOptions.Center;
            costTextTMP.color = Color.white;

            // ─── Number circle (bottom-right, health/stat badge) ─
            var numBorder = CreateUIChild(front, "NumberBorder", new Vector2(73, -118), new Vector2(30, 30));
            var numBorderImg = numBorder.AddComponent<Image>();
            numBorderImg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            var numCircle = CreateUIChild(numBorder, "NumberCircle", Vector2.zero, new Vector2(26, 26));
            var numCircleImg = numCircle.AddComponent<Image>();
            numCircleImg.color = new Color(0.96f, 0.95f, 0.92f, 1f);

            var numGO = CreateUIChild(numCircle, "NumberText", Vector2.zero, new Vector2(26, 26));
            var numText = numGO.AddComponent<TextMeshProUGUI>();
            numText.text = "8";
            numText.fontSize = 13;
            numText.fontStyle = FontStyles.Bold;
            numText.alignment = TextAlignmentOptions.Center;
            numText.color = new Color(0.08f, 0.08f, 0.08f, 1f);

            // ─── Type line (bottom-center) ────────────────
            var typeGO = CreateUIChild(front, "TypeLineText", new Vector2(-10, -120), new Vector2(140, 18));
            var typeText = typeGO.AddComponent<TextMeshProUGUI>();
            typeText.text = "Darkness Daemon";
            typeText.fontSize = 9;
            typeText.alignment = TextAlignmentOptions.Center;
            typeText.color = new Color(0.3f, 0.3f, 0.3f, 1f);

            // ─── Wire CardVisual component ────────────────
            var visual = cardGO.AddComponent<CardVisual>();
            SetPrivateField(visual, "outerFrame", outerImg);
            SetPrivateField(visual, "artworkImage", artImg);
            SetPrivateField(visual, "nameBanner", nameBannerImg);
            SetPrivateField(visual, "nameText", nameText);
            SetPrivateField(visual, "descBox", descBoxImg);
            SetPrivateField(visual, "descriptionText", descText);
            SetPrivateField(visual, "costCircle", costCircleImg);
            SetPrivateField(visual, "costText", costTextTMP);
            SetPrivateField(visual, "numberCircle", numCircleImg);
            SetPrivateField(visual, "numberText", numText);
            SetPrivateField(visual, "typeLineText", typeText);
            SetPrivateField(visual, "cardFront", front);
            SetPrivateField(visual, "cardBackImage", backImg);
            SetPrivateField(visual, "glowEffect", glowImg);

            // Add Button for interaction
            var btn = cardGO.AddComponent<Button>();
            btn.targetGraphic = outerImg;

            // Add LayoutElement for grid sizing
            var le = cardGO.AddComponent<LayoutElement>();
            le.preferredWidth = 200;
            le.preferredHeight = 280;

            PrefabUtility.SaveAsPrefabAsset(cardGO, "Assets/Prefabs/CardPrefab.prefab");
            Object.DestroyImmediate(cardGO);
            Debug.Log("[ProjectSetup] Created CardPrefab (Duel Craft design)");
        }

        static void CreateLogEntryPrefab()
        {
            if (AssetExists("Assets/Prefabs/LogEntryPrefab.prefab")) return;

            var go = new GameObject("LogEntry");
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 20);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = 11;
            text.color = Color.white;
            text.enableWordWrapping = true;

            var layout = go.AddComponent<ContentSizeFitter>();
            layout.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/LogEntryPrefab.prefab");
            Object.DestroyImmediate(go);
            Debug.Log("[ProjectSetup] Created LogEntryPrefab");
        }

        // ═══════════════════════════════════════════════════
        //  CARD IMPORT
        // ═══════════════════════════════════════════════════

        static void ImportCardsFromJson()
        {
            if (AssetExists("Assets/Resources/CardData/CardDatabase.asset"))
            {
                Debug.Log("[ProjectSetup] CardDatabase already exists, skipping import");
                return;
            }

            string jsonPath = Path.Combine(Application.dataPath, "Resources/CardData/all_cards.json");
            if (!File.Exists(jsonPath))
            {
                Debug.LogError("[ProjectSetup] all_cards.json not found at: " + jsonPath);
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var db = CardDatabaseJson.FromJson(json);
            Debug.Log($"[ProjectSetup] Parsed {db.totalCards} cards, {db.decks.Length} decks");

            string cardPath = "Assets/Resources/CardData/Cards";
            string deckPath = "Assets/Resources/CardData/Decks";
            EnsureFolder(cardPath);
            EnsureFolder(deckPath);

            var daemons = new List<DaemonCardData>();
            var pillars = new List<PillarCardData>();
            var domains = new List<DomainCardData>();
            var masks = new List<MaskCardData>();
            var seals = new List<SealCardData>();
            var dispels = new List<DispelCardData>();
            var conjurors = new List<ConjurorCardData>();

            foreach (var entry in db.cards)
            {
                string assetFile = $"{cardPath}/{entry.id}.asset";
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
                }
            }

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

            // Create decks
            foreach (var deckEntry in db.decks)
            {
                var deck = CreateDeck(deckEntry, cardPath);
                string safeName = deckEntry.name.Replace("'", "").Replace(" ", "_");
                AssetDatabase.CreateAsset(deck, $"{deckPath}/{safeName}.asset");
            }

            // Wire evolutions
            foreach (var entry in db.cards)
            {
                if (string.IsNullOrEmpty(entry.evolvesTo)) continue;
                var src = AssetDatabase.LoadAssetAtPath<DaemonCardData>($"{cardPath}/{entry.id}.asset");
                var tgt = AssetDatabase.LoadAssetAtPath<DaemonCardData>($"{cardPath}/{entry.evolvesTo}.asset");
                if (src != null && tgt != null)
                {
                    src.evolvesTo = tgt;
                    EditorUtility.SetDirty(src);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ProjectSetup] Imported {db.totalCards} cards + {db.decks.Length} decks");
        }

        // ═══════════════════════════════════════════════════
        //  SCENE CREATION
        // ═══════════════════════════════════════════════════

        static void CreateMainMenuScene()
        {
            if (AssetExists("Assets/Scenes/MainMenu.unity")) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera — light pastel background (PTCG Pocket inspiration)
            var camGO = new GameObject("Main Camera");
            var cam = camGO.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.03f, 0.02f, 0.06f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.orthographic = true;
            cam.orthographicSize = 5;
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";

            CreateEventSystem();

            var canvasGO = CreateCanvas("MainMenuCanvas");

            // ─── Background: dark with subtle gradient ───
            var bgPanel = CreatePanel(canvasGO, "Background", new Color(0.02f, 0.02f, 0.06f), true);

            // Top gradient accent (like PTCG Pocket's gradient header)
            var topGrad = CreateUIChild(canvasGO, "TopGradient", Vector2.zero, Vector2.zero);
            var topGradRT = topGrad.GetComponent<RectTransform>();
            topGradRT.anchorMin = new Vector2(0, 0.82f);
            topGradRT.anchorMax = Vector2.one;
            topGradRT.offsetMin = Vector2.zero;
            topGradRT.offsetMax = Vector2.zero;
            var topGradImg = topGrad.AddComponent<Image>();
            topGradImg.color = new Color(Gold.r * 0.15f, Gold.g * 0.1f, Gold.b * 0.05f, 0.4f);

            // ─── Logo / Title area (top center, hub style) ───
            var titleContainer = CreateUIChild(canvasGO, "TitleContainer", Vector2.zero, Vector2.zero);
            var tcRT = titleContainer.GetComponent<RectTransform>();
            tcRT.anchorMin = new Vector2(0.15f, 0.86f);
            tcRT.anchorMax = new Vector2(0.85f, 0.98f);
            tcRT.offsetMin = Vector2.zero;
            tcRT.offsetMax = Vector2.zero;

            var titleGO = CreateUIChild(titleContainer, "TitleText", new Vector2(0, 10), new Vector2(500, 60));
            var titleText = titleGO.AddComponent<TextMeshProUGUI>();
            titleText.text = "DUAL CRAFT";
            titleText.fontSize = 52;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Gold;
            titleText.characterSpacing = 8f;

            var taglineGO = CreateUIChild(titleContainer, "TaglineText", new Vector2(0, -26), new Vector2(500, 28));
            var taglineText = taglineGO.AddComponent<TextMeshProUGUI>();
            taglineText.text = GameConstants.GameTagline;
            taglineText.fontSize = 14;
            taglineText.alignment = TextAlignmentOptions.Center;
            taglineText.color = new Color(Cream.r, Cream.g, Cream.b, 0.7f);
            taglineText.characterSpacing = 3f;

            // ─── Pack showcase area (center — like PTCG Pocket booster carousel) ───
            var packShowcase = CreateUIChild(canvasGO, "PackShowcase", Vector2.zero, Vector2.zero);
            var psRT = packShowcase.GetComponent<RectTransform>();
            psRT.anchorMin = new Vector2(0.05f, 0.48f);
            psRT.anchorMax = new Vector2(0.95f, 0.84f);
            psRT.offsetMin = Vector2.zero;
            psRT.offsetMax = Vector2.zero;
            var psBg = packShowcase.AddComponent<Image>();
            psBg.color = new Color(0.05f, 0.04f, 0.08f, 0.6f);

            // Pack title
            var packTitleGO = CreateUIChild(packShowcase, "PackTitle", Vector2.zero, Vector2.zero);
            var ptRT = packTitleGO.GetComponent<RectTransform>();
            ptRT.anchorMin = new Vector2(0, 0.85f);
            ptRT.anchorMax = Vector2.one;
            ptRT.offsetMin = Vector2.zero;
            ptRT.offsetMax = Vector2.zero;
            var packTitleTMP = packTitleGO.AddComponent<TextMeshProUGUI>();
            packTitleTMP.text = "BOOSTER PACKS";
            packTitleTMP.fontSize = 14;
            packTitleTMP.fontStyle = FontStyles.Bold;
            packTitleTMP.alignment = TextAlignmentOptions.Center;
            packTitleTMP.color = new Color(0.5f, 0.5f, 0.55f);
            packTitleTMP.characterSpacing = 4f;

            // 3 decorative packs (like PTCG Pocket pack carousel)
            float[] packXOffsets = { -200f, 0f, 200f };
            Color[] packColors = {
                new Color(0.9f, 0.3f, 0.1f, 0.8f), // Flame
                new Color(0.4f, 0.2f, 0.6f, 0.8f), // Dark
                new Color(0.2f, 0.8f, 0.3f, 0.8f), // Nature
            };
            string[] packNames = { "INFERNO", "SHADOW", "VERDANT" };

            for (int i = 0; i < 3; i++)
            {
                float scale = i == 1 ? 1f : 0.85f;
                float yOff = i == 1 ? 0f : -10f;
                var packCard = CreateUIChild(packShowcase, $"PackCard{i}", new Vector2(packXOffsets[i], yOff - 15), new Vector2(140 * scale, 200 * scale));
                var pcImg = packCard.AddComponent<Image>();
                pcImg.color = packColors[i];

                // Pack border
                var packBorder = CreateUIChild(packCard, "Border", Vector2.zero, new Vector2(144 * scale, 204 * scale));
                var pbImg = packBorder.AddComponent<Image>();
                pbImg.color = new Color(Gold.r, Gold.g, Gold.b, i == 1 ? 0.4f : 0.15f);
                packBorder.transform.SetSiblingIndex(0);

                // Pack internal label
                var packLabel = CreateUIChild(packCard, "Label", new Vector2(0, -70 * scale), new Vector2(120 * scale, 24));
                var plTMP = packLabel.AddComponent<TextMeshProUGUI>();
                plTMP.text = packNames[i];
                plTMP.fontSize = Mathf.RoundToInt(12 * scale);
                plTMP.fontStyle = FontStyles.Bold;
                plTMP.alignment = TextAlignmentOptions.Center;
                plTMP.color = Color.white;
                plTMP.characterSpacing = 3f;
            }

            // Open packs button (below carousel)
            var openPacksBtn = CreateStyledButton(packShowcase, "OpenPacksButton", "OPEN PACKS", Gold, true);
            var opRT = openPacksBtn.GetComponent<RectTransform>();
            opRT.anchorMin = new Vector2(0.3f, 0.0f);
            opRT.anchorMax = new Vector2(0.7f, 0.12f);
            opRT.offsetMin = Vector2.zero;
            opRT.offsetMax = Vector2.zero;
            opRT.anchoredPosition = Vector2.zero;

            // ─── Feature cards row (mid section — like PTCG Pocket Wonder Pick / Shop) ───
            var featureRow = CreateUIChild(canvasGO, "FeatureRow", Vector2.zero, Vector2.zero);
            var frRT = featureRow.GetComponent<RectTransform>();
            frRT.anchorMin = new Vector2(0.05f, 0.30f);
            frRT.anchorMax = new Vector2(0.95f, 0.47f);
            frRT.offsetMin = Vector2.zero;
            frRT.offsetMax = Vector2.zero;
            var frHLG = featureRow.AddComponent<HorizontalLayoutGroup>();
            frHLG.spacing = 16;
            frHLG.childAlignment = TextAnchor.MiddleCenter;
            frHLG.childForceExpandWidth = true;
            frHLG.childForceExpandHeight = true;
            frHLG.padding = new RectOffset(8, 8, 8, 8);

            // Battle card
            var battleCard = CreateFeatureCard(featureRow, "BattleCard", "BATTLE", "Versus & Solo", new Color(0.15f, 0.25f, 0.45f));
            var aiBtn = battleCard; // Use as AI duel button

            // Grimoire card
            var collCard = CreateFeatureCard(featureRow, "GrimoireCard", "GRIMOIRE", "117 Cards", new Color(0.25f, 0.15f, 0.35f));

            // Deck Builder card
            var deckCard = CreateFeatureCard(featureRow, "DeckCard", "DECKS", "Build & Edit", new Color(0.15f, 0.30f, 0.20f));

            // ─── Bottom action row ───
            var bottomRow = CreateUIChild(canvasGO, "BottomRow", Vector2.zero, Vector2.zero);
            var brRT = bottomRow.GetComponent<RectTransform>();
            brRT.anchorMin = new Vector2(0.05f, 0.15f);
            brRT.anchorMax = new Vector2(0.95f, 0.28f);
            brRT.offsetMin = Vector2.zero;
            brRT.offsetMax = Vector2.zero;
            var brHLG = bottomRow.AddComponent<HorizontalLayoutGroup>();
            brHLG.spacing = 12;
            brHLG.childAlignment = TextAnchor.MiddleCenter;
            brHLG.childForceExpandWidth = true;
            brHLG.childForceExpandHeight = true;
            brHLG.padding = new RectOffset(8, 8, 6, 6);

            var shopBtn = CreateFeatureCard(bottomRow, "ShopCard", "SHOP", "Coming Soon", new Color(0.3f, 0.25f, 0.15f));
            var storyBtn = CreateFeatureCard(bottomRow, "StoryCard", "STORY", "Campaign", new Color(0.2f, 0.15f, 0.30f));
            var settingsBtn = CreateFeatureCard(bottomRow, "SettingsCard", "SETTINGS", "", new Color(0.12f, 0.12f, 0.15f));

            // ─── Bottom nav bar (PTCG Pocket style persistent tab bar) ───
            CreateBottomNavBar(canvasGO);

            // ─── Version text ───
            var verGO = CreateUIChild(canvasGO, "VersionText", Vector2.zero, Vector2.zero);
            var verRT = verGO.GetComponent<RectTransform>();
            verRT.anchorMin = new Vector2(0, 0.08f);
            verRT.anchorMax = new Vector2(1, 0.13f);
            verRT.offsetMin = Vector2.zero;
            verRT.offsetMax = Vector2.zero;
            var verText = verGO.AddComponent<TextMeshProUGUI>();
            verText.text = "v0.2.0 — Craft Your Legacy.";
            verText.fontSize = 11;
            verText.alignment = TextAlignmentOptions.Center;
            verText.color = new Color(0.3f, 0.3f, 0.35f);

            // ─── Wire MainMenuController ───
            var controller = canvasGO.AddComponent<MainMenuController>();
            SetPrivateField(controller, "aiDuelButton", battleCard.GetComponent<Button>());
            SetPrivateField(controller, "collectionButton", collCard.GetComponent<Button>());
            SetPrivateField(controller, "deckBuilderButton", deckCard.GetComponent<Button>());
            SetPrivateField(controller, "shopButton", shopBtn.GetComponent<Button>());
            SetPrivateField(controller, "storyButton", storyBtn.GetComponent<Button>());
            SetPrivateField(controller, "settingsButton", settingsBtn.GetComponent<Button>());
            SetPrivateField(controller, "openPacksButton", openPacksBtn.GetComponent<Button>());
            SetPrivateField(controller, "titleText", titleText);
            SetPrivateField(controller, "taglineText", taglineText);

            // Music hook
            var mmMusic = canvasGO.AddComponent<MusicSceneHook>();
            SetPrivateField(mmMusic, "musicMode", MusicManager.MusicMode.Menu);

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/MainMenu.unity");
            Debug.Log("[ProjectSetup] Created MainMenu scene (Hub layout)");
        }

        static void CreateBattleScene()
        {
            if (AssetExists("Assets/Scenes/Battle.unity")) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera
            var camGO = new GameObject("Main Camera");
            var cam = camGO.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.025f, 0.02f, 0.045f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.orthographic = true;
            cam.orthographicSize = 5;
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";

            CreateEventSystem();

            var canvasGO = CreateCanvas("BattleCanvas");

            // Load prefabs
            var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CardPrefab.prefab");
            var logPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LogEntryPrefab.prefab");
            var cardDb = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/Resources/CardData/CardDatabase.asset");

            DeckData deck1 = null, deck2 = null;
            var deckGuids = AssetDatabase.FindAssets("t:DeckData", new[] { "Assets/Resources/CardData/Decks" });
            if (deckGuids.Length > 0) deck1 = AssetDatabase.LoadAssetAtPath<DeckData>(AssetDatabase.GUIDToAssetPath(deckGuids[0]));
            if (deckGuids.Length > 1) deck2 = AssetDatabase.LoadAssetAtPath<DeckData>(AssetDatabase.GUIDToAssetPath(deckGuids[1]));

            // ─── Full-screen background ───
            var bgPanel = CreatePanel(canvasGO, "Background", new Color(0.025f, 0.025f, 0.05f), true);

            // ═══ OPPONENT SIDE (TOP 45%) ═════════════════
            // Layout from top: ConjurorZone → PillarRow → DaemonField → Hand

            // P2 Conjuror zone (top strip)
            var p2ConjZone = CreateUIChild(canvasGO, "P2ConjurorZone", Vector2.zero, Vector2.zero);
            var p2CzRT = p2ConjZone.GetComponent<RectTransform>();
            p2CzRT.anchorMin = new Vector2(0.15f, 0.90f);
            p2CzRT.anchorMax = new Vector2(0.75f, 0.98f);
            p2CzRT.offsetMin = Vector2.zero;
            p2CzRT.offsetMax = Vector2.zero;
            var p2CzBg = p2ConjZone.AddComponent<Image>();
            p2CzBg.color = new Color(0.08f, 0.03f, 0.03f, 0.4f);
            // P2 info bar inside conjuror zone
            var p2InfoHLG = p2ConjZone.AddComponent<HorizontalLayoutGroup>();
            p2InfoHLG.spacing = 14;
            p2InfoHLG.childAlignment = TextAnchor.MiddleCenter;
            p2InfoHLG.childForceExpandWidth = false;
            p2InfoHLG.childForceExpandHeight = true;
            p2InfoHLG.padding = new RectOffset(12, 12, 4, 4);

            var p2NameGO = CreateUIChild(p2ConjZone, "P2NameText", Vector2.zero, new Vector2(130, 30));
            var p2Name = p2NameGO.AddComponent<TextMeshProUGUI>();
            p2Name.text = "Opponent";
            p2Name.fontSize = 18;
            p2Name.fontStyle = FontStyles.Bold;
            p2Name.color = new Color(0.9f, 0.4f, 0.4f);
            p2NameGO.AddComponent<LayoutElement>().preferredWidth = 130;

            var p2HpGO = CreateUIChild(p2ConjZone, "P2HpText", Vector2.zero, new Vector2(80, 24));
            var p2HpText = p2HpGO.AddComponent<TextMeshProUGUI>();
            p2HpText.text = "30/30";
            p2HpText.fontSize = 16;
            p2HpText.color = new Color(1f, 0.85f, 0.85f);
            p2HpGO.AddComponent<LayoutElement>().preferredWidth = 80;

            var p2HpBarGO = CreateUIChild(p2ConjZone, "P2HpBar", Vector2.zero, new Vector2(160, 16));
            var p2HpBar = p2HpBarGO.AddComponent<Slider>();
            SetupSlider(p2HpBar, new Color(0.85f, 0.2f, 0.2f));
            p2HpBarGO.AddComponent<LayoutElement>().preferredWidth = 160;

            var p2WillGO = CreateUIChild(p2ConjZone, "P2WillText", Vector2.zero, new Vector2(80, 24));
            var p2Will = p2WillGO.AddComponent<TextMeshProUGUI>();
            p2Will.text = "Will: 1/10";
            p2Will.fontSize = 13;
            p2Will.color = new Color(0.4f, 0.75f, 1f);
            p2WillGO.AddComponent<LayoutElement>().preferredWidth = 80;

            var p2DeckCntGO = CreateUIChild(p2ConjZone, "P2DeckCountText", Vector2.zero, new Vector2(70, 24));
            var p2DeckCnt = p2DeckCntGO.AddComponent<TextMeshProUGUI>();
            p2DeckCnt.text = "Deck: 40";
            p2DeckCnt.fontSize = 13;
            p2DeckCnt.color = Cream;
            p2DeckCntGO.AddComponent<LayoutElement>().preferredWidth = 70;

            // P2 Pillar row (face-down, behind daemons)
            var p2PillarZoneBg = CreateUIChild(canvasGO, "P2PillarZoneBg", Vector2.zero, Vector2.zero);
            var p2PzBgRT = p2PillarZoneBg.GetComponent<RectTransform>();
            p2PzBgRT.anchorMin = new Vector2(0.1f, 0.78f);
            p2PzBgRT.anchorMax = new Vector2(0.75f, 0.89f);
            p2PzBgRT.offsetMin = Vector2.zero;
            p2PzBgRT.offsetMax = Vector2.zero;
            var p2PzBgImg = p2PillarZoneBg.AddComponent<Image>();
            p2PzBgImg.color = new Color(0.06f, 0.02f, 0.08f, 0.25f);

            var p2Pillar = CreateHorizontalContainer(canvasGO, "P2PillarContainer", Vector2.zero, Vector2.zero);
            var p2PillarRT = p2Pillar.GetComponent<RectTransform>();
            p2PillarRT.anchorMin = new Vector2(0.1f, 0.78f);
            p2PillarRT.anchorMax = new Vector2(0.75f, 0.89f);
            p2PillarRT.offsetMin = new Vector2(8, 2);
            p2PillarRT.offsetMax = new Vector2(-8, -2);
            p2Pillar.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            // P2 Daemon field (main combat zone)
            var p2FieldZoneBg = CreateUIChild(canvasGO, "P2FieldZoneBg", Vector2.zero, Vector2.zero);
            var p2FzBgRT = p2FieldZoneBg.GetComponent<RectTransform>();
            p2FzBgRT.anchorMin = new Vector2(0.05f, 0.60f);
            p2FzBgRT.anchorMax = new Vector2(0.80f, 0.77f);
            p2FzBgRT.offsetMin = Vector2.zero;
            p2FzBgRT.offsetMax = Vector2.zero;
            var p2FzBgImg = p2FieldZoneBg.AddComponent<Image>();
            p2FzBgImg.color = new Color(0.08f, 0.04f, 0.04f, 0.3f);

            var p2Field = CreateHorizontalContainer(canvasGO, "P2FieldContainer", Vector2.zero, Vector2.zero);
            var p2FieldRT = p2Field.GetComponent<RectTransform>();
            p2FieldRT.anchorMin = new Vector2(0.05f, 0.60f);
            p2FieldRT.anchorMax = new Vector2(0.80f, 0.77f);
            p2FieldRT.offsetMin = new Vector2(8, 2);
            p2FieldRT.offsetMax = new Vector2(-8, -2);
            p2Field.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            // P2 Hand (face-down, at edge near center)
            var p2Hand = CreateHorizontalContainer(canvasGO, "P2HandContainer", Vector2.zero, Vector2.zero);
            var p2HandRT = p2Hand.GetComponent<RectTransform>();
            p2HandRT.anchorMin = new Vector2(0.1f, 0.53f);
            p2HandRT.anchorMax = new Vector2(0.75f, 0.59f);
            p2HandRT.offsetMin = new Vector2(8, 0);
            p2HandRT.offsetMax = new Vector2(-8, 0);
            p2Hand.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            // ═══ CENTER DIVIDER + CONTROLS ═══════════════
            // Gold divider line
            var divider = CreateUIChild(canvasGO, "BoardDivider", Vector2.zero, Vector2.zero);
            var divRT = divider.GetComponent<RectTransform>();
            divRT.anchorMin = new Vector2(0.02f, 0.498f);
            divRT.anchorMax = new Vector2(0.82f, 0.502f);
            divRT.offsetMin = Vector2.zero;
            divRT.offsetMax = Vector2.zero;
            var dividerImg = divider.AddComponent<Image>();
            dividerImg.color = new Color(Gold.r, Gold.g, Gold.b, 0.35f);

            // Center controls panel
            var centerPanel = CreateUIChild(canvasGO, "CenterControls", Vector2.zero, Vector2.zero);
            var cpRT = centerPanel.GetComponent<RectTransform>();
            cpRT.anchorMin = new Vector2(0.2f, 0.47f);
            cpRT.anchorMax = new Vector2(0.7f, 0.53f);
            cpRT.offsetMin = Vector2.zero;
            cpRT.offsetMax = Vector2.zero;
            var centerBg = centerPanel.AddComponent<Image>();
            centerBg.color = new Color(0.06f, 0.05f, 0.08f, 0.90f);

            var centerHLG = centerPanel.AddComponent<HorizontalLayoutGroup>();
            centerHLG.spacing = 8;
            centerHLG.childAlignment = TextAnchor.MiddleCenter;
            centerHLG.childForceExpandWidth = false;
            centerHLG.childForceExpandHeight = true;
            centerHLG.padding = new RectOffset(8, 8, 2, 2);

            var phaseGO = CreateUIChild(centerPanel, "PhaseText", Vector2.zero, new Vector2(120, 30));
            var phaseText = phaseGO.AddComponent<TextMeshProUGUI>();
            phaseText.text = "DRAW PHASE";
            phaseText.fontSize = 14;
            phaseText.fontStyle = FontStyles.Bold;
            phaseText.alignment = TextAlignmentOptions.Center;
            phaseText.color = Gold;
            phaseGO.AddComponent<LayoutElement>().preferredWidth = 120;

            var turnGO = CreateUIChild(centerPanel, "TurnText", Vector2.zero, new Vector2(60, 24));
            var turnText = turnGO.AddComponent<TextMeshProUGUI>();
            turnText.text = "Turn 1";
            turnText.fontSize = 12;
            turnText.alignment = TextAlignmentOptions.Center;
            turnText.color = Cream;
            turnGO.AddComponent<LayoutElement>().preferredWidth = 60;

            var nextPhaseGO = CreateMenuButton(centerPanel, "NextPhaseButton", "NEXT", Gold);
            nextPhaseGO.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 32);
            nextPhaseGO.GetComponent<LayoutElement>().preferredWidth = 80;
            nextPhaseGO.GetComponent<LayoutElement>().preferredHeight = 32;

            var endTurnGO = CreateMenuButton(centerPanel, "EndTurnButton", "END TURN", new Color(1f, 0.4f, 0.4f));
            endTurnGO.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 32);
            endTurnGO.GetComponent<LayoutElement>().preferredWidth = 100;
            endTurnGO.GetComponent<LayoutElement>().preferredHeight = 32;

            // ═══ DICE ROLLER (right of center) ═══════════
            var diceArea = CreateUIChild(canvasGO, "DiceArea", Vector2.zero, Vector2.zero);
            var daRT = diceArea.GetComponent<RectTransform>();
            daRT.anchorMin = new Vector2(0.72f, 0.45f);
            daRT.anchorMax = new Vector2(0.80f, 0.55f);
            daRT.offsetMin = Vector2.zero;
            daRT.offsetMax = Vector2.zero;
            var diceAreaBg = diceArea.AddComponent<Image>();
            diceAreaBg.color = new Color(0.08f, 0.07f, 0.10f, 0.95f);

            var diceFace = CreateUIChild(diceArea, "DiceBackground", Vector2.zero, new Vector2(60, 60));
            var diceFaceImg = diceFace.AddComponent<Image>();
            diceFaceImg.color = new Color(0.12f, 0.12f, 0.15f);

            var diceValGO = CreateUIChild(diceFace, "DiceValueText", Vector2.zero, new Vector2(50, 50));
            var diceVal = diceValGO.AddComponent<TextMeshProUGUI>();
            diceVal.text = "?";
            diceVal.fontSize = 38;
            diceVal.fontStyle = FontStyles.Bold;
            diceVal.alignment = TextAlignmentOptions.Center;
            diceVal.color = Gold;

            var diceResultGO = CreateUIChild(diceArea, "DiceResultLabel", new Vector2(0, -45), new Vector2(100, 20));
            var diceResult = diceResultGO.AddComponent<TextMeshProUGUI>();
            diceResult.text = "";
            diceResult.fontSize = 11;
            diceResult.alignment = TextAlignmentOptions.Center;
            diceResult.color = Cream;

            var diceRoller = diceArea.AddComponent<DiceRoller>();
            SetPrivateField(diceRoller, "diceBackground", diceFaceImg);
            SetPrivateField(diceRoller, "diceValueText", diceVal);
            SetPrivateField(diceRoller, "resultLabel", diceResult);
            diceArea.SetActive(false);

            // ═══ PLAYER 1 SIDE (BOTTOM 45%) ══════════════
            // Layout from bottom: Hand → DaemonField → PillarRow → Conjuror zone

            // P1 Hand (bottom edge)
            var p1Hand = CreateHorizontalContainer(canvasGO, "P1HandContainer", Vector2.zero, Vector2.zero);
            var p1HandRT = p1Hand.GetComponent<RectTransform>();
            p1HandRT.anchorMin = new Vector2(0.05f, 0.01f);
            p1HandRT.anchorMax = new Vector2(0.80f, 0.12f);
            p1HandRT.offsetMin = new Vector2(8, 4);
            p1HandRT.offsetMax = new Vector2(-8, 0);
            p1Hand.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            // P1 Daemon field
            var p1FieldZoneBg = CreateUIChild(canvasGO, "P1FieldZoneBg", Vector2.zero, Vector2.zero);
            var p1FzBgRT = p1FieldZoneBg.GetComponent<RectTransform>();
            p1FzBgRT.anchorMin = new Vector2(0.05f, 0.13f);
            p1FzBgRT.anchorMax = new Vector2(0.80f, 0.30f);
            p1FzBgRT.offsetMin = Vector2.zero;
            p1FzBgRT.offsetMax = Vector2.zero;
            var p1FzBgImg = p1FieldZoneBg.AddComponent<Image>();
            p1FzBgImg.color = new Color(0.04f, 0.04f, 0.08f, 0.3f);

            var p1Field = CreateHorizontalContainer(canvasGO, "P1FieldContainer", Vector2.zero, Vector2.zero);
            var p1FieldRT = p1Field.GetComponent<RectTransform>();
            p1FieldRT.anchorMin = new Vector2(0.05f, 0.13f);
            p1FieldRT.anchorMax = new Vector2(0.80f, 0.30f);
            p1FieldRT.offsetMin = new Vector2(8, 2);
            p1FieldRT.offsetMax = new Vector2(-8, -2);
            p1Field.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            // P1 Pillar row
            var p1PillarZoneBg = CreateUIChild(canvasGO, "P1PillarZoneBg", Vector2.zero, Vector2.zero);
            var p1PzBgRT = p1PillarZoneBg.GetComponent<RectTransform>();
            p1PzBgRT.anchorMin = new Vector2(0.1f, 0.31f);
            p1PzBgRT.anchorMax = new Vector2(0.75f, 0.42f);
            p1PzBgRT.offsetMin = Vector2.zero;
            p1PzBgRT.offsetMax = Vector2.zero;
            var p1PzBgImg = p1PillarZoneBg.AddComponent<Image>();
            p1PzBgImg.color = new Color(0.02f, 0.04f, 0.08f, 0.25f);

            var p1Pillar = CreateHorizontalContainer(canvasGO, "P1PillarContainer", Vector2.zero, Vector2.zero);
            var p1PillarRT = p1Pillar.GetComponent<RectTransform>();
            p1PillarRT.anchorMin = new Vector2(0.1f, 0.31f);
            p1PillarRT.anchorMax = new Vector2(0.75f, 0.42f);
            p1PillarRT.offsetMin = new Vector2(8, 2);
            p1PillarRT.offsetMax = new Vector2(-8, -2);
            p1Pillar.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            // P1 Conjuror zone (info bar above pillars)
            var p1ConjZone = CreateUIChild(canvasGO, "P1ConjurorZone", Vector2.zero, Vector2.zero);
            var p1CzRT = p1ConjZone.GetComponent<RectTransform>();
            p1CzRT.anchorMin = new Vector2(0.15f, 0.43f);
            p1CzRT.anchorMax = new Vector2(0.75f, 0.47f);
            p1CzRT.offsetMin = Vector2.zero;
            p1CzRT.offsetMax = Vector2.zero;
            var p1CzBg = p1ConjZone.AddComponent<Image>();
            p1CzBg.color = new Color(0.03f, 0.05f, 0.08f, 0.4f);
            var p1InfoHLG = p1ConjZone.AddComponent<HorizontalLayoutGroup>();
            p1InfoHLG.spacing = 14;
            p1InfoHLG.childAlignment = TextAnchor.MiddleCenter;
            p1InfoHLG.childForceExpandWidth = false;
            p1InfoHLG.childForceExpandHeight = true;
            p1InfoHLG.padding = new RectOffset(12, 12, 2, 2);

            var p1NameGO = CreateUIChild(p1ConjZone, "P1NameText", Vector2.zero, new Vector2(130, 30));
            var p1Name = p1NameGO.AddComponent<TextMeshProUGUI>();
            p1Name.text = "Player 1";
            p1Name.fontSize = 18;
            p1Name.fontStyle = FontStyles.Bold;
            p1Name.color = new Color(0.4f, 0.85f, 0.4f);
            p1NameGO.AddComponent<LayoutElement>().preferredWidth = 130;

            var p1HpGO = CreateUIChild(p1ConjZone, "P1HpText", Vector2.zero, new Vector2(80, 24));
            var p1HpText = p1HpGO.AddComponent<TextMeshProUGUI>();
            p1HpText.text = "30/30";
            p1HpText.fontSize = 16;
            p1HpText.color = new Color(0.85f, 1f, 0.85f);
            p1HpGO.AddComponent<LayoutElement>().preferredWidth = 80;

            var p1HpBarGO = CreateUIChild(p1ConjZone, "P1HpBar", Vector2.zero, new Vector2(160, 16));
            var p1HpBar = p1HpBarGO.AddComponent<Slider>();
            SetupSlider(p1HpBar, new Color(0.2f, 0.8f, 0.2f));
            p1HpBarGO.AddComponent<LayoutElement>().preferredWidth = 160;

            var p1WillGO = CreateUIChild(p1ConjZone, "P1WillText", Vector2.zero, new Vector2(80, 24));
            var p1Will = p1WillGO.AddComponent<TextMeshProUGUI>();
            p1Will.text = "Will: 1/10";
            p1Will.fontSize = 13;
            p1Will.color = new Color(0.4f, 0.75f, 1f);
            p1WillGO.AddComponent<LayoutElement>().preferredWidth = 80;

            var p1DeckCntGO = CreateUIChild(p1ConjZone, "P1DeckCountText", Vector2.zero, new Vector2(70, 24));
            var p1DeckCnt = p1DeckCntGO.AddComponent<TextMeshProUGUI>();
            p1DeckCnt.text = "Deck: 40";
            p1DeckCnt.fontSize = 13;
            p1DeckCnt.color = Cream;
            p1DeckCntGO.AddComponent<LayoutElement>().preferredWidth = 70;

            // ═══ GAME LOG (right sidebar) ════════════════
            var logPanel = CreatePanel(canvasGO, "GameLogPanel", new Color(0.04f, 0.04f, 0.06f, 0.92f), false);
            var logRT = logPanel.GetComponent<RectTransform>();
            logRT.anchorMin = new Vector2(0.82f, 0.02f);
            logRT.anchorMax = new Vector2(0.99f, 0.98f);
            logRT.offsetMin = Vector2.zero;
            logRT.offsetMax = Vector2.zero;

            var logHeaderGO = CreateUIChild(logPanel, "LogHeader", Vector2.zero, Vector2.zero);
            var logHeaderRT = logHeaderGO.GetComponent<RectTransform>();
            logHeaderRT.anchorMin = new Vector2(0, 0.94f);
            logHeaderRT.anchorMax = Vector2.one;
            logHeaderRT.offsetMin = Vector2.zero;
            logHeaderRT.offsetMax = Vector2.zero;
            var logHeaderBg = logHeaderGO.AddComponent<Image>();
            logHeaderBg.color = new Color(0.06f, 0.05f, 0.08f);
            var logTitle = CreateUIChild(logHeaderGO, "LogTitle", Vector2.zero, Vector2.zero);
            var logTitleRT = logTitle.GetComponent<RectTransform>();
            logTitleRT.anchorMin = Vector2.zero;
            logTitleRT.anchorMax = Vector2.one;
            logTitleRT.offsetMin = Vector2.zero;
            logTitleRT.offsetMax = Vector2.zero;
            var logTitleTMP = logTitle.AddComponent<TextMeshProUGUI>();
            logTitleTMP.text = "BATTLE LOG";
            logTitleTMP.fontSize = 12;
            logTitleTMP.fontStyle = FontStyles.Bold;
            logTitleTMP.alignment = TextAlignmentOptions.Center;
            logTitleTMP.color = Gold;

            var logScrollArea = CreateUIChild(logPanel, "LogScrollArea", Vector2.zero, Vector2.zero);
            var logScrollAreaRT = logScrollArea.GetComponent<RectTransform>();
            logScrollAreaRT.anchorMin = Vector2.zero;
            logScrollAreaRT.anchorMax = new Vector2(1, 0.93f);
            logScrollAreaRT.offsetMin = new Vector2(2, 2);
            logScrollAreaRT.offsetMax = new Vector2(-2, 0);
            logScrollArea.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var logScroll = logScrollArea.AddComponent<ScrollRect>();
            logScroll.vertical = true;
            logScroll.horizontal = false;

            var logContent = CreateUIChild(logScrollArea, "LogContainer", Vector2.zero, new Vector2(0, 0));
            var logContentRT = logContent.GetComponent<RectTransform>();
            logContentRT.anchorMin = new Vector2(0, 1);
            logContentRT.anchorMax = Vector2.one;
            logContentRT.pivot = new Vector2(0.5f, 1);
            var logVLG = logContent.AddComponent<VerticalLayoutGroup>();
            logVLG.spacing = 2;
            logVLG.childAlignment = TextAnchor.UpperLeft;
            logVLG.childForceExpandWidth = true;
            logVLG.childForceExpandHeight = false;
            logVLG.padding = new RectOffset(4, 4, 4, 4);
            var logCSF = logContent.AddComponent<ContentSizeFitter>();
            logCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            logScroll.content = logContentRT;

            // ═══ ZONE LABELS ═════════════════════════════
            void AddZoneLabel(string name, string text, Vector2 anchorMin, Vector2 anchorMax)
            {
                var lbl = CreateUIChild(canvasGO, name, Vector2.zero, Vector2.zero);
                var lrt = lbl.GetComponent<RectTransform>();
                lrt.anchorMin = anchorMin;
                lrt.anchorMax = anchorMax;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
                var tmp = lbl.AddComponent<TextMeshProUGUI>();
                tmp.text = text;
                tmp.fontSize = 9;
                tmp.fontStyle = FontStyles.Italic;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.color = new Color(0.4f, 0.4f, 0.45f, 0.6f);
            }

            AddZoneLabel("P2PillarLabel", "PILLARS", new Vector2(0.02f, 0.85f), new Vector2(0.09f, 0.88f));
            AddZoneLabel("P2FieldLabel", "DAEMONS", new Vector2(0.02f, 0.72f), new Vector2(0.09f, 0.75f));
            AddZoneLabel("P1FieldLabel", "DAEMONS", new Vector2(0.02f, 0.26f), new Vector2(0.09f, 0.29f));
            AddZoneLabel("P1PillarLabel", "PILLARS", new Vector2(0.02f, 0.38f), new Vector2(0.09f, 0.41f));

            // ═══ GAME OVER OVERLAY ═══════════════════════
            var goPanel = CreatePanel(canvasGO, "GameOverPanel", new Color(0, 0, 0, 0.88f), true);

            var goGlow = CreateUIChild(goPanel, "VictoryGlow", Vector2.zero, new Vector2(500, 300));
            var goGlowImg = goGlow.AddComponent<Image>();
            goGlowImg.color = new Color(Gold.r, Gold.g, Gold.b, 0.08f);

            var goText = CreateUIChild(goPanel, "GameOverText", Vector2.zero, new Vector2(500, 200));
            var goTMP = goText.AddComponent<TextMeshProUGUI>();
            goTMP.text = "VICTORY!";
            goTMP.fontSize = 56;
            goTMP.fontStyle = FontStyles.Bold;
            goTMP.alignment = TextAlignmentOptions.Center;
            goTMP.color = Gold;
            goTMP.characterSpacing = 6f;

            var goBackBtn = CreateMenuButton(goPanel, "BackToMenuButton", "MAIN MENU", Cream);
            var goBackRT = goBackBtn.GetComponent<RectTransform>();
            goBackRT.anchoredPosition = new Vector2(0, -120);
            goBackBtn.GetComponent<Button>().onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

            goPanel.SetActive(false);

            // ═══ WIRE BATTLE CONTROLLER ═════════════════
            var bsc = canvasGO.AddComponent<BattleSceneController>();
            SetPrivateField(bsc, "cardDatabase", cardDb);
            SetPrivateField(bsc, "player1Deck", deck1);
            SetPrivateField(bsc, "player2Deck", deck2);
            SetPrivateField(bsc, "p1HandContainer", p1Hand.transform);
            SetPrivateField(bsc, "p1FieldContainer", p1Field.transform);
            SetPrivateField(bsc, "p1PillarContainer", p1Pillar.transform);
            SetPrivateField(bsc, "p1NameText", p1Name);
            SetPrivateField(bsc, "p1HpText", p1HpText);
            SetPrivateField(bsc, "p1HpBar", p1HpBar);
            SetPrivateField(bsc, "p1WillText", p1Will);
            SetPrivateField(bsc, "p1DeckCountText", p1DeckCnt);
            SetPrivateField(bsc, "p2HandContainer", p2Hand.transform);
            SetPrivateField(bsc, "p2FieldContainer", p2Field.transform);
            SetPrivateField(bsc, "p2PillarContainer", p2Pillar.transform);
            SetPrivateField(bsc, "p2NameText", p2Name);
            SetPrivateField(bsc, "p2HpText", p2HpText);
            SetPrivateField(bsc, "p2HpBar", p2HpBar);
            SetPrivateField(bsc, "p2WillText", p2Will);
            SetPrivateField(bsc, "p2DeckCountText", p2DeckCnt);
            SetPrivateField(bsc, "phaseText", phaseText);
            SetPrivateField(bsc, "turnText", turnText);
            SetPrivateField(bsc, "nextPhaseButton", nextPhaseGO.GetComponent<Button>());
            SetPrivateField(bsc, "endTurnButton", endTurnGO.GetComponent<Button>());
            SetPrivateField(bsc, "cardPrefab", cardPrefab);
            SetPrivateField(bsc, "daemonFieldPrefab", cardPrefab);
            SetPrivateField(bsc, "pillarPrefab", cardPrefab);
            SetPrivateField(bsc, "logContainer", logContent.transform);
            SetPrivateField(bsc, "logEntryPrefab", logPrefab);
            SetPrivateField(bsc, "logScrollRect", logScroll);
            SetPrivateField(bsc, "gameOverPanel", goPanel);
            SetPrivateField(bsc, "gameOverText", goTMP);

            // Music hook
            var btMusic = canvasGO.AddComponent<MusicSceneHook>();
            SetPrivateField(btMusic, "musicMode", MusicManager.MusicMode.Battle);

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Battle.unity");
            Debug.Log("[ProjectSetup] Created Battle scene (full-field layout)");
        }

        static void CreateCollectionScene()
        {
            if (AssetExists("Assets/Scenes/Collection.unity")) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera
            var camGO = new GameObject("Main Camera");
            var cam = camGO.AddComponent<Camera>();
            cam.backgroundColor = BgDark;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.orthographic = true;
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";

            CreateEventSystem();

            var canvasGO = CreateCanvas("CollectionCanvas");
            var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CardPrefab.prefab");
            var cardDb = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/Resources/CardData/CardDatabase.asset");

            // ─── Background ───
            CreatePanel(canvasGO, "Background", new Color(0.025f, 0.025f, 0.05f), true);

            // ─── Top banner (header + back button) ───
            var headerBar = CreateUIChild(canvasGO, "HeaderBar", Vector2.zero, Vector2.zero);
            var hbRT = headerBar.GetComponent<RectTransform>();
            hbRT.anchorMin = new Vector2(0, 0.92f);
            hbRT.anchorMax = Vector2.one;
            hbRT.offsetMin = Vector2.zero;
            hbRT.offsetMax = Vector2.zero;
            var hbImg = headerBar.AddComponent<Image>();
            hbImg.color = new Color(0.04f, 0.04f, 0.07f, 0.95f);

            var backBtn = CreateMenuButton(headerBar, "BackButton", "< BACK", new Color(0.6f, 0.6f, 0.6f));
            var backRT = backBtn.GetComponent<RectTransform>();
            backRT.anchorMin = new Vector2(0, 0);
            backRT.anchorMax = new Vector2(0.08f, 1);
            backRT.offsetMin = new Vector2(8, 4);
            backRT.offsetMax = new Vector2(0, -4);
            backBtn.GetComponent<Button>().onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

            var headerGO = CreateUIChild(headerBar, "Header", Vector2.zero, Vector2.zero);
            var headerGORT = headerGO.GetComponent<RectTransform>();
            headerGORT.anchorMin = new Vector2(0.08f, 0);
            headerGORT.anchorMax = Vector2.one;
            headerGORT.offsetMin = Vector2.zero;
            headerGORT.offsetMax = Vector2.zero;
            var headerText = headerGO.AddComponent<TextMeshProUGUI>();
            headerText.text = "GRIMOIRE";
            headerText.fontSize = 32;
            headerText.fontStyle = FontStyles.Bold;
            headerText.alignment = TextAlignmentOptions.Center;
            headerText.color = Gold;
            headerText.characterSpacing = 6f;

            // ─── Filter bar ───
            var filterBar = CreateUIChild(canvasGO, "FilterBar", Vector2.zero, Vector2.zero);
            var fbRT = filterBar.GetComponent<RectTransform>();
            fbRT.anchorMin = new Vector2(0, 0.86f);
            fbRT.anchorMax = new Vector2(1, 0.92f);
            fbRT.offsetMin = new Vector2(8, 2);
            fbRT.offsetMax = new Vector2(-8, -2);
            var fbImg = filterBar.AddComponent<Image>();
            fbImg.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);
            var filterHLG = filterBar.AddComponent<HorizontalLayoutGroup>();
            filterHLG.spacing = 8;
            filterHLG.childAlignment = TextAnchor.MiddleCenter;
            filterHLG.childForceExpandWidth = false;
            filterHLG.childForceExpandHeight = true;
            filterHLG.padding = new RectOffset(12, 12, 4, 4);

            // Search input
            var searchGO = CreateUIChild(filterBar, "SearchInput", Vector2.zero, new Vector2(220, 32));
            var searchBg = searchGO.AddComponent<Image>();
            searchBg.color = new Color(0.08f, 0.08f, 0.10f);
            var searchInput = searchGO.AddComponent<TMP_InputField>();
            var searchTextGO = CreateUIChild(searchGO, "Text", Vector2.zero, new Vector2(210, 28));
            var searchText = searchTextGO.AddComponent<TextMeshProUGUI>();
            searchText.fontSize = 13;
            searchText.color = Cream;
            searchInput.textComponent = searchText;
            var searchPlaceholder = CreateUIChild(searchGO, "Placeholder", Vector2.zero, new Vector2(210, 28));
            var phText = searchPlaceholder.AddComponent<TextMeshProUGUI>();
            phText.text = "Search cards...";
            phText.fontSize = 13;
            phText.color = new Color(0.4f, 0.4f, 0.45f);
            phText.fontStyle = FontStyles.Italic;
            searchInput.placeholder = phText;
            searchGO.AddComponent<LayoutElement>().preferredWidth = 220;

            // Dropdowns
            var catDropGO = CreateDropdown(filterBar, "CategoryDropdown", "All Types", 140);
            var elemDropGO = CreateDropdown(filterBar, "ElementDropdown", "All Elements", 140);
            var rarDropGO = CreateDropdown(filterBar, "RarityDropdown", "All Rarities", 130);
            var sortDropGO = CreateDropdown(filterBar, "SortDropdown", "Sort By", 110);

            // Result count
            var resultGO = CreateUIChild(filterBar, "ResultCountText", Vector2.zero, new Vector2(90, 24));
            var resultText = resultGO.AddComponent<TextMeshProUGUI>();
            resultText.text = "117 cards";
            resultText.fontSize = 12;
            resultText.alignment = TextAlignmentOptions.Center;
            resultText.color = new Color(0.5f, 0.5f, 0.55f);
            resultGO.AddComponent<LayoutElement>().preferredWidth = 90;

            // ─── Card grid scroll area ───
            var scrollGO = CreateUIChild(canvasGO, "CardGridScroll", Vector2.zero, Vector2.zero);
            var scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.01f, 0.08f); // above bottom nav bar
            scrollRT.anchorMax = new Vector2(0.99f, 0.855f);
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;
            scrollGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.vertical = true;
            scrollRect.horizontal = false;

            var gridContent = CreateUIChild(scrollGO, "CardGridContainer", Vector2.zero, new Vector2(920, 0));
            var gridRT = gridContent.GetComponent<RectTransform>();
            gridRT.anchorMin = new Vector2(0, 1);
            gridRT.anchorMax = Vector2.one;
            gridRT.pivot = new Vector2(0.5f, 1);
            var gridLayout = gridContent.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(200, 280);
            gridLayout.spacing = new Vector2(10, 10);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 5;
            gridLayout.childAlignment = TextAnchor.UpperCenter;
            gridLayout.padding = new RectOffset(8, 8, 8, 8);
            var gridCSF = gridContent.AddComponent<ContentSizeFitter>();
            gridCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = gridRT;

            // ─── Card detail panel (modal overlay) ───
            var detailPanel = CreatePanel(canvasGO, "CardDetailPanel", new Color(0, 0, 0, 0.92f), true);

            // Detail backdrop
            var detailBackdrop = CreateUIChild(detailPanel, "DetailBackdrop", Vector2.zero, new Vector2(680, 420));
            var dbImg = detailBackdrop.AddComponent<Image>();
            dbImg.color = new Color(0.05f, 0.05f, 0.08f, 0.98f);

            var detailCard = CreateUIChild(detailBackdrop, "DetailCardVisual", new Vector2(-180, 0), new Vector2(240, 340));
            var dcBg = detailCard.AddComponent<Image>();
            dcBg.color = new Color(0.1f, 0.1f, 0.12f);

            var detailNameGO = CreateUIChild(detailBackdrop, "DetailNameText", new Vector2(120, 140), new Vector2(300, 40));
            var detailName = detailNameGO.AddComponent<TextMeshProUGUI>();
            detailName.fontSize = 26;
            detailName.fontStyle = FontStyles.Bold;
            detailName.color = Gold;

            var detailTypeGO = CreateUIChild(detailBackdrop, "DetailTypeText", new Vector2(120, 100), new Vector2(300, 28));
            var detailType = detailTypeGO.AddComponent<TextMeshProUGUI>();
            detailType.fontSize = 14;
            detailType.color = Cream;

            // Gold accent line
            var detailAccent = CreateUIChild(detailBackdrop, "DetailAccent", new Vector2(120, 82), new Vector2(260, 2));
            var daImg = detailAccent.AddComponent<Image>();
            daImg.color = new Color(Gold.r, Gold.g, Gold.b, 0.4f);

            var detailDescGO = CreateUIChild(detailBackdrop, "DetailDescText", new Vector2(120, 20), new Vector2(300, 100));
            var detailDesc = detailDescGO.AddComponent<TextMeshProUGUI>();
            detailDesc.fontSize = 13;
            detailDesc.color = Cream;
            detailDesc.enableWordWrapping = true;

            var detailFlavorGO = CreateUIChild(detailBackdrop, "DetailFlavorText", new Vector2(120, -55), new Vector2(300, 36));
            var detailFlavor = detailFlavorGO.AddComponent<TextMeshProUGUI>();
            detailFlavor.fontSize = 11;
            detailFlavor.fontStyle = FontStyles.Italic;
            detailFlavor.color = new Color(0.5f, 0.5f, 0.55f);

            var detailStatsGO = CreateUIChild(detailBackdrop, "DetailStatsText", new Vector2(120, -90), new Vector2(300, 28));
            var detailStats = detailStatsGO.AddComponent<TextMeshProUGUI>();
            detailStats.fontSize = 15;
            detailStats.color = Cream;

            var closeBtn = CreateMenuButton(detailBackdrop, "DetailCloseButton", "CLOSE", new Color(0.6f, 0.6f, 0.6f));
            var closeBtnRT = closeBtn.GetComponent<RectTransform>();
            closeBtnRT.anchoredPosition = new Vector2(120, -140);
            closeBtnRT.sizeDelta = new Vector2(120, 40);

            detailPanel.SetActive(false);

            // Wire CollectionScreen
            var cs = canvasGO.AddComponent<CollectionScreen>();
            SetPrivateField(cs, "cardDatabase", cardDb);
            SetPrivateField(cs, "cardGridContainer", gridContent.transform);
            SetPrivateField(cs, "cardThumbnailPrefab", cardPrefab);
            SetPrivateField(cs, "searchInput", searchInput);
            SetPrivateField(cs, "categoryDropdown", catDropGO.GetComponent<TMP_Dropdown>());
            SetPrivateField(cs, "elementDropdown", elemDropGO.GetComponent<TMP_Dropdown>());
            SetPrivateField(cs, "rarityDropdown", rarDropGO.GetComponent<TMP_Dropdown>());
            SetPrivateField(cs, "sortDropdown", sortDropGO.GetComponent<TMP_Dropdown>());
            SetPrivateField(cs, "cardDetailPanel", detailPanel);
            SetPrivateField(cs, "detailNameText", detailName);
            SetPrivateField(cs, "detailTypeText", detailType);
            SetPrivateField(cs, "detailDescText", detailDesc);
            SetPrivateField(cs, "detailFlavorText", detailFlavor);
            SetPrivateField(cs, "detailStatsText", detailStats);
            SetPrivateField(cs, "detailCloseButton", closeBtn.GetComponent<Button>());
            SetPrivateField(cs, "resultCountText", resultText);

            // Bottom nav bar
            CreateBottomNavBar(canvasGO);

            // Music hook
            var colMusic = canvasGO.AddComponent<MusicSceneHook>();
            SetPrivateField(colMusic, "musicMode", MusicManager.MusicMode.Collection);

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Collection.unity");
            Debug.Log("[ProjectSetup] Created Collection scene (PTCG-inspired)");
        }

        static void CreateDeckBuilderScene()
        {
            if (AssetExists("Assets/Scenes/DeckBuilder.unity")) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("Main Camera");
            var cam = camGO.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.025f, 0.025f, 0.05f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.orthographic = true;
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";

            CreateEventSystem();

            var canvasGO = CreateCanvas("DeckBuilderCanvas");
            var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CardPrefab.prefab");
            var cardDb = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/Resources/CardData/CardDatabase.asset");

            CreatePanel(canvasGO, "Background", new Color(0.025f, 0.025f, 0.05f), true);

            // ─── Top bar (header + back) ───
            var headerBar = CreateUIChild(canvasGO, "HeaderBar", Vector2.zero, Vector2.zero);
            var hbRT = headerBar.GetComponent<RectTransform>();
            hbRT.anchorMin = new Vector2(0, 0.92f);
            hbRT.anchorMax = Vector2.one;
            hbRT.offsetMin = Vector2.zero;
            hbRT.offsetMax = Vector2.zero;
            var hbImg = headerBar.AddComponent<Image>();
            hbImg.color = new Color(0.04f, 0.04f, 0.07f, 0.95f);

            var backBtn = CreateMenuButton(headerBar, "BackButton", "< BACK", new Color(0.6f, 0.6f, 0.6f));
            var backRT = backBtn.GetComponent<RectTransform>();
            backRT.anchorMin = new Vector2(0, 0);
            backRT.anchorMax = new Vector2(0.08f, 1);
            backRT.offsetMin = new Vector2(8, 4);
            backRT.offsetMax = new Vector2(0, -4);
            backBtn.GetComponent<Button>().onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

            var headerGO = CreateUIChild(headerBar, "Header", Vector2.zero, Vector2.zero);
            var headerGORT = headerGO.GetComponent<RectTransform>();
            headerGORT.anchorMin = new Vector2(0.08f, 0);
            headerGORT.anchorMax = Vector2.one;
            headerGORT.offsetMin = Vector2.zero;
            headerGORT.offsetMax = Vector2.zero;
            var headerText = headerGO.AddComponent<TextMeshProUGUI>();
            headerText.text = "DECK BUILDER";
            headerText.fontSize = 32;
            headerText.fontStyle = FontStyles.Bold;
            headerText.alignment = TextAlignmentOptions.Center;
            headerText.color = Gold;
            headerText.characterSpacing = 6f;

            // ─── Left side — Card Pool ───
            var poolPanel = CreatePanel(canvasGO, "CardPoolPanel", new Color(0.04f, 0.04f, 0.06f, 0.92f), false);
            var poolRT = poolPanel.GetComponent<RectTransform>();
            poolRT.anchorMin = new Vector2(0.01f, 0.08f);
            poolRT.anchorMax = new Vector2(0.62f, 0.91f);
            poolRT.offsetMin = Vector2.zero;
            poolRT.offsetMax = Vector2.zero;

            // Pool filter row
            var poolFilterRow = CreateUIChild(poolPanel, "PoolFilterRow", Vector2.zero, Vector2.zero);
            var pfRT = poolFilterRow.GetComponent<RectTransform>();
            pfRT.anchorMin = new Vector2(0, 0.94f);
            pfRT.anchorMax = Vector2.one;
            pfRT.offsetMin = new Vector2(6, 0);
            pfRT.offsetMax = new Vector2(-6, -4);
            var pfBg = poolFilterRow.AddComponent<Image>();
            pfBg.color = new Color(0.05f, 0.05f, 0.08f, 0.8f);
            var pfHLG = poolFilterRow.AddComponent<HorizontalLayoutGroup>();
            pfHLG.spacing = 8;
            pfHLG.childAlignment = TextAnchor.MiddleCenter;
            pfHLG.childForceExpandWidth = false;
            pfHLG.childForceExpandHeight = true;
            pfHLG.padding = new RectOffset(8, 8, 2, 2);

            var elemFilterGO = CreateDropdown(poolFilterRow, "ElementFilterDropdown", "Flame", 140);

            var poolLabel = CreateUIChild(poolFilterRow, "PoolLabel", Vector2.zero, new Vector2(140, 24));
            var poolLabelTMP = poolLabel.AddComponent<TextMeshProUGUI>();
            poolLabelTMP.text = "AVAILABLE CARDS";
            poolLabelTMP.fontSize = 12;
            poolLabelTMP.fontStyle = FontStyles.Bold;
            poolLabelTMP.alignment = TextAlignmentOptions.Center;
            poolLabelTMP.color = Cream;
            poolLabel.AddComponent<LayoutElement>().preferredWidth = 140;

            // Pool scroll
            var poolScrollArea = CreateUIChild(poolPanel, "PoolScrollArea", Vector2.zero, Vector2.zero);
            var psaRT = poolScrollArea.GetComponent<RectTransform>();
            psaRT.anchorMin = Vector2.zero;
            psaRT.anchorMax = new Vector2(1, 0.93f);
            psaRT.offsetMin = new Vector2(4, 4);
            psaRT.offsetMax = new Vector2(-4, 0);
            poolScrollArea.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var poolScroll = poolScrollArea.AddComponent<ScrollRect>();
            poolScroll.vertical = true;
            poolScroll.horizontal = false;

            var poolContent = CreateUIChild(poolScrollArea, "CardPoolContainer", Vector2.zero, Vector2.zero);
            var poolContentRT = poolContent.GetComponent<RectTransform>();
            poolContentRT.anchorMin = new Vector2(0, 1);
            poolContentRT.anchorMax = Vector2.one;
            poolContentRT.pivot = new Vector2(0.5f, 1);
            var poolGrid = poolContent.AddComponent<GridLayoutGroup>();
            poolGrid.cellSize = new Vector2(200, 280);
            poolGrid.spacing = new Vector2(8, 8);
            poolGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            poolGrid.constraintCount = 3;
            poolGrid.childAlignment = TextAnchor.UpperCenter;
            poolGrid.padding = new RectOffset(4, 4, 4, 4);
            poolContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            poolScroll.content = poolContentRT;

            // ─── Right side — Deck Panel ───
            var deckPanel = CreatePanel(canvasGO, "DeckPanel", new Color(0.04f, 0.04f, 0.06f, 0.92f), false);
            var deckPanelRT = deckPanel.GetComponent<RectTransform>();
            deckPanelRT.anchorMin = new Vector2(0.63f, 0.08f);
            deckPanelRT.anchorMax = new Vector2(0.99f, 0.91f);
            deckPanelRT.offsetMin = Vector2.zero;
            deckPanelRT.offsetMax = Vector2.zero;

            // Deck config row
            var deckConfigRow = CreateUIChild(deckPanel, "DeckConfigRow", Vector2.zero, Vector2.zero);
            var dcRT = deckConfigRow.GetComponent<RectTransform>();
            dcRT.anchorMin = new Vector2(0, 0.92f);
            dcRT.anchorMax = Vector2.one;
            dcRT.offsetMin = new Vector2(6, 0);
            dcRT.offsetMax = new Vector2(-6, -4);
            var dcConfigBg = deckConfigRow.AddComponent<Image>();
            dcConfigBg.color = new Color(0.05f, 0.05f, 0.08f, 0.8f);
            var dcHLG = deckConfigRow.AddComponent<HorizontalLayoutGroup>();
            dcHLG.spacing = 8;
            dcHLG.childAlignment = TextAnchor.MiddleCenter;
            dcHLG.childForceExpandWidth = false;
            dcHLG.childForceExpandHeight = true;
            dcHLG.padding = new RectOffset(8, 8, 2, 2);

            // Deck name input
            var deckNameGO = CreateUIChild(deckConfigRow, "DeckNameInput", Vector2.zero, new Vector2(160, 30));
            var deckNameBg = deckNameGO.AddComponent<Image>();
            deckNameBg.color = new Color(0.08f, 0.08f, 0.10f);
            var deckNameInput = deckNameGO.AddComponent<TMP_InputField>();
            var deckNameTextGO = CreateUIChild(deckNameGO, "Text", Vector2.zero, new Vector2(150, 26));
            var deckNameText = deckNameTextGO.AddComponent<TextMeshProUGUI>();
            deckNameText.fontSize = 13;
            deckNameText.color = Cream;
            deckNameInput.textComponent = deckNameText;
            deckNameGO.AddComponent<LayoutElement>().preferredWidth = 160;

            var deckElemGO = CreateDropdown(deckConfigRow, "DeckElementDropdown", "Flame", 120);

            // Deck list scroll
            var deckScrollArea = CreateUIChild(deckPanel, "DeckScrollArea", Vector2.zero, Vector2.zero);
            var dsaRT = deckScrollArea.GetComponent<RectTransform>();
            dsaRT.anchorMin = Vector2.zero;
            dsaRT.anchorMax = new Vector2(1, 0.91f);
            dsaRT.offsetMin = new Vector2(4, 80);
            dsaRT.offsetMax = new Vector2(-4, 0);
            deckScrollArea.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var deckListSR = deckScrollArea.AddComponent<ScrollRect>();
            deckListSR.vertical = true;
            deckListSR.horizontal = false;

            var deckListContent = CreateUIChild(deckScrollArea, "DeckListContainer", Vector2.zero, new Vector2(270, 0));
            var dlcRT = deckListContent.GetComponent<RectTransform>();
            dlcRT.anchorMin = new Vector2(0, 1);
            dlcRT.anchorMax = Vector2.one;
            dlcRT.pivot = new Vector2(0.5f, 1);
            var dlcVLG = deckListContent.AddComponent<VerticalLayoutGroup>();
            dlcVLG.spacing = 3;
            dlcVLG.childForceExpandWidth = true;
            dlcVLG.childForceExpandHeight = false;
            dlcVLG.padding = new RectOffset(4, 4, 4, 4);
            deckListContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            deckListSR.content = dlcRT;

            // Pillar list
            var pillarListContent = CreateUIChild(deckPanel, "PillarListContainer", Vector2.zero, Vector2.zero);
            var plRT = pillarListContent.GetComponent<RectTransform>();
            plRT.anchorMin = new Vector2(0.02f, 0.07f);
            plRT.anchorMax = new Vector2(0.98f, 0.14f);
            plRT.offsetMin = Vector2.zero;
            plRT.offsetMax = Vector2.zero;
            var plVLG = pillarListContent.AddComponent<VerticalLayoutGroup>();
            plVLG.spacing = 3;

            // Stats + buttons row at bottom
            var bottomRow = CreateUIChild(deckPanel, "BottomRow", Vector2.zero, Vector2.zero);
            var brRT = bottomRow.GetComponent<RectTransform>();
            brRT.anchorMin = Vector2.zero;
            brRT.anchorMax = new Vector2(1, 0.06f);
            brRT.offsetMin = new Vector2(6, 4);
            brRT.offsetMax = new Vector2(-6, 0);
            var brBg = bottomRow.AddComponent<Image>();
            brBg.color = new Color(0.05f, 0.05f, 0.08f, 0.8f);
            var brHLG = bottomRow.AddComponent<HorizontalLayoutGroup>();
            brHLG.spacing = 8;
            brHLG.childAlignment = TextAnchor.MiddleCenter;
            brHLG.childForceExpandWidth = false;
            brHLG.childForceExpandHeight = true;
            brHLG.padding = new RectOffset(8, 8, 2, 2);

            var cardCountGO = CreateUIChild(bottomRow, "CardCountText", Vector2.zero, new Vector2(90, 24));
            var cardCountText = cardCountGO.AddComponent<TextMeshProUGUI>();
            cardCountText.text = "Cards: 0/40";
            cardCountText.fontSize = 13;
            cardCountText.color = Cream;
            cardCountGO.AddComponent<LayoutElement>().preferredWidth = 90;

            var pillarCountGO = CreateUIChild(bottomRow, "PillarCountText", Vector2.zero, new Vector2(80, 24));
            var pillarCountText = pillarCountGO.AddComponent<TextMeshProUGUI>();
            pillarCountText.text = "Pillars: 0/4";
            pillarCountText.fontSize = 13;
            pillarCountText.color = Cream;
            pillarCountGO.AddComponent<LayoutElement>().preferredWidth = 80;

            var saveBtn = CreateMenuButton(bottomRow, "SaveButton", "SAVE DECK", Gold);
            saveBtn.GetComponent<LayoutElement>().preferredWidth = 110;
            saveBtn.GetComponent<LayoutElement>().preferredHeight = 34;
            var clearBtn = CreateMenuButton(bottomRow, "ClearButton", "CLEAR", new Color(1f, 0.4f, 0.4f));
            clearBtn.GetComponent<LayoutElement>().preferredWidth = 80;
            clearBtn.GetComponent<LayoutElement>().preferredHeight = 34;

            // Wire DeckBuilderScreen
            var dbs = canvasGO.AddComponent<DeckBuilderScreen>();
            SetPrivateField(dbs, "cardDatabase", cardDb);
            SetPrivateField(dbs, "cardPoolContainer", poolContent.transform);
            SetPrivateField(dbs, "cardThumbnailPrefab", cardPrefab);
            SetPrivateField(dbs, "elementFilterDropdown", elemFilterGO.GetComponent<TMP_Dropdown>());
            SetPrivateField(dbs, "deckListContainer", deckListContent.transform);
            SetPrivateField(dbs, "pillarListContainer", pillarListContent.transform);
            SetPrivateField(dbs, "deckNameInput", deckNameInput);
            SetPrivateField(dbs, "deckElementDropdown", deckElemGO.GetComponent<TMP_Dropdown>());
            SetPrivateField(dbs, "cardCountText", cardCountText);
            SetPrivateField(dbs, "pillarCountText", pillarCountText);
            SetPrivateField(dbs, "saveButton", saveBtn.GetComponent<Button>());
            SetPrivateField(dbs, "clearButton", clearBtn.GetComponent<Button>());

            // Bottom nav bar
            CreateBottomNavBar(canvasGO);

            // Music hook
            var dbMusic = canvasGO.AddComponent<MusicSceneHook>();
            SetPrivateField(dbMusic, "musicMode", MusicManager.MusicMode.Collection);

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/DeckBuilder.unity");
            Debug.Log("[ProjectSetup] Created DeckBuilder scene (PTCG-inspired)");
        }

        // ═══════════════════════════════════════════════════
        //  PACK OPENING SCENE
        // ═══════════════════════════════════════════════════

        static void CreatePackOpeningScene()
        {
            if (AssetExists("Assets/Scenes/PackOpening.unity")) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("Main Camera");
            var cam = camGO.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.orthographic = true;
            cam.orthographicSize = 5;
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";

            CreateEventSystem();

            var canvasGO = CreateCanvas("PackOpeningCanvas");
            var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CardPrefab.prefab");
            var cardDb = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/Resources/CardData/CardDatabase.asset");

            // ─── Background ───
            CreatePanel(canvasGO, "Background", new Color(0.02f, 0.02f, 0.05f), true);

            // Top gradient accent
            var topGrad = CreateUIChild(canvasGO, "TopGradient", Vector2.zero, Vector2.zero);
            var tgRT = topGrad.GetComponent<RectTransform>();
            tgRT.anchorMin = new Vector2(0, 0.85f);
            tgRT.anchorMax = Vector2.one;
            tgRT.offsetMin = Vector2.zero;
            tgRT.offsetMax = Vector2.zero;
            var tgImg = topGrad.AddComponent<Image>();
            tgImg.color = new Color(Gold.r * 0.15f, Gold.g * 0.1f, 0.2f, 0.4f);

            // ═══ PACK SELECT PANEL ═══════════════════════
            var packSelectPanel = CreatePanel(canvasGO, "PackSelectPanel", new Color(0, 0, 0, 0), true);

            // Title
            var psTitleGO = CreateUIChild(packSelectPanel, "Title", Vector2.zero, Vector2.zero);
            var psTitleRT = psTitleGO.GetComponent<RectTransform>();
            psTitleRT.anchorMin = new Vector2(0.2f, 0.88f);
            psTitleRT.anchorMax = new Vector2(0.8f, 0.96f);
            psTitleRT.offsetMin = Vector2.zero;
            psTitleRT.offsetMax = Vector2.zero;
            var psTitleTMP = psTitleGO.AddComponent<TextMeshProUGUI>();
            psTitleTMP.text = "BOOSTER PACKS";
            psTitleTMP.fontSize = 36;
            psTitleTMP.fontStyle = FontStyles.Bold;
            psTitleTMP.alignment = TextAlignmentOptions.Center;
            psTitleTMP.color = Gold;
            psTitleTMP.characterSpacing = 6f;

            // Center pack display area (large pack image)
            var packDisplay = CreateUIChild(packSelectPanel, "PackDisplay", Vector2.zero, Vector2.zero);
            var pdRT = packDisplay.GetComponent<RectTransform>();
            pdRT.anchorMin = new Vector2(0.25f, 0.30f);
            pdRT.anchorMax = new Vector2(0.75f, 0.85f);
            pdRT.offsetMin = Vector2.zero;
            pdRT.offsetMax = Vector2.zero;

            // Pack glow behind
            var packGlowArea = CreateUIChild(packDisplay, "PackGlow", Vector2.zero, Vector2.zero);
            var pgRT = packGlowArea.GetComponent<RectTransform>();
            pgRT.anchorMin = new Vector2(-0.1f, -0.1f);
            pgRT.anchorMax = new Vector2(1.1f, 1.1f);
            pgRT.offsetMin = Vector2.zero;
            pgRT.offsetMax = Vector2.zero;
            var pgImg = packGlowArea.AddComponent<Image>();
            pgImg.color = new Color(Gold.r, Gold.g, Gold.b, 0.08f);

            // Side packs (left / right, smaller, faded)
            var leftPack = CreateUIChild(packDisplay, "LeftPack", Vector2.zero, Vector2.zero);
            var lpRT = leftPack.GetComponent<RectTransform>();
            lpRT.anchorMin = new Vector2(-0.5f, 0.1f);
            lpRT.anchorMax = new Vector2(-0.05f, 0.9f);
            lpRT.offsetMin = Vector2.zero;
            lpRT.offsetMax = Vector2.zero;
            var lpImg = leftPack.AddComponent<Image>();
            lpImg.color = new Color(0.15f, 0.12f, 0.25f, 0.5f);
            leftPack.AddComponent<Button>().onClick.AddListener(() => { }); // handled by controller

            var rightPack = CreateUIChild(packDisplay, "RightPack", Vector2.zero, Vector2.zero);
            var rpRT = rightPack.GetComponent<RectTransform>();
            rpRT.anchorMin = new Vector2(1.05f, 0.1f);
            rpRT.anchorMax = new Vector2(1.5f, 0.9f);
            rpRT.offsetMin = Vector2.zero;
            rpRT.offsetMax = Vector2.zero;
            var rpImg = rightPack.AddComponent<Image>();
            rpImg.color = new Color(0.15f, 0.12f, 0.25f, 0.5f);
            rightPack.AddComponent<Button>().onClick.AddListener(() => { });

            // Center pack (main, full color like PTCG ceremony)
            var centerPack = CreateUIChild(packDisplay, "CenterPack", Vector2.zero, Vector2.zero);
            var cpRT = centerPack.GetComponent<RectTransform>();
            cpRT.anchorMin = new Vector2(0.1f, 0.05f);
            cpRT.anchorMax = new Vector2(0.9f, 0.95f);
            cpRT.offsetMin = Vector2.zero;
            cpRT.offsetMax = Vector2.zero;
            var cpImg = centerPack.AddComponent<Image>();
            cpImg.color = new Color(0.4f, 0.2f, 0.6f, 1f); // default pack color

            // Pack name label
            var packNameGO = CreateUIChild(packSelectPanel, "PackNameText", Vector2.zero, Vector2.zero);
            var pnRT = packNameGO.GetComponent<RectTransform>();
            pnRT.anchorMin = new Vector2(0.2f, 0.22f);
            pnRT.anchorMax = new Vector2(0.8f, 0.30f);
            pnRT.offsetMin = Vector2.zero;
            pnRT.offsetMax = Vector2.zero;
            var packNameTMP = packNameGO.AddComponent<TextMeshProUGUI>();
            packNameTMP.text = "Shadow Veil";
            packNameTMP.fontSize = 24;
            packNameTMP.fontStyle = FontStyles.Bold;
            packNameTMP.alignment = TextAlignmentOptions.Center;
            packNameTMP.color = Cream;
            packNameTMP.characterSpacing = 3f;

            // Pack info
            var packCountGO = CreateUIChild(packSelectPanel, "PackCountText", Vector2.zero, Vector2.zero);
            var pcRT = packCountGO.GetComponent<RectTransform>();
            pcRT.anchorMin = new Vector2(0.2f, 0.17f);
            pcRT.anchorMax = new Vector2(0.8f, 0.22f);
            pcRT.offsetMin = Vector2.zero;
            pcRT.offsetMax = Vector2.zero;
            var packCountTMP = packCountGO.AddComponent<TextMeshProUGUI>();
            packCountTMP.text = "5 Cards · Dark Theme";
            packCountTMP.fontSize = 14;
            packCountTMP.alignment = TextAlignmentOptions.Center;
            packCountTMP.color = new Color(0.5f, 0.5f, 0.55f);

            // Timer text (above open button)
            var timerGO = CreateUIChild(packSelectPanel, "TimerText", Vector2.zero, Vector2.zero);
            var timerRT = timerGO.GetComponent<RectTransform>();
            timerRT.anchorMin = new Vector2(0.15f, 0.15f);
            timerRT.anchorMax = new Vector2(0.85f, 0.20f);
            timerRT.offsetMin = Vector2.zero;
            timerRT.offsetMax = Vector2.zero;
            var timerTMP = timerGO.AddComponent<TextMeshProUGUI>();
            timerTMP.text = "READY TO OPEN!";
            timerTMP.fontSize = 16;
            timerTMP.fontStyle = FontStyles.Bold;
            timerTMP.alignment = TextAlignmentOptions.Center;
            timerTMP.color = new Color(0.3f, 0.9f, 0.3f);

            // Open button (PTCG Pocket-style rounded button at bottom)
            var openBtn = CreateStyledButton(packSelectPanel, "OpenButton", "OPEN", Gold, true);
            var openBtnRT = openBtn.GetComponent<RectTransform>();
            openBtnRT.anchorMin = new Vector2(0.3f, 0.08f);
            openBtnRT.anchorMax = new Vector2(0.7f, 0.15f);
            openBtnRT.offsetMin = Vector2.zero;
            openBtnRT.offsetMax = Vector2.zero;

            // Back button
            var backBtn = CreateMenuButton(packSelectPanel, "BackButton", "< BACK", new Color(0.6f, 0.6f, 0.6f));
            var bbRT = backBtn.GetComponent<RectTransform>();
            bbRT.anchorMin = new Vector2(0.02f, 0.92f);
            bbRT.anchorMax = new Vector2(0.12f, 0.98f);
            bbRT.offsetMin = Vector2.zero;
            bbRT.offsetMax = Vector2.zero;

            // ═══ CEREMONY PANEL ═════════════════════════
            var ceremonyPanel = CreatePanel(canvasGO, "CeremonyPanel", new Color(0.01f, 0.01f, 0.04f, 0.98f), true);

            var ceremPackGlow = CreateUIChild(ceremonyPanel, "PackGlow", Vector2.zero, new Vector2(500, 500));
            var cpgImg = ceremPackGlow.AddComponent<Image>();
            cpgImg.color = new Color(1f, 1f, 1f, 0.05f);
            ceremPackGlow.SetActive(false);

            var ceremPackImg = CreateUIChild(ceremonyPanel, "PackImage", Vector2.zero, new Vector2(250, 360));
            var cpiImg = ceremPackImg.AddComponent<Image>();
            cpiImg.color = new Color(0.4f, 0.2f, 0.6f);

            ceremonyPanel.SetActive(false);

            // ═══ RESULTS PANEL ══════════════════════════
            var resultsPanel = CreatePanel(canvasGO, "ResultsPanel", new Color(0.02f, 0.02f, 0.05f, 0.98f), true);

            // Rainbow progress bar (like PTCG Pocket opening results)
            var progressBar = CreateUIChild(resultsPanel, "ProgressBar", Vector2.zero, Vector2.zero);
            var pbRT = progressBar.GetComponent<RectTransform>();
            pbRT.anchorMin = new Vector2(0, 0.92f);
            pbRT.anchorMax = new Vector2(1, 0.925f);
            pbRT.offsetMin = Vector2.zero;
            pbRT.offsetMax = Vector2.zero;
            var pbImg = progressBar.AddComponent<Image>();
            pbImg.color = Gold;

            var resultsTitleGO = CreateUIChild(resultsPanel, "ResultsTitle", Vector2.zero, Vector2.zero);
            var rtRT = resultsTitleGO.GetComponent<RectTransform>();
            rtRT.anchorMin = new Vector2(0.2f, 0.93f);
            rtRT.anchorMax = new Vector2(0.8f, 0.99f);
            rtRT.offsetMin = Vector2.zero;
            rtRT.offsetMax = Vector2.zero;
            var resultsTitleTMP = resultsTitleGO.AddComponent<TextMeshProUGUI>();
            resultsTitleTMP.text = "Opening Results";
            resultsTitleTMP.fontSize = 32;
            resultsTitleTMP.fontStyle = FontStyles.Bold;
            resultsTitleTMP.alignment = TextAlignmentOptions.Center;
            resultsTitleTMP.color = Cream;

            // Card results grid (3 + 2 layout like PTCG Pocket)
            var resultsGrid = CreateUIChild(resultsPanel, "ResultsGrid", Vector2.zero, Vector2.zero);
            var rgRT = resultsGrid.GetComponent<RectTransform>();
            rgRT.anchorMin = new Vector2(0.05f, 0.20f);
            rgRT.anchorMax = new Vector2(0.95f, 0.90f);
            rgRT.offsetMin = Vector2.zero;
            rgRT.offsetMax = Vector2.zero;
            var rgGrid = resultsGrid.AddComponent<GridLayoutGroup>();
            rgGrid.cellSize = new Vector2(200, 280);
            rgGrid.spacing = new Vector2(20, 20);
            rgGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            rgGrid.constraintCount = 3;
            rgGrid.childAlignment = TextAnchor.UpperCenter;
            rgGrid.padding = new RectOffset(30, 30, 20, 20);

            // Next button
            var nextBtn = CreateStyledButton(resultsPanel, "NextButton", "NEXT", Gold, true);
            var nbRT = nextBtn.GetComponent<RectTransform>();
            nbRT.anchorMin = new Vector2(0.3f, 0.04f);
            nbRT.anchorMax = new Vector2(0.7f, 0.12f);
            nbRT.offsetMin = Vector2.zero;
            nbRT.offsetMax = Vector2.zero;

            resultsPanel.SetActive(false);

            // ─── Bottom nav bar ───
            CreateBottomNavBar(canvasGO);

            // ─── Wire BoosterPackController ───
            var bpc = canvasGO.AddComponent<BoosterPackController>();
            SetPrivateField(bpc, "cardDatabase", cardDb);
            SetPrivateField(bpc, "packSelectPanel", packSelectPanel);
            SetPrivateField(bpc, "packNameText", packNameTMP);
            SetPrivateField(bpc, "packCountText", packCountTMP);
            SetPrivateField(bpc, "openButton", openBtn.GetComponent<Button>());
            SetPrivateField(bpc, "backButton", backBtn.GetComponent<Button>());
            SetPrivateField(bpc, "ceremonyPanel", ceremonyPanel);
            SetPrivateField(bpc, "packImage", cpiImg);
            SetPrivateField(bpc, "packGlow", cpgImg);
            SetPrivateField(bpc, "resultsPanel", resultsPanel);
            SetPrivateField(bpc, "resultsTitle", resultsTitleTMP);
            SetPrivateField(bpc, "resultsGrid", resultsGrid.transform);
            SetPrivateField(bpc, "cardPrefab", cardPrefab);
            SetPrivateField(bpc, "nextButton", nextBtn.GetComponent<Button>());
            SetPrivateField(bpc, "timerText", timerTMP);

            // Music hook
            var poMusic = canvasGO.AddComponent<MusicSceneHook>();
            SetPrivateField(poMusic, "musicMode", MusicManager.MusicMode.Collection);

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/PackOpening.unity");
            Debug.Log("[ProjectSetup] Created PackOpening scene (PTCG Pocket ceremony)");
        }

        // ═══════════════════════════════════════════════════
        //  BUILD SETTINGS
        // ═══════════════════════════════════════════════════

        static void ConfigureBuildSettings()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Battle.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Collection.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/DeckBuilder.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/PackOpening.unity", true),
            };
            EditorBuildSettings.scenes = scenes;
        }

        // ═══════════════════════════════════════════════════
        //  CARD CREATION HELPERS (duplicated from CardImporter for batch mode)
        // ═══════════════════════════════════════════════════

        static DaemonCardData CreateDaemon(CardJsonEntry e)
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

        static PillarCardData CreatePillar(CardJsonEntry e)
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

        static DomainCardData CreateDomain(CardJsonEntry e)
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

        static MaskCardData CreateMask(CardJsonEntry e)
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

        static SealCardData CreateSeal(CardJsonEntry e)
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

        static DispelCardData CreateDispel(CardJsonEntry e)
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

        static ConjurorCardData CreateConjuror(CardJsonEntry e)
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

        static DeckData CreateDeck(DeckJsonEntry e, string cardPath)
        {
            var d = ScriptableObject.CreateInstance<DeckData>();
            d.deckName = e.name;
            d.element = ParseElement(e.element);
            d.description = e.description ?? "";
            d.isStarter = e.isStarter;
            if (e.cards != null)
            {
                var counts = new Dictionary<string, int>();
                foreach (var id in e.cards)
                    counts[id] = counts.ContainsKey(id) ? counts[id] + 1 : 1;
                d.cards = counts.Select(kv =>
                {
                    var card = AssetDatabase.LoadAssetAtPath<CardData>($"{cardPath}/{kv.Key}.asset");
                    return new DeckEntry { card = card, count = kv.Value };
                }).ToArray();
            }
            if (e.pillars != null)
            {
                var pCounts = new Dictionary<string, int>();
                foreach (var id in e.pillars)
                    pCounts[id] = pCounts.ContainsKey(id) ? pCounts[id] + 1 : 1;
                d.pillars = pCounts.Select(kv =>
                {
                    var card = AssetDatabase.LoadAssetAtPath<CardData>($"{cardPath}/{kv.Key}.asset");
                    return new DeckEntry { card = card, count = kv.Value };
                }).ToArray();
            }
            return d;
        }

        static void SetBaseFields(CardData card, CardJsonEntry e)
        {
            card.cardId = e.id;
            card.cardName = e.name;
            card.category = ParseCategory(e.category);
            card.rarity = ParseRarity(e.rarity);
            card.description = e.description ?? "";
            card.flavorText = e.flavorText ?? "";
        }

        // ═══════════════════════════════════════════════════
        //  UI HELPERS
        // ═══════════════════════════════════════════════════

        static GameObject CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingLayerName = "UI";
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        static void CreateEventSystem()
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        static GameObject CreatePanel(GameObject parent, string name, Color color, bool fullStretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            if (fullStretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        static GameObject CreateUIChild(GameObject parent, string name, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return go;
        }

        static GameObject CreateMenuButton(GameObject parent, string name, string label, Color textColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(260, 48);

            var img = go.AddComponent<Image>();
            img.color = ButtonBg;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            // Button colors
            var colors = btn.colors;
            colors.normalColor = ButtonBg;
            colors.highlightedColor = new Color(0.18f, 0.18f, 0.18f);
            colors.pressedColor = new Color(0.08f, 0.08f, 0.08f);
            btn.colors = colors;

            var textGO = CreateUIChild(go, "Text", Vector2.zero, rt.sizeDelta);
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 16;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = textColor;

            // Layout element for vertical layouts
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = 48;

            return go;
        }

        /// <summary>PTCG-inspired styled button with accent border and hover colors.</summary>
        static GameObject CreateStyledButton(GameObject parent, string name, string label, Color textColor, bool isPrimary)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(280, isPrimary ? 52 : 44);

            // Border/accent glow behind button
            var borderGO = CreateUIChild(go, "Border", Vector2.zero, rt.sizeDelta + new Vector2(2, 2));
            var borderImg = borderGO.AddComponent<Image>();
            borderImg.color = isPrimary ? new Color(Gold.r, Gold.g, Gold.b, 0.35f) : new Color(0.2f, 0.2f, 0.22f, 0.5f);

            // Background
            var bgColor = isPrimary
                ? new Color(0.08f, 0.07f, 0.04f)
                : new Color(0.08f, 0.08f, 0.10f);

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = isPrimary
                ? new Color(0.14f, 0.12f, 0.06f)
                : new Color(0.14f, 0.14f, 0.16f);
            colors.pressedColor = new Color(0.04f, 0.04f, 0.04f);
            btn.colors = colors;

            var textGO = CreateUIChild(go, "Text", Vector2.zero, rt.sizeDelta);
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = isPrimary ? 17 : 15;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = textColor;
            text.characterSpacing = isPrimary ? 4f : 2f;

            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = isPrimary ? 52 : 44;

            return go;
        }

        /// <summary>PTCG Pocket-style bottom navigation bar with 5 tab icons.</summary>
        static void CreateBottomNavBar(GameObject canvas)
        {
            var navBar = CreateUIChild(canvas, "BottomNavBar", Vector2.zero, Vector2.zero);
            var nbRT = navBar.GetComponent<RectTransform>();
            nbRT.anchorMin = new Vector2(0, 0);
            nbRT.anchorMax = new Vector2(1, 0.07f);
            nbRT.offsetMin = Vector2.zero;
            nbRT.offsetMax = Vector2.zero;
            var nbBg = navBar.AddComponent<Image>();
            nbBg.color = new Color(0.04f, 0.04f, 0.06f, 0.97f);

            // Gold accent line at top of nav bar
            var navAccent = CreateUIChild(navBar, "NavAccent", Vector2.zero, Vector2.zero);
            var naRT = navAccent.GetComponent<RectTransform>();
            naRT.anchorMin = new Vector2(0, 0.92f);
            naRT.anchorMax = Vector2.one;
            naRT.offsetMin = Vector2.zero;
            naRT.offsetMax = Vector2.zero;
            var naImg = navAccent.AddComponent<Image>();
            naImg.color = new Color(Gold.r, Gold.g, Gold.b, 0.25f);

            var navHLG = navBar.AddComponent<HorizontalLayoutGroup>();
            navHLG.spacing = 0;
            navHLG.childAlignment = TextAnchor.MiddleCenter;
            navHLG.childForceExpandWidth = true;
            navHLG.childForceExpandHeight = true;
            navHLG.padding = new RectOffset(20, 20, 2, 4);

            string[] tabNames = { "Home", "Packs", "Battle", "Grimoire", "Decks" };
            string[] tabScenes = { "MainMenu", "PackOpening", "Battle", "Collection", "DeckBuilder" };
            string[] tabIcons = { "⌂", "◆", "⚔", "📖", "≡" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = CreateUIChild(navBar, $"Tab_{tabNames[i]}", Vector2.zero, new Vector2(0, 0));
                tab.AddComponent<LayoutElement>().flexibleWidth = 1;

                var tabVLG = tab.AddComponent<VerticalLayoutGroup>();
                tabVLG.spacing = 1;
                tabVLG.childAlignment = TextAnchor.MiddleCenter;
                tabVLG.childForceExpandWidth = true;
                tabVLG.childForceExpandHeight = false;

                var iconGO = CreateUIChild(tab, "Icon", Vector2.zero, new Vector2(24, 24));
                var iconTMP = iconGO.AddComponent<TextMeshProUGUI>();
                iconTMP.text = tabIcons[i];
                iconTMP.fontSize = 20;
                iconTMP.alignment = TextAlignmentOptions.Center;
                iconTMP.color = i == 0 ? Gold : new Color(0.45f, 0.45f, 0.5f);
                iconGO.AddComponent<LayoutElement>().preferredHeight = 22;

                var labelGO = CreateUIChild(tab, "Label", Vector2.zero, new Vector2(60, 14));
                var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
                labelTMP.text = tabNames[i];
                labelTMP.fontSize = 9;
                labelTMP.alignment = TextAlignmentOptions.Center;
                labelTMP.color = i == 0 ? Gold : new Color(0.4f, 0.4f, 0.45f);
                labelGO.AddComponent<LayoutElement>().preferredHeight = 14;

                // Tab button
                var tabImg = tab.AddComponent<Image>();
                tabImg.color = new Color(0, 0, 0, 0);
                var tabBtn = tab.AddComponent<Button>();
                tabBtn.targetGraphic = tabImg;
                string sceneName = tabScenes[i];
                tabBtn.onClick.AddListener(() => SceneManager.LoadScene(sceneName));
            }
        }

        /// <summary>PTCG Pocket-inspired feature card (rounded container with icon + label).</summary>
        static GameObject CreateFeatureCard(GameObject parent, string name, string title, string subtitle, Color accentColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 100);

            // Card background
            var img = go.AddComponent<Image>();
            img.color = new Color(accentColor.r * 0.3f + 0.05f, accentColor.g * 0.3f + 0.05f, accentColor.b * 0.3f + 0.05f, 0.92f);

            // Accent strip at top
            var strip = CreateUIChild(go, "AccentStrip", Vector2.zero, Vector2.zero);
            var stripRT = strip.GetComponent<RectTransform>();
            stripRT.anchorMin = new Vector2(0, 0.88f);
            stripRT.anchorMax = Vector2.one;
            stripRT.offsetMin = Vector2.zero;
            stripRT.offsetMax = Vector2.zero;
            var stripImg = strip.AddComponent<Image>();
            stripImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.5f);

            // Title text
            var titleGO = CreateUIChild(go, "Title", new Vector2(0, 8), new Vector2(180, 32));
            var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
            titleTMP.text = title;
            titleTMP.fontSize = 18;
            titleTMP.fontStyle = FontStyles.Bold;
            titleTMP.alignment = TextAlignmentOptions.Center;
            titleTMP.color = Cream;
            titleTMP.characterSpacing = 3f;

            // Subtitle
            if (!string.IsNullOrEmpty(subtitle))
            {
                var subGO = CreateUIChild(go, "Subtitle", new Vector2(0, -14), new Vector2(180, 20));
                var subTMP = subGO.AddComponent<TextMeshProUGUI>();
                subTMP.text = subtitle;
                subTMP.fontSize = 11;
                subTMP.alignment = TextAlignmentOptions.Center;
                subTMP.color = new Color(0.5f, 0.5f, 0.55f);
            }

            // Button for navigation
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(accentColor.r * 0.4f + 0.08f, accentColor.g * 0.4f + 0.08f, accentColor.b * 0.4f + 0.08f, 0.95f);
            colors.pressedColor = new Color(0.06f, 0.06f, 0.06f);
            btn.colors = colors;

            go.AddComponent<LayoutElement>().flexibleWidth = 1;

            return go;
        }

        static GameObject CreateHorizontalContainer(GameObject parent, string name, Vector2 pos, Vector2 size)
        {
            var go = CreateUIChild(parent, name, pos, size);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            return go;
        }

        static GameObject CreateDropdown(GameObject parent, string name, string defaultText, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 36);

            var img = go.AddComponent<Image>();
            img.color = ButtonBg;

            var dropdown = go.AddComponent<TMP_Dropdown>();

            // Caption text
            var captionGO = CreateUIChild(go, "Label", Vector2.zero, new Vector2(width - 20, 30));
            var captionText = captionGO.AddComponent<TextMeshProUGUI>();
            captionText.text = defaultText;
            captionText.fontSize = 14;
            captionText.color = Cream;
            captionText.alignment = TextAlignmentOptions.Left;
            dropdown.captionText = captionText;

            // Template (minimal — Unity handles the rest)
            var templateGO = CreateUIChild(go, "Template", new Vector2(0, -36), new Vector2(width, 100));
            var templateRT = templateGO.GetComponent<RectTransform>();
            templateRT.pivot = new Vector2(0.5f, 1f);
            var templateImg = templateGO.AddComponent<Image>();
            templateImg.color = new Color(0.15f, 0.15f, 0.15f);
            var templateScroll = templateGO.AddComponent<ScrollRect>();

            var viewportGO = CreateUIChild(templateGO, "Viewport", Vector2.zero, Vector2.zero);
            var viewportRT = viewportGO.GetComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportGO.AddComponent<Image>().color = Color.white;
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;
            templateScroll.viewport = viewportRT;

            var contentGO = CreateUIChild(viewportGO, "Content", Vector2.zero, Vector2.zero);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = Vector2.one;
            contentRT.pivot = new Vector2(0.5f, 1);
            templateScroll.content = contentRT;

            var itemGO = CreateUIChild(contentGO, "Item", Vector2.zero, new Vector2(0, 30));
            var itemToggle = itemGO.AddComponent<Toggle>();

            var itemLabelGO = CreateUIChild(itemGO, "Item Label", Vector2.zero, new Vector2(width - 10, 28));
            var itemLabel = itemLabelGO.AddComponent<TextMeshProUGUI>();
            itemLabel.fontSize = 14;
            itemLabel.color = Cream;
            dropdown.itemText = itemLabel;

            templateGO.SetActive(false);
            dropdown.template = templateRT;

            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 36;

            return go;
        }

        static void SetupSlider(Slider slider, Color fillColor)
        {
            var go = slider.gameObject;
            var bgGO = CreateUIChild(go, "Background", Vector2.zero, Vector2.zero);
            var bgRT = bgGO.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            bgGO.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);

            var fillArea = CreateUIChild(go, "Fill Area", Vector2.zero, Vector2.zero);
            var fillAreaRT = fillArea.GetComponent<RectTransform>();
            fillAreaRT.anchorMin = Vector2.zero;
            fillAreaRT.anchorMax = Vector2.one;
            fillAreaRT.offsetMin = Vector2.zero;
            fillAreaRT.offsetMax = Vector2.zero;

            var fill = CreateUIChild(fillArea, "Fill", Vector2.zero, Vector2.zero);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(1, 1);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = fillColor;

            slider.fillRect = fillRT;
            slider.maxValue = GameConstants.ConjurorMaxHp;
            slider.value = GameConstants.ConjurorMaxHp;
        }

        // ═══════════════════════════════════════════════════
        //  REFLECTION HELPER
        // ═══════════════════════════════════════════════════

        static void SetPrivateField(object obj, string fieldName, object value)
        {
            var type = obj.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    return;
                }
                type = type.BaseType;
            }
            Debug.LogWarning($"[ProjectSetup] Field '{fieldName}' not found on {obj.GetType().Name}");
        }

        // ═══════════════════════════════════════════════════
        //  ENUM PARSERS
        // ═══════════════════════════════════════════════════

        static Element ParseElement(string s)
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

        static CreatureType ParseCreatureType(string s)
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

        static CardCategory ParseCategory(string s) => s?.ToLower() switch
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

        static Rarity ParseRarity(string s) => s?.ToLower() switch
        {
            "common" => Rarity.Common,
            "rare" => Rarity.Rare,
            "epic" => Rarity.Epic,
            "legendary" => Rarity.Legendary,
            _ => Rarity.Common,
        };

        static AbilityType ParseAbilityType(string s) => s?.ToLower() switch
        {
            "passive" => AbilityType.Passive,
            "on-summon" or "onsummon" => AbilityType.OnSummon,
            "on-destroy" or "ondestroy" => AbilityType.OnDestroy,
            _ => AbilityType.Passive,
        };

        static SealTrigger ParseSealTrigger(string s) => s?.ToLower() switch
        {
            "on-attack" or "onattack" => SealTrigger.OnAttack,
            "on-summon" or "onsummon" => SealTrigger.OnSummon,
            "on-daemon-destroy" or "ondaemondestroy" => SealTrigger.OnDaemonDestroy,
            "on-spell" or "onspell" => SealTrigger.OnSpell,
            _ => SealTrigger.OnAttack,
        };

        static DispelTarget ParseDispelTarget(string s) => s?.ToLower() switch
        {
            "domain" => DispelTarget.Domain,
            "mask" => DispelTarget.Mask,
            "seal" => DispelTarget.Seal,
            "any" => DispelTarget.Any,
            _ => DispelTarget.Any,
        };

        static DomainEffectType ParseDomainEffectType(string s) => s?.ToLower() switch
        {
            "atk-buff-all" or "atkbuffall" => DomainEffectType.AtkBuffAll,
            "damage-all-end" or "damageallend" => DomainEffectType.DamageAllEnd,
            "protection" => DomainEffectType.Protection,
            "element-atk-buff" or "elementatkbuff" => DomainEffectType.ElementAtkBuff,
            "extra-draw" or "extradraw" => DomainEffectType.ExtraDraw,
            "pillar-restore" or "pillarrestore" => DomainEffectType.PillarRestore,
            "pillar-heal" or "pillarheal" => DomainEffectType.PillarHeal,
            _ => DomainEffectType.AtkBuffAll,
        };

        static MaskEffectType ParseMaskEffectType(string s) => s?.ToLower() switch
        {
            "atk-boost" or "atkboost" => MaskEffectType.AtkBoost,
            "ashe-boost" or "asheboost" => MaskEffectType.AsheBoost,
            "haste" => MaskEffectType.Haste,
            "stealth" => MaskEffectType.Stealth,
            "thorns" => MaskEffectType.Thorns,
            "entangle" => MaskEffectType.Entangle,
            _ => MaskEffectType.AtkBoost,
        };

        static SealEffectType ParseSealEffectType(string s) => s?.ToLower() switch
        {
            "drain" => SealEffectType.Drain,
            "destroy" => SealEffectType.Destroy,
            "negate" => SealEffectType.Negate,
            "counter-spell" or "counterspell" => SealEffectType.CounterSpell,
            "heal-conjuror" or "healconjuror" => SealEffectType.HealConjuror,
            _ => SealEffectType.Drain,
        };

        // ═══════════════════════════════════════════════════
        //  UTILITY
        // ═══════════════════════════════════════════════════

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static bool AssetExists(string path)
        {
            return File.Exists(Path.Combine(Directory.GetCurrentDirectory(), path));
        }
    }
}
#endif
