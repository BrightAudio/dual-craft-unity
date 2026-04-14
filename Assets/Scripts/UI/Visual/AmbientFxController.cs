// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Ambient FX Controller
//  Manages background particles, wisps, vignette, and
//  board ambience color shifting.  Lightweight runtime.
// ═══════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DualCraft.UI.Visual
{
    public class AmbientFxController : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────
        [Header("Particle Systems (optional — assign if present)")]
        [SerializeField] private ParticleSystem backgroundParticles;
        [SerializeField] private ParticleSystem embers;
        [SerializeField] private ParticleSystem magicWisps;
        [SerializeField] private ParticleSystem dustMotes;
        [SerializeField] private ParticleSystem sparkles;
        [SerializeField] private ParticleSystem edgeGlow;

        [Header("Overlay Images")]
        [SerializeField] private Image vignetteOverlay;
        [SerializeField] private Image boardTintOverlay;
        [SerializeField] private Image ambientLightOverlay;

        [Header("Intensity Presets")]
        [SerializeField] private float calmEmissionRate      = 5f;
        [SerializeField] private float battleEmissionRate    = 15f;
        [SerializeField] private float tensionEmissionRate   = 30f;
        [SerializeField] private float victoryEmissionRate   = 50f;

        [SerializeField] private float calmVignette          = 0.15f;
        [SerializeField] private float battleVignette        = 0.25f;
        [SerializeField] private float tensionVignette       = 0.40f;
        [SerializeField] private float victoryVignette       = 0.10f;

        [Header("Colors")]
        [SerializeField] private Color calmTint     = new Color(0.08f, 0.06f, 0.14f, 0.30f);
        [SerializeField] private Color battleTint   = new Color(0.12f, 0.06f, 0.06f, 0.35f);
        [SerializeField] private Color tensionTint  = new Color(0.20f, 0.05f, 0.05f, 0.45f);
        [SerializeField] private Color victoryTint  = new Color(0.10f, 0.08f, 0.02f, 0.30f);

        // ── State ────────────────────────────────────────────
        private AmbienceIntensity _current = AmbienceIntensity.Calm;
        private Coroutine _transitionCo;
        private Coroutine _lightFlickerCo;
        private DualCraftVisualTheme Theme => DualCraftVisualTheme.I;

        private void Start()
        {
            // Start ambient light flicker
            if (ambientLightOverlay != null)
                _lightFlickerCo = StartCoroutine(AmbientLightFlicker());
        }

        // ══════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════

        /// <summary>Transition to a new intensity preset over time.</summary>
        public void SetIntensity(AmbienceIntensity intensity, float transitionTime = 1f)
        {
            if (intensity == _current) return;
            _current = intensity;
            if (_transitionCo != null) StopCoroutine(_transitionCo);
            _transitionCo = StartCoroutine(TransitionTo(intensity, transitionTime));
        }

        /// <summary>Set a custom board tint (e.g., when a domain is active).</summary>
        public void SetDomainTint(Color tint, float duration = 1f)
        {
            if (boardTintOverlay != null)
                StartCoroutine(LerpOverlayColor(boardTintOverlay, tint, duration));
        }

        /// <summary>Clear domain tint back to current ambient preset.</summary>
        public void ClearDomainTint(float duration = 1f)
        {
            SetDomainTint(GetTint(_current), duration);
        }

        // ══════════════════════════════════════════════════════
        //  TRANSITION LOGIC
        // ══════════════════════════════════════════════════════

        private IEnumerator TransitionTo(AmbienceIntensity intensity, float dur)
        {
            float targetRate = GetEmissionRate(intensity);
            float targetVig  = GetVignetteAlpha(intensity);
            Color targetTint = GetTint(intensity);

            // Particles — main systems
            SetParticleRate(backgroundParticles, targetRate);
            SetParticleRate(embers, targetRate * 0.4f);
            SetParticleRate(magicWisps, targetRate * 0.6f);
            SetParticleRate(dustMotes, targetRate * 0.3f);
            // Extra particle layers
            SetParticleRate(sparkles, targetRate * 0.2f);
            SetParticleRate(edgeGlow, intensity == AmbienceIntensity.HighTension ? targetRate * 0.5f : 0f);

            // Victory gets a burst
            if (intensity == AmbienceIntensity.Victory)
            {
                if (sparkles != null) sparkles.Emit(40);
                if (embers != null) embers.Emit(20);
            }

            // Vignette + tint lerp
            float t = 0f;
            float startVig = vignetteOverlay != null ? vignetteOverlay.color.a : 0f;
            Color startTint = boardTintOverlay != null ? boardTintOverlay.color : Color.clear;

            while (t < dur)
            {
                t += Time.deltaTime;
                float p = UIAnimUtils.EaseInOutCubic(Mathf.Clamp01(t / dur));

                if (vignetteOverlay != null)
                {
                    Color c = vignetteOverlay.color;
                    c.a = Mathf.Lerp(startVig, targetVig, p);
                    vignetteOverlay.color = c;
                }
                if (boardTintOverlay != null)
                    boardTintOverlay.color = Color.Lerp(startTint, targetTint, p);

                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  PRESET LOOKUPS
        // ══════════════════════════════════════════════════════

        private float GetEmissionRate(AmbienceIntensity i) => i switch
        {
            AmbienceIntensity.Calm        => calmEmissionRate,
            AmbienceIntensity.Battle      => battleEmissionRate,
            AmbienceIntensity.HighTension => tensionEmissionRate,
            AmbienceIntensity.Victory     => victoryEmissionRate,
            _ => calmEmissionRate,
        };

        private float GetVignetteAlpha(AmbienceIntensity i) => i switch
        {
            AmbienceIntensity.Calm        => calmVignette,
            AmbienceIntensity.Battle      => battleVignette,
            AmbienceIntensity.HighTension => tensionVignette,
            AmbienceIntensity.Victory     => victoryVignette,
            _ => calmVignette,
        };

        private Color GetTint(AmbienceIntensity i) => i switch
        {
            AmbienceIntensity.Calm        => calmTint,
            AmbienceIntensity.Battle      => battleTint,
            AmbienceIntensity.HighTension => tensionTint,
            AmbienceIntensity.Victory     => victoryTint,
            _ => calmTint,
        };

        // ══════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════

        private static void SetParticleRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.rateOverTime = rate;
        }

        private IEnumerator LerpOverlayColor(Image img, Color target, float dur)
        {
            Color start = img.color;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                img.color = Color.Lerp(start, target, Mathf.Clamp01(t / dur));
                yield return null;
            }
            img.color = target;
        }

        // ══════════════════════════════════════════════════════
        //  RUNTIME PARTICLE CREATION  (if no prefab assigned)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Creates a simple particle system at runtime for UI overlay use.
        /// Call from an initializer if you don't have prefabs set up yet.
        /// </summary>
        public static ParticleSystem CreateSimpleParticles(Transform parent, Color color, float rate = 10f, float size = 0.05f, float lifetime = 3f)
        {
            var go = new GameObject("AmbientParticles");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startColor = color;
            main.startSize = size;
            main.startLifetime = lifetime;
            main.startSpeed = 0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.loop = true;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(20f, 12f, 1f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.3f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = grad;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = -1;

            return ps;
        }

        /// <summary>Create sparkle particles with trails for magical ambience.</summary>
        public static ParticleSystem CreateSparkleParticles(Transform parent, Color color, float rate = 5f)
        {
            var ps = CreateSimpleParticles(parent, color, rate, 0.03f, 2f);
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);

            var trails = ps.trails;
            trails.enabled = true;
            trails.ratio = 0.4f;
            trails.lifetime = 0.3f;
            trails.minVertexDistance = 0.05f;
            trails.dieWithParticles = true;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            return ps;
        }

        /// <summary>Burst celebration effect (call on victory or rare reveal).</summary>
        public void PlayCelebrationBurst(Color color, int count = 60)
        {
            if (backgroundParticles != null)
            {
                var main = backgroundParticles.main;
                var origColor = main.startColor;
                main.startColor = color;
                backgroundParticles.Emit(count);
                main.startColor = origColor;
            }
            if (sparkles != null) sparkles.Emit(count / 2);
            if (embers != null) embers.Emit(count / 3);
        }

        /// <summary>Set element-themed particle colors across all systems.</summary>
        public void SetElementTheme(DualCraft.Core.Element element)
        {
            Color elColor = Theme != null ? Theme.GetElementColor(element) : Color.white;
            SetParticleColor(magicWisps, elColor);
            SetParticleColor(sparkles, elColor);
            Color emberColor = Color.Lerp(elColor, new Color(1f, 0.6f, 0.2f), 0.5f);
            SetParticleColor(embers, emberColor);
        }

        /// <summary>Clear element theme back to default.</summary>
        public void ClearElementTheme()
        {
            Color defaultWisp = Theme != null ? Theme.accent : new Color(0.42f, 0.36f, 0.91f);
            SetParticleColor(magicWisps, defaultWisp);
            if (sparkles != null)
                SetParticleColor(sparkles, new Color(1f, 0.95f, 0.75f, 0.6f));
            if (embers != null)
                SetParticleColor(embers, new Color(1f, 0.6f, 0.2f, 0.5f));
        }

        private static void SetParticleColor(ParticleSystem ps, Color color)
        {
            if (ps == null) return;
            var main = ps.main;
            main.startColor = color;
        }

        private IEnumerator AmbientLightFlicker()
        {
            if (ambientLightOverlay == null) yield break;
            Color baseColor = Theme != null ? Theme.glowOuterSoft : new Color(0.42f, 0.36f, 0.91f, 0.10f);
            while (true)
            {
                // Gentle flickering ambient light (simulates magical environment)
                float noise = Mathf.PerlinNoise(Time.time * 0.5f, 0f);
                float alpha = Mathf.Lerp(0.02f, 0.08f, noise);
                Color c = baseColor;
                c.a = alpha;
                ambientLightOverlay.color = c;
                yield return null;
            }
        }
    }
}
