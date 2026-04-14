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

            int w = 256, h = 192;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color primary = ElementSystem.GetElementColor(element);
            Color secondary = GetSecondaryColor(element);
            Color accent = GetCategoryAccent(category);

            // Stable hash from cardId for per-card variation
            int hash = StableHash(cardId);
            float variation = (hash % 1000) / 1000f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / w;
                    float ny = (float)y / h;

                    // Base gradient (diagonal sweep)
                    float gradT = Mathf.Clamp01((nx + ny) * 0.5f + variation * 0.2f);
                    Color pixel = Color.Lerp(primary * 0.6f, secondary * 0.8f, gradT);

                    // Add radial vignette from center
                    float cx = nx - 0.5f, cy = ny - 0.5f;
                    float dist = Mathf.Sqrt(cx * cx + cy * cy) * 1.4f;
                    pixel = Color.Lerp(pixel, pixel * 0.3f, Mathf.Clamp01(dist));

                    // Add element-specific pattern
                    float pattern = GetElementPattern(element, nx, ny, variation);
                    pixel = Color.Lerp(pixel, accent, pattern * 0.35f);

                    // Category-specific overlay
                    float catOverlay = GetCategoryOverlay(category, nx, ny, variation);
                    pixel = Color.Lerp(pixel, Color.white * 0.9f, catOverlay * 0.15f);

                    // Rarity shimmer
                    if (rarity >= Rarity.Epic)
                    {
                        float shimmer = Mathf.Sin((nx * 8f + ny * 6f + variation * 12f) * Mathf.PI) * 0.5f + 0.5f;
                        shimmer *= shimmer;
                        Color rarityCol = ElementSystem.GetRarityColor(rarity);
                        pixel = Color.Lerp(pixel, rarityCol, shimmer * (rarity == Rarity.Legendary ? 0.25f : 0.15f));
                    }

                    // Subtle noise for texture feel
                    float noise = PseudoNoise(x + hash, y + hash) * 0.08f;
                    pixel += new Color(noise, noise, noise, 0f);

                    pixel.a = 1f;
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
            Color col = Color.Lerp(ElementSystem.GetElementColor(element), ElementSystem.GetRarityColor(rarity), 0.3f);
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
