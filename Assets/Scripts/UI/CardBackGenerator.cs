// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Back Texture Generator
//  Generates a procedural card back matching the official
//  Duel Craft design: gold/blue mystical portal theme
// ═══════════════════════════════════════════════════════

using UnityEngine;

namespace DualCraft.UI
{
    public static class CardBackGenerator
    {
        private static Texture2D _cached;

        public static Sprite Generate()
        {
            if (_cached != null)
                return Sprite.Create(_cached, new Rect(0, 0, _cached.width, _cached.height), new Vector2(0.5f, 0.5f));

            int w = 256, h = 340;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color darkBg = new Color(0.04f, 0.03f, 0.08f);
            Color gold = new Color(0.784f, 0.663f, 0.416f);
            Color darkGold = new Color(0.45f, 0.38f, 0.22f);
            Color blue = new Color(0.2f, 0.4f, 0.85f);
            Color lightBlue = new Color(0.5f, 0.7f, 1f);

            float cx = 0.5f, cy = 0.5f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / w;
                    float ny = (float)y / h;
                    float dx = nx - cx, dy = ny - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);

                    // Base: dark navy
                    Color pixel = darkBg;

                    // Outer ornate border (gold frame ~8% from edges)
                    float borderDist = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(ny, 1f - ny));
                    if (borderDist < 0.08f)
                    {
                        float t = 1f - borderDist / 0.08f;
                        // Gold border with decorative pattern
                        float pattern = Mathf.Sin(angle * 12f) * 0.3f + 0.7f;
                        pixel = Color.Lerp(pixel, gold * pattern, t * 0.9f);

                        // Corner flourishes
                        float cornerDist = Mathf.Min(nx < 0.5f ? nx : 1f - nx, ny < 0.5f ? ny : 1f - ny);
                        if (cornerDist < 0.12f)
                        {
                            float cornerT = 1f - cornerDist / 0.12f;
                            pixel = Color.Lerp(pixel, darkGold, cornerT * t * 0.5f);
                        }
                    }

                    // Inner ornate ring (second gold ring)
                    if (borderDist > 0.08f && borderDist < 0.12f)
                    {
                        float t = 1f - Mathf.Abs(borderDist - 0.1f) / 0.02f;
                        float ring = Mathf.Sin(angle * 16f + dist * 20f) * 0.4f + 0.6f;
                        pixel = Color.Lerp(pixel, darkGold * ring, Mathf.Max(0, t) * 0.6f);
                    }

                    // Concentric portal rings (center)
                    float ring1 = Mathf.Abs(Mathf.Sin(dist * 18f));
                    float ringMask = Mathf.Max(0, 1f - dist * 3.5f); // Fade out from center
                    if (ringMask > 0)
                    {
                        // Alternating gold and blue rings
                        float ringPhase = Mathf.Sin(dist * 36f);
                        Color ringColor = ringPhase > 0 ? gold : blue;
                        float intensity = ring1 * ringMask;
                        pixel = Color.Lerp(pixel, ringColor * 0.7f, intensity * 0.5f);
                    }

                    // Central mask glow (half gold, half blue)
                    float centerDist = dist * 4f;
                    if (centerDist < 1f)
                    {
                        float centerT = 1f - centerDist;
                        centerT = centerT * centerT; // Quadratic falloff
                        Color maskColor = dx < 0 ? gold : lightBlue;
                        pixel = Color.Lerp(pixel, maskColor, centerT * 0.6f);

                        // Eye-like bright spot in very center
                        if (centerDist < 0.15f)
                        {
                            float eyeT = 1f - centerDist / 0.15f;
                            pixel = Color.Lerp(pixel, Color.white, eyeT * 0.4f);
                        }
                    }

                    // Mystical rune dots around the portal
                    for (int i = 0; i < 12; i++)
                    {
                        float runeAngle = i * Mathf.PI * 2f / 12f;
                        float runeR = 0.28f;
                        float rx = cx + Mathf.Cos(runeAngle) * runeR;
                        float ry = cy + Mathf.Sin(runeAngle) * runeR * (float)w / h;
                        float runeDist = Mathf.Sqrt((nx - rx) * (nx - rx) + (ny - ry) * (ny - ry));
                        if (runeDist < 0.015f)
                        {
                            float runeT = 1f - runeDist / 0.015f;
                            pixel = Color.Lerp(pixel, lightBlue, runeT * 0.8f);
                        }
                    }

                    // Subtle radial light beams
                    float beams = Mathf.Pow(Mathf.Max(0, Mathf.Cos(angle * 8f)), 8f);
                    pixel = Color.Lerp(pixel, gold * 0.3f, beams * ringMask * 0.2f);

                    pixel.a = 1f;
                    tex.SetPixel(x, y, pixel);
                }
            }

            tex.Apply();
            _cached = tex;
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }
    }
}
