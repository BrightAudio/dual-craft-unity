// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Visual Theme (ScriptableObject)
//  Centralizes all colors, gradients, timing, glow,
//  rarity, element, zone theming for the entire UI.
// ═══════════════════════════════════════════════════════
using UnityEngine;
using DualCraft.Core;

namespace DualCraft.UI.Visual
{
    [CreateAssetMenu(fileName = "DualCraftTheme", menuName = "Dual Craft/Visual Theme")]
    public class DualCraftVisualTheme : ScriptableObject
    {
        // ── Singleton accessor (loaded from Resources/) ──────────
        private static DualCraftVisualTheme _instance;
        public static DualCraftVisualTheme I
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<DualCraftVisualTheme>("DualCraftTheme");
                return _instance;
            }
        }

        // ══════════════════════════════════════════════════════════
        //  CORE PALETTE
        // ══════════════════════════════════════════════════════════
        [Header("Core Palette")]
        public Color bgDark          = new Color(0.043f, 0.043f, 0.043f, 1f);   // #0B0B0B
        public Color bgPanel         = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        public Color gold            = new Color(0.784f, 0.663f, 0.416f, 1f);   // #C8A96A
        public Color cream           = new Color(0.961f, 0.941f, 0.910f, 1f);   // #F5F0E8
        public Color accent          = new Color(0.42f, 0.36f, 0.91f, 1f);      // Mystic violet
        public Color danger          = new Color(0.90f, 0.22f, 0.22f, 1f);
        public Color healing         = new Color(0.25f, 0.85f, 0.45f, 1f);
        public Color manaBlue        = new Color(0.30f, 0.55f, 0.95f, 1f);

        // ══════════════════════════════════════════════════════════
        //  GLOW / BLOOM
        // ══════════════════════════════════════════════════════════
        [Header("Glow")]
        public Color glowDefault     = new Color(0.5f, 0.45f, 0.85f, 0.5f);
        public Color glowHighlight   = new Color(0.85f, 0.75f, 0.35f, 0.7f);
        public Color glowDanger      = new Color(0.95f, 0.15f, 0.15f, 0.6f);
        public Color glowHealing     = new Color(0.2f, 0.9f, 0.4f, 0.5f);
        public Color glowMana        = new Color(0.3f, 0.5f, 1f, 0.55f);

        // ══════════════════════════════════════════════════════════
        //  ZONE COLORS  (board regions)
        // ══════════════════════════════════════════════════════════
        [Header("Zone Colors")]
        public Color zoneHand        = new Color(0.12f, 0.10f, 0.18f, 0.85f);
        public Color zoneDaemon      = new Color(0.14f, 0.08f, 0.08f, 0.80f);
        public Color zonePillar      = new Color(0.10f, 0.10f, 0.06f, 0.80f);
        public Color zoneSeal        = new Color(0.06f, 0.12f, 0.12f, 0.75f);
        public Color zoneDomain      = new Color(0.08f, 0.06f, 0.16f, 0.80f);
        public Color zoneConjuror    = new Color(0.10f, 0.08f, 0.14f, 0.90f);

        [Header("Zone Glow (per-zone tint for frame highlight)")]
        public Color zoneGlowHand    = new Color(0.50f, 0.45f, 0.85f, 0.40f);
        public Color zoneGlowDaemon  = new Color(0.85f, 0.25f, 0.25f, 0.45f);
        public Color zoneGlowPillar  = new Color(0.78f, 0.66f, 0.42f, 0.45f);
        public Color zoneGlowSeal    = new Color(0.22f, 0.72f, 0.55f, 0.40f);
        public Color zoneGlowDomain  = new Color(0.42f, 0.36f, 0.91f, 0.45f);

        // ══════════════════════════════════════════════════════════
        //  ELEMENT COLORS
        // ══════════════════════════════════════════════════════════
        [Header("Element Colors")]
        public Color elFlame   = new Color(0.95f, 0.35f, 0.10f, 1f);
        public Color elIce     = new Color(0.55f, 0.85f, 0.98f, 1f);
        public Color elWater   = new Color(0.15f, 0.45f, 0.95f, 1f);
        public Color elEarth   = new Color(0.55f, 0.40f, 0.22f, 1f);
        public Color elAir     = new Color(0.80f, 0.90f, 0.95f, 1f);
        public Color elLight   = new Color(1.00f, 0.95f, 0.60f, 1f);
        public Color elDark    = new Color(0.35f, 0.15f, 0.55f, 1f);
        public Color elNature  = new Color(0.20f, 0.75f, 0.30f, 1f);

        // ══════════════════════════════════════════════════════════
        //  RARITY COLORS
        // ══════════════════════════════════════════════════════════
        [Header("Rarity Colors")]
        public Color rarCommon    = new Color(0.65f, 0.65f, 0.65f, 1f);
        public Color rarRare      = new Color(0.30f, 0.55f, 0.95f, 1f);
        public Color rarEpic      = new Color(0.65f, 0.30f, 0.90f, 1f);
        public Color rarLegendary = new Color(0.95f, 0.75f, 0.15f, 1f);

        // ══════════════════════════════════════════════════════════
        //  ANIMATION TIMINGS
        // ══════════════════════════════════════════════════════════
        [Header("Animation Timings")]
        public float fadeTime         = 0.25f;
        public float popTime          = 0.15f;
        public float slideTime        = 0.35f;
        public float pulseTime        = 0.6f;
        public float hoverScale       = 1.08f;
        public float clickScale       = 0.92f;
        public float floatTextSpeed   = 120f;
        public float floatTextLife    = 1.2f;
        public float screenShakeMag   = 8f;
        public float screenShakeTime  = 0.25f;

        // ══════════════════════════════════════════════════════════
        //  FONT REFERENCES  (assign in inspector or leave null)
        // ══════════════════════════════════════════════════════════
        [Header("Fonts (optional — null uses default TMP font)")]
        public TMPro.TMP_FontAsset fontTitle;
        public TMPro.TMP_FontAsset fontBody;
        public TMPro.TMP_FontAsset fontNumbers;

        // ══════════════════════════════════════════════════════════
        //  SPRITE REFERENCES  (optional — UI frames / icons)
        // ══════════════════════════════════════════════════════════
        [Header("UI Sprites (optional)")]
        public Sprite panelFrame;
        public Sprite zoneFrame;
        public Sprite buttonBg;
        public Sprite glowRing;
        public Sprite vignetteOverlay;
        public Sprite iconSword;
        public Sprite iconShield;
        public Sprite iconSkull;
        public Sprite iconHeart;
        public Sprite iconMana;

        // ══════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════

        public Color GetElementColor(Element el) => el switch
        {
            Element.Flame  => elFlame,
            Element.Ice    => elIce,
            Element.Water  => elWater,
            Element.Earth  => elEarth,
            Element.Air    => elAir,
            Element.Light  => elLight,
            Element.Dark   => elDark,
            Element.Nature => elNature,
            _              => cream,
        };

        public Color GetRarityColor(Rarity r) => r switch
        {
            Rarity.Common    => rarCommon,
            Rarity.Rare      => rarRare,
            Rarity.Epic      => rarEpic,
            Rarity.Legendary => rarLegendary,
            _                => cream,
        };

        public Color GetZoneColor(ZoneType z) => z switch
        {
            ZoneType.Hand     => zoneHand,
            ZoneType.Daemon   => zoneDaemon,
            ZoneType.Pillar   => zonePillar,
            ZoneType.Seal     => zoneSeal,
            ZoneType.Domain   => zoneDomain,
            ZoneType.Conjuror => zoneConjuror,
            _                 => bgPanel,
        };

        public Color GetZoneGlow(ZoneType z) => z switch
        {
            ZoneType.Hand   => zoneGlowHand,
            ZoneType.Daemon => zoneGlowDaemon,
            ZoneType.Pillar => zoneGlowPillar,
            ZoneType.Seal   => zoneGlowSeal,
            ZoneType.Domain => zoneGlowDomain,
            _               => glowDefault,
        };

        // ══════════════════════════════════════════════════════════
        //  PANEL HIERARCHY (layered depth)
        // ══════════════════════════════════════════════════════════
        [Header("Panel Hierarchy")]
        public Color panelSurface     = new Color(0.10f, 0.09f, 0.15f, 0.94f);
        public Color panelElevated    = new Color(0.13f, 0.11f, 0.20f, 0.96f);
        public Color panelOverlay     = new Color(0.16f, 0.14f, 0.24f, 0.98f);
        public Color panelBorder      = new Color(0.30f, 0.26f, 0.50f, 0.35f);
        public Color panelBorderGold  = new Color(0.78f, 0.66f, 0.42f, 0.30f);
        public Color separator        = new Color(0.40f, 0.35f, 0.65f, 0.18f);

        // ══════════════════════════════════════════════════════════
        //  TEXT HIERARCHY
        // ══════════════════════════════════════════════════════════
        [Header("Text Hierarchy")]
        public Color textPrimary     = new Color(0.96f, 0.94f, 0.91f, 1f);
        public Color textSecondary   = new Color(0.72f, 0.68f, 0.78f, 1f);
        public Color textMuted       = new Color(0.45f, 0.42f, 0.52f, 1f);
        public Color textGold        = new Color(0.90f, 0.78f, 0.52f, 1f);
        public float textShadowAlpha = 0.6f;

        // ══════════════════════════════════════════════════════════
        //  HDR GLOW MULTIPLIERS (for "magical" pop on key elements)
        // ══════════════════════════════════════════════════════════
        [Header("HDR Glow")]
        public float glowIntensity     = 1.4f;
        public float glowPulseMin      = 0.20f;
        public float glowPulseMax      = 0.70f;
        public float glowBreathSpeed   = 1.2f;
        public Color glowInnerRing     = new Color(1f, 0.92f, 0.65f, 0.25f);
        public Color glowOuterSoft     = new Color(0.42f, 0.36f, 0.91f, 0.10f);

        // ══════════════════════════════════════════════════════════
        //  GRADIENT PRESETS (procedural at runtime)
        // ══════════════════════════════════════════════════════════
        [Header("Gradient Presets")]
        public Color gradTopDark     = new Color(0.02f, 0.02f, 0.06f, 1f);
        public Color gradBottomDark  = new Color(0.06f, 0.05f, 0.12f, 1f);
        public Color gradGoldTop     = new Color(0.90f, 0.78f, 0.50f, 1f);
        public Color gradGoldBottom  = new Color(0.60f, 0.45f, 0.18f, 1f);
        public Color gradDangerTop   = new Color(0.95f, 0.30f, 0.20f, 1f);
        public Color gradDangerBot   = new Color(0.55f, 0.08f, 0.08f, 1f);

        // ══════════════════════════════════════════════════════════
        //  COMBAT FEEDBACK TUNING
        // ══════════════════════════════════════════════════════════
        [Header("Combat Feedback")]
        public float critShakeIntensity  = 14f;
        public float critShakeDuration   = 0.3f;
        public float critFlashAlpha      = 0.55f;
        public float hitPauseTime        = 0.04f;
        public float comboTextScale      = 1.8f;
        public Color overkillColor       = new Color(1f, 0.4f, 0.1f, 1f);
        public Color immuneColor         = new Color(0.5f, 0.7f, 1f, 1f);
        public Color blockedColor        = new Color(0.6f, 0.6f, 0.7f, 1f);

        // ══════════════════════════════════════════════════════════
        //  TRANSITION TIMINGS
        // ══════════════════════════════════════════════════════════
        [Header("Transitions")]
        public float sceneTransitionTime  = 0.6f;
        public float menuTransitionTime   = 0.35f;
        public float cardFlipTime         = 0.4f;
        public float phaseRevealTime      = 0.5f;
        public float victoryFanfareDelay  = 0.8f;

        // ══════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════

        /// <summary>Linearly interpolates two theme colors with glow energy.</summary>
        public static Color GlowLerp(Color a, Color b, float t)
        {
            Color c = Color.Lerp(a, b, t);
            c.a = Mathf.Lerp(a.a, b.a, t);
            return c;
        }

        /// <summary>Returns a HDR-boosted version of a color for glow rendering.</summary>
        public Color Intensify(Color c)
        {
            return new Color(c.r * glowIntensity, c.g * glowIntensity, c.b * glowIntensity, c.a);
        }

        /// <summary>Creates a vertical gradient between two theme colors.</summary>
        public static Gradient MakeGradient(Color top, Color bottom)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(top, 0f), new GradientColorKey(bottom, 1f) },
                new[] { new GradientAlphaKey(top.a, 0f), new GradientAlphaKey(bottom.a, 1f) }
            );
            return g;
        }

        /// <summary>Get a panel color by depth level (0=surface, 1=elevated, 2=overlay).</summary>
        public Color GetPanelColor(int depth) => depth switch
        {
            0 => panelSurface,
            1 => panelElevated,
            _ => panelOverlay,
        };
    }

    // ══════════════════════════════════════════════════════════
    //  ZONE TYPE  — used by board and theme helpers
    // ══════════════════════════════════════════════════════════
    public enum ZoneType
    {
        Hand,
        Daemon,
        Pillar,
        Seal,
        Domain,
        Conjuror
    }

    // ══════════════════════════════════════════════════════════
    //  AMBIENCE INTENSITY  — for particle / vignette presets
    // ══════════════════════════════════════════════════════════
    public enum AmbienceIntensity
    {
        Calm,
        Battle,
        HighTension,
        Victory
    }
}
