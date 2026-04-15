// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Runtime Card Texture Generator
//  Creates element-themed gradient textures for cards
//  that don't have hand-drawn artwork
// ═══════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

namespace DualCraft.UI
{
    using Core;

    public static class CardTextureGenerator
    {
        private static readonly Dictionary<string, Texture2D> _cache = new();

        public static Sprite GenerateCardArt(Element element, CardCategory category, Rarity rarity, string cardId)
        {
            string key = $"{cardId}_{element}_{category}_{rarity}";
            if (_cache.TryGetValue(key, out var cached))
                return Sprite.Create(cached, new Rect(0, 0, cached.width, cached.height), new Vector2(0.5f, 0.5f));

            // Higher resolution for sharper card art
            int w = 352, h = 220;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color primary = ElementColors.GetElementColor(element);
            Color secondary = GetSecondaryColor(element);
            Color accent = GetCategoryAccent(category);

            // Get creature type color if applicable
            CreatureType cType = CreatureType.Elemental;
            Color creatureCol = primary;
            if (category == CardCategory.Daemon)
            {
                int cHash = StableHash(cardId + "_ctype");
                cType = (CreatureType)(cHash % 5);
                creatureCol = ElementColors.GetCreatureTypeColor(cType);
            }

            // Stable hash from cardId for per-card variation
            int hash = StableHash(cardId);
            float variation = (hash % 1000) / 1000f;
            float v2 = ((hash / 1000) % 1000) / 1000f; // second variation channel

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / w;
                    float ny = (float)y / h;

                    // Multi-layered base: gradient + noise
                    float gradT = Mathf.Clamp01((nx * 0.6f + ny * 0.4f) + variation * 0.15f);
                    Color pixel = Color.Lerp(primary * 0.5f, secondary * 0.7f, gradT);

                    // Add depth with a second diagonal pass
                    float grad2 = Mathf.Clamp01(((1f - nx) * 0.4f + ny * 0.6f) + v2 * 0.1f);
                    pixel = Color.Lerp(pixel, accent * 0.4f, grad2 * 0.3f);

                    // Radial vignette with off-center focal point
                    float fcx = 0.5f + (variation - 0.5f) * 0.2f;
                    float fcy = 0.4f + (v2 - 0.5f) * 0.15f;
                    float dx = nx - fcx, dy = ny - fcy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) * 1.3f;
                    pixel = Color.Lerp(pixel, pixel * 0.2f, Mathf.Clamp01(dist * dist));

                    // Bright focal glow
                    float glow = Mathf.Max(0, 1f - dist * 2.5f);
                    glow = glow * glow;
                    pixel = Color.Lerp(pixel, primary * 1.2f + Color.white * 0.2f, glow * 0.4f);

                    // Element-specific pattern layers (two passes for depth)
                    float pattern1 = GetElementPattern(element, nx, ny, variation);
                    float pattern2 = GetElementPattern(element, nx * 1.3f + 0.1f, ny * 1.2f + 0.2f, v2);
                    pixel = Color.Lerp(pixel, accent, pattern1 * 0.3f);
                    pixel = Color.Lerp(pixel, secondary * 1.1f, pattern2 * 0.15f);

                    // Creature type visual signature (daemons only)
                    if (category == CardCategory.Daemon)
                    {
                        float ctPattern = GetCreatureTypePattern(cType, nx, ny, variation);
                        pixel = Color.Lerp(pixel, creatureCol * 0.9f, ctPattern * 0.25f);
                    }

                    // Category-specific overlay
                    float catOverlay = GetCategoryOverlay(category, nx, ny, variation);
                    pixel = Color.Lerp(pixel, Color.white * 0.85f, catOverlay * 0.12f);

                    // Rarity effects
                    if (rarity >= Rarity.Rare)
                    {
                        float shimmer = Mathf.Sin((nx * 10f + ny * 7f + variation * 15f) * Mathf.PI) * 0.5f + 0.5f;
                        shimmer *= shimmer;
                        Color rarityCol = ElementColors.GetRarityColor(rarity);
                        float intensity = rarity switch
                        {
                            Rarity.Rare => 0.1f,
                            Rarity.Epic => 0.18f,
                            Rarity.Legendary => 0.28f,
                            _ => 0f,
                        };
                        pixel = Color.Lerp(pixel, rarityCol, shimmer * intensity);

                        // Legendary: extra star sparkle
                        if (rarity == Rarity.Legendary)
                        {
                            float sparkle = PseudoNoise(x * 3 + hash, y * 3 + hash);
                            if (sparkle > 0.97f)
                                pixel = Color.Lerp(pixel, Color.white, 0.6f);
                        }
                    }

                    // Multi-scale noise for texture feel
                    float n1 = PseudoNoise(x + hash, y + hash) * 0.06f;
                    float n2 = PseudoNoise(x * 2 + hash, y * 2 + hash) * 0.03f;
                    pixel += new Color(n1 + n2, n1 + n2, n1 + n2, 0f);

                    pixel.a = 1f;
                    pixel.r = Mathf.Clamp01(pixel.r);
                    pixel.g = Mathf.Clamp01(pixel.g);
                    pixel.b = Mathf.Clamp01(pixel.b);
                    tex.SetPixel(x, y, pixel);
                }
            }

            tex.Apply();
            _cache[key] = tex;
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        public static Sprite GenerateFrameTexture(Element element, Rarity rarity)
        {
            int w = 8, h = 8;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color col = Color.Lerp(ElementColors.GetElementColor(element), ElementColors.GetRarityColor(rarity), 0.3f);
            col *= 0.8f;
            col.a = 1f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, col);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        // Element-specific visual patterns
        static float GetElementPattern(Element element, float x, float y, float variation)
        {
            return element switch
            {
                // Flame: rising waves
                Element.Flame => Mathf.Max(0, Mathf.Sin((x * 5f + y * 3f + variation * 4f) * Mathf.PI) * 
                                 Mathf.Sin((y * 4f + variation * 6f) * Mathf.PI)),
                // Ice: crystalline facets
                Element.Ice => Mathf.Abs(Mathf.Sin(x * 8f * Mathf.PI) * Mathf.Sin(y * 8f * Mathf.PI)) *
                               Mathf.Max(0, Mathf.Sin((x + y) * 4f * Mathf.PI)),
                // Water: ripples
                Element.Water => Mathf.Max(0, Mathf.Sin(Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 
                                 12f * Mathf.PI + variation * 6f)),
                // Earth: strata layers
                Element.Earth => Mathf.Max(0, Mathf.Sin((y * 6f + Mathf.Sin(x * 3f + variation) * 0.4f) * Mathf.PI)),
                // Air: swirling wisps
                Element.Air => Mathf.Max(0, Mathf.Sin((Mathf.Atan2(y - 0.5f, x - 0.5f) * 3f + 
                               Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 8f) + variation * 4f)),
                // Light: radial burst
                Element.Light => Mathf.Pow(Mathf.Max(0, Mathf.Cos(Mathf.Atan2(y - 0.5f, x - 0.5f) * 6f + variation * 3f)), 4f) *
                                 (1f - Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 1.5f),
                // Dark: shadow tendrils  
                Element.Dark => Mathf.Max(0, Mathf.Sin(x * 4f * Mathf.PI + Mathf.Sin(y * 3f + variation * 5f) * 2f)) *
                                (1f - y) * 0.8f,
                // Nature: organic growth
                Element.Nature => Mathf.Max(0, Mathf.Sin((x * 3f + Mathf.Sin(y * 5f + variation) * 0.5f) * Mathf.PI)) *
                                  Mathf.Max(0, Mathf.Cos(y * 2f * Mathf.PI + variation)),
                _ => 0f,
            };
        }

        // Category overlays
        static float GetCategoryOverlay(CardCategory category, float x, float y, float v)
        {
            return category switch
            {
                // Daemon: diamond pattern in center
                CardCategory.Daemon => Mathf.Max(0, 1f - (Mathf.Abs(x - 0.5f) + Mathf.Abs(y - 0.5f)) * 3f),
                // Pillar: vertical bars
                CardCategory.Pillar => Mathf.Max(0, Mathf.Sin(x * 6f * Mathf.PI)) * Mathf.Max(0, 1f - Mathf.Abs(y - 0.5f) * 2f),
                // Conjuror: concentric rings
                CardCategory.Conjuror => Mathf.Max(0, Mathf.Sin(Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 10f * Mathf.PI)),
                // Domain: horizontal gradient bands
                CardCategory.Domain => Mathf.Max(0, Mathf.Sin(y * 4f * Mathf.PI + v)) * 0.5f,
                // Mask: diagonal cross
                CardCategory.Mask => Mathf.Max(0, 1f - Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x - (1f - y))) * 5f),
                // Seal: circle
                CardCategory.Seal => Mathf.Max(0, Mathf.Sin(Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 6f * Mathf.PI)) * 0.6f,
                // Dispel: X pattern
                CardCategory.Dispel => Mathf.Max(0, 1f - Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x + y - 1f)) * 6f),
                _ => 0f,
            };
        }

        // Creature type visual signature patterns
        static float GetCreatureTypePattern(CreatureType cType, float x, float y, float v)
        {
            return cType switch
            {
                // Elemental: swirling energy rings
                CreatureType.Elemental => Mathf.Max(0, Mathf.Sin(Mathf.Atan2(y - 0.5f, x - 0.5f) * 4f +
                    Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 10f + v * 5f)),
                // Spirit: ethereal wisps rising upward
                CreatureType.Spirit => Mathf.Max(0, Mathf.Sin((x * 6f + Mathf.Sin(y * 4f + v * 3f) * 1.2f) * Mathf.PI))
                    * Mathf.Clamp01(y * 1.5f) * 0.8f,
                // Undead: cracked/shattered ground pattern
                CreatureType.Undead => Mathf.Max(0, 1f - Mathf.Min(
                    Mathf.Abs(Mathf.Sin(x * 7f + v) * Mathf.Cos(y * 5f + v)),
                    Mathf.Abs(Mathf.Cos(x * 5f - v) * Mathf.Sin(y * 7f - v))) * 4f) * (1f - y * 0.5f),
                // Machine: grid circuitry lines
                CreatureType.Machine => Mathf.Max(
                    Mathf.Max(0, 1f - Mathf.Abs(Mathf.Sin(x * 10f * Mathf.PI)) * 5f),
                    Mathf.Max(0, 1f - Mathf.Abs(Mathf.Sin(y * 10f * Mathf.PI)) * 5f)) * 0.4f +
                    Mathf.Max(0, 1f - Mathf.Abs(Mathf.Sin((x + y) * 6f * Mathf.PI + v)) * 8f) * 0.3f,
                // Artificial: hexagonal tessellation
                CreatureType.Artificial => Mathf.Max(0, Mathf.Cos(x * 8f * Mathf.PI) * Mathf.Cos(y * 8f * Mathf.PI) +
                    Mathf.Cos((x * 0.866f + y * 0.5f) * 8f * Mathf.PI) * 0.5f) * 0.6f,
                _ => 0f,
            };
        }

        static Color GetSecondaryColor(Element element) => element switch
        {
            Element.Flame => new Color(0.95f, 0.2f, 0.05f),
            Element.Ice => new Color(0.85f, 0.92f, 1f),
            Element.Water => new Color(0.1f, 0.3f, 0.7f),
            Element.Earth => new Color(0.55f, 0.35f, 0.15f),
            Element.Air => new Color(0.9f, 0.95f, 1f),
            Element.Light => new Color(1f, 1f, 0.85f),
            Element.Dark => new Color(0.15f, 0.05f, 0.25f),
            Element.Nature => new Color(0.05f, 0.45f, 0.15f),
            _ => Color.gray,
        };

        static Color GetCategoryAccent(CardCategory cat) => cat switch
        {
            CardCategory.Daemon => new Color(1f, 0.85f, 0.7f),
            CardCategory.Pillar => new Color(0.7f, 0.85f, 1f),
            CardCategory.Conjuror => new Color(1f, 0.95f, 0.7f),
            CardCategory.Domain => new Color(0.7f, 1f, 0.85f),
            CardCategory.Mask => new Color(0.9f, 0.7f, 1f),
            CardCategory.Seal => new Color(1f, 0.7f, 0.7f),
            CardCategory.Dispel => new Color(0.85f, 0.85f, 0.85f),
            _ => Color.white,
        };

        static float PseudoNoise(int x, int y)
        {
            int n = x + y * 57;
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / (float)0x7fffffff;
        }

        static int StableHash(string s)
        {
            int hash = 17;
            foreach (char c in s)
                hash = hash * 31 + c;
            return Mathf.Abs(hash);
        }
    }
}
