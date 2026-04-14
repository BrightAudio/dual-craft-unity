// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Music Manager
//  Procedurally generates ambient background music.
//  Persists across scenes via DontDestroyOnLoad.
// ═══════════════════════════════════════════════════════

using UnityEngine;

namespace DualCraft.Audio
{
    public class MusicManager : MonoBehaviour
    {
        public static MusicManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private float volume = 0.25f;

        private AudioSource _menuSource;
        private AudioSource _battleSource;
        private AudioSource _ambientSource;
        private MusicMode _currentMode = MusicMode.None;
        private float _fadeTarget = 1f;
        private float _fadeDuration = 1.5f;
        private float _fadeTimer;

        public enum MusicMode { None, Menu, Battle, Collection, PackOpening }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _menuSource = CreateSource("MenuMusic");
            _battleSource = CreateSource("BattleMusic");
            _ambientSource = CreateSource("AmbientMusic");

            GenerateMenuTrack();
            GenerateBattleTrack();
            GenerateAmbientTrack();
        }

        private AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
            return src;
        }

        public void PlayMode(MusicMode mode)
        {
            if (mode == _currentMode) return;
            _currentMode = mode;

            FadeOut(_menuSource);
            FadeOut(_battleSource);
            FadeOut(_ambientSource);

            switch (mode)
            {
                case MusicMode.Menu:
                    FadeIn(_menuSource);
                    break;
                case MusicMode.Battle:
                    FadeIn(_battleSource);
                    break;
                case MusicMode.Collection:
                case MusicMode.PackOpening:
                    FadeIn(_ambientSource);
                    break;
            }
        }

        private void FadeIn(AudioSource src)
        {
            if (!src.isPlaying) src.Play();
            StartCoroutine(FadeCoroutine(src, volume, _fadeDuration));
        }

        private void FadeOut(AudioSource src)
        {
            StartCoroutine(FadeCoroutine(src, 0f, _fadeDuration));
        }

        private System.Collections.IEnumerator FadeCoroutine(AudioSource src, float targetVol, float duration)
        {
            float start = src.volume;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                src.volume = Mathf.Lerp(start, targetVol, t / duration);
                yield return null;
            }
            src.volume = targetVol;
            if (targetVol <= 0.01f) src.Stop();
        }

        // ─── Procedural Music Generation ─────────────────
        // Uses pentatonic scale tones layered with ambient pads

        private void GenerateMenuTrack()
        {
            // Dark ambient pad with slow pentatonic melody
            int sampleRate = 44100;
            int duration = 30; // 30 second loop
            int samples = sampleRate * duration;
            var clip = AudioClip.Create("MenuMusic", samples, 1, sampleRate, false);
            float[] data = new float[samples];

            // Pentatonic scale (C minor pentatonic): C, Eb, F, G, Bb
            float[] notes = { 130.81f, 155.56f, 174.61f, 196.00f, 233.08f, 261.63f, 311.13f };
            float noteLen = sampleRate * 2f; // 2 second notes
            int noteIndex = 0;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;

                // Deep ambient drone (C2 + G2)
                float drone = Mathf.Sin(2f * Mathf.PI * 65.41f * t) * 0.12f;
                drone += Mathf.Sin(2f * Mathf.PI * 98.0f * t) * 0.08f;

                // Slow melody
                int noteSlot = (int)(i / noteLen);
                float freq = notes[noteSlot % notes.Length];
                float env = 1f - ((i % (int)noteLen) / noteLen); // decay envelope
                env = env * env; // exponential decay
                float melody = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.06f * env;

                // Subtle shimmer (high harmonics)
                float shimmer = Mathf.Sin(2f * Mathf.PI * 523.25f * t + Mathf.Sin(t * 0.3f) * 3f) * 0.015f;

                data[i] = Mathf.Clamp(drone + melody + shimmer, -1f, 1f);
            }

            clip.SetData(data, 0);
            _menuSource.clip = clip;
        }

        private void GenerateBattleTrack()
        {
            int sampleRate = 44100;
            int duration = 20;
            int samples = sampleRate * duration;
            var clip = AudioClip.Create("BattleMusic", samples, 1, sampleRate, false);
            float[] data = new float[samples];

            // More intense: pulsing bass + dramatic chords
            float[] bassNotes = { 82.41f, 87.31f, 98.0f, 110.0f }; // E2, F2, G2, A2
            float bpm = 100f;
            float beatLen = 60f / bpm * sampleRate;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                int beat = (int)(i / beatLen);

                // Punchy bass pulse
                float bassFreq = bassNotes[beat % bassNotes.Length];
                float beatPos = (i % (int)beatLen) / beatLen;
                float bassEnv = beatPos < 0.3f ? 1f - (beatPos / 0.3f) : 0f;
                float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * 0.2f * bassEnv;

                // Tension chord pad
                float pad = Mathf.Sin(2f * Mathf.PI * 164.81f * t) * 0.04f; // E3
                pad += Mathf.Sin(2f * Mathf.PI * 196.0f * t) * 0.03f; // G3
                pad += Mathf.Sin(2f * Mathf.PI * 246.94f * t) * 0.03f; // B3

                // Percussion-like clicks on beats
                float perc = 0f;
                if (beatPos < 0.02f)
                    perc = (0.02f - beatPos) / 0.02f * 0.15f * Mathf.Sin(2f * Mathf.PI * 800f * t);

                data[i] = Mathf.Clamp(bass + pad + perc, -1f, 1f);
            }

            clip.SetData(data, 0);
            _battleSource.clip = clip;
        }

        private void GenerateAmbientTrack()
        {
            int sampleRate = 44100;
            int duration = 40;
            int samples = sampleRate * duration;
            var clip = AudioClip.Create("AmbientMusic", samples, 1, sampleRate, false);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;

                // Warm evolving pad
                float pad = Mathf.Sin(2f * Mathf.PI * 130.81f * t) * 0.06f;
                pad += Mathf.Sin(2f * Mathf.PI * 196.0f * t + Mathf.Sin(t * 0.2f) * 2f) * 0.04f;
                pad += Mathf.Sin(2f * Mathf.PI * 261.63f * t + Mathf.Sin(t * 0.15f) * 3f) * 0.03f;

                // Sparkle
                float sparkle = Mathf.Sin(2f * Mathf.PI * 1046.5f * t + Mathf.Sin(t * 0.5f) * 6f) * 0.008f;
                float sparkle2 = Mathf.Sin(2f * Mathf.PI * 784.0f * t + Mathf.Sin(t * 0.7f) * 4f) * 0.006f;

                data[i] = Mathf.Clamp(pad + sparkle + sparkle2, -1f, 1f);
            }

            clip.SetData(data, 0);
            _ambientSource.clip = clip;
        }

        public void SetVolume(float vol)
        {
            volume = Mathf.Clamp01(vol);
            if (_menuSource && _menuSource.isPlaying) _menuSource.volume = volume;
            if (_battleSource && _battleSource.isPlaying) _battleSource.volume = volume;
            if (_ambientSource && _ambientSource.isPlaying) _ambientSource.volume = volume;
        }
    }
}
