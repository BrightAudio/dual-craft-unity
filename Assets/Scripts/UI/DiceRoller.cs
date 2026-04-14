// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Digital Dice Roller
//  Animated 6-sided dice with visual feedback.
//  Used during combat when cards specify dice mechanics.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DualCraft.UI
{
    public class DiceRoller : MonoBehaviour
    {
        [Header("Dice Display")]
        [SerializeField] private Image diceBackground;
        [SerializeField] private TextMeshProUGUI diceValueText;
        [SerializeField] private Image[] dicePips; // optional pip display
        [SerializeField] private TextMeshProUGUI resultLabel;

        [Header("Animation")]
        [SerializeField] private float rollDuration = 1.2f;
        [SerializeField] private float spinSpeed = 12f;

        [Header("Colors")]
        [SerializeField] private Color diceColor = new Color(0.12f, 0.12f, 0.15f);
        [SerializeField] private Color successColor = new Color(0.2f, 0.85f, 0.3f);
        [SerializeField] private Color failColor = new Color(0.9f, 0.25f, 0.25f);
        [SerializeField] private Color neutralColor = new Color(0.784f, 0.663f, 0.416f); // gold

        private bool _isRolling;
        private int _lastResult;

        public bool IsRolling => _isRolling;
        public int LastResult => _lastResult;

        public event Action<int> OnRollComplete;

        /// <summary>
        /// Roll a 6-sided die with animation. Calls onComplete with the result (1-6).
        /// </summary>
        public void Roll(Action<int> onComplete = null)
        {
            if (_isRolling) return;
            StartCoroutine(RollCoroutine(onComplete));
        }

        /// <summary>
        /// Roll and evaluate against a threshold.
        /// successMin..6 = success, 1..successMin-1 = fail.
        /// </summary>
        public void RollWithThreshold(int successMin, string successText, string failText, Action<int, bool> onComplete = null)
        {
            if (_isRolling) return;
            StartCoroutine(RollWithThresholdCoroutine(successMin, successText, failText, onComplete));
        }

        private IEnumerator RollCoroutine(Action<int> onComplete)
        {
            _isRolling = true;
            gameObject.SetActive(true);

            if (resultLabel) resultLabel.text = "";

            // Determine final result upfront
            int finalValue = UnityEngine.Random.Range(1, 7);
            _lastResult = finalValue;

            // Animate: rapidly cycle through random numbers
            float elapsed = 0f;
            float currentDelay = 0.04f;

            while (elapsed < rollDuration)
            {
                int display = UnityEngine.Random.Range(1, 7);
                UpdateDiceDisplay(display, neutralColor);

                // Shake effect
                float shakeAmount = Mathf.Lerp(8f, 0f, elapsed / rollDuration);
                transform.localPosition += new Vector3(
                    UnityEngine.Random.Range(-shakeAmount, shakeAmount),
                    UnityEngine.Random.Range(-shakeAmount, shakeAmount),
                    0f
                );

                // Scale pulse
                float pulse = 1f + Mathf.Sin(elapsed * spinSpeed) * 0.08f;
                transform.localScale = Vector3.one * pulse;

                yield return new WaitForSeconds(currentDelay);
                elapsed += currentDelay;

                // Slow down as we approach the end
                currentDelay = Mathf.Lerp(0.04f, 0.18f, elapsed / rollDuration);
            }

            // Final result
            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one;
            UpdateDiceDisplay(finalValue, neutralColor);

            // Brief flash
            yield return StartCoroutine(FlashDice(neutralColor));

            _isRolling = false;
            OnRollComplete?.Invoke(finalValue);
            onComplete?.Invoke(finalValue);
        }

        private IEnumerator RollWithThresholdCoroutine(int successMin, string successText, string failText, Action<int, bool> onComplete)
        {
            _isRolling = true;
            gameObject.SetActive(true);

            if (resultLabel) resultLabel.text = "Rolling...";

            int finalValue = UnityEngine.Random.Range(1, 7);
            _lastResult = finalValue;
            bool success = finalValue >= successMin;

            float elapsed = 0f;
            float currentDelay = 0.04f;

            while (elapsed < rollDuration)
            {
                int display = UnityEngine.Random.Range(1, 7);
                UpdateDiceDisplay(display, neutralColor);

                float shakeAmount = Mathf.Lerp(8f, 0f, elapsed / rollDuration);
                var basePos = GetComponent<RectTransform>()?.anchoredPosition ?? Vector2.zero;
                transform.localPosition += new Vector3(
                    UnityEngine.Random.Range(-shakeAmount, shakeAmount),
                    UnityEngine.Random.Range(-shakeAmount, shakeAmount), 0f);

                float pulse = 1f + Mathf.Sin(elapsed * spinSpeed) * 0.08f;
                transform.localScale = Vector3.one * pulse;

                yield return new WaitForSeconds(currentDelay);
                elapsed += currentDelay;
                currentDelay = Mathf.Lerp(0.04f, 0.18f, elapsed / rollDuration);
            }

            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one;

            Color resultColor = success ? successColor : failColor;
            UpdateDiceDisplay(finalValue, resultColor);

            if (resultLabel)
            {
                resultLabel.text = success ? successText : failText;
                resultLabel.color = resultColor;
            }

            yield return StartCoroutine(FlashDice(resultColor));

            _isRolling = false;
            OnRollComplete?.Invoke(finalValue);
            onComplete?.Invoke(finalValue, success);
        }

        private IEnumerator FlashDice(Color color)
        {
            // Scale up then back
            float t = 0f;
            while (t < 0.3f)
            {
                float scale = 1f + Mathf.Sin(t / 0.3f * Mathf.PI) * 0.15f;
                transform.localScale = Vector3.one * scale;
                t += Time.deltaTime;
                yield return null;
            }
            transform.localScale = Vector3.one;

            // Solid color for a moment
            if (diceBackground) diceBackground.color = Color.Lerp(diceColor, color, 0.3f);
            yield return new WaitForSeconds(0.8f);
            if (diceBackground) diceBackground.color = diceColor;
        }

        private void UpdateDiceDisplay(int value, Color accentColor)
        {
            if (diceValueText)
            {
                diceValueText.text = value.ToString();
                diceValueText.color = accentColor;
                diceValueText.fontSize = 48;
            }

            if (diceBackground)
                diceBackground.color = diceColor;

            // Update pips if available
            if (dicePips != null && dicePips.Length >= 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (dicePips[i])
                    {
                        dicePips[i].gameObject.SetActive(i < value);
                        dicePips[i].color = accentColor;
                    }
                }
            }
        }

        /// <summary>
        /// Hide the dice roller UI.
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Show the dice roller UI without rolling.
        /// </summary>
        public void Show()
        {
            gameObject.SetActive(true);
            if (resultLabel) resultLabel.text = "";
            if (diceValueText) diceValueText.text = "?";
        }
    }
}
