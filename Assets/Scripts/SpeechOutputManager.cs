using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEngine;
using UnityEngine.Events;

namespace WorkspaceVoiceAssistant
{
    /// <summary>
    /// Speaks a recommendation through Meta TTS when available, with a generic
    /// fallback for any component that exposes Speak(string) or SpeakText(string).
    /// </summary>
    public sealed class SpeechOutputManager : MonoBehaviour
    {
        [Header("Speech Output")]
        [SerializeField] private TextToSpeechAgent metaTextToSpeechAgent;
        [Tooltip("Optional generic TTS component exposing Speak(string) or SpeakText(string).")]
        [SerializeField] private MonoBehaviour fallbackSpeaker;

        [Header("Timing")]
        [SerializeField, Min(0.1f)] private float genericSpeechFinishedDelaySeconds = 2f;
        [SerializeField, Min(0.5f)] private float stepPauseSeconds = 0.35f;
        [SerializeField, Min(1f)] private float metaSpeechTimeoutSeconds = 30f;

        [Header("Events")]
        [SerializeField] private StringEvent onSpeechStarted = new StringEvent();
        [SerializeField] private StringEvent onSpeechError = new StringEvent();
        [SerializeField] private UnityEvent onSpeechFinished = new UnityEvent();

        private Coroutine fallbackFinishRoutine;
        private Coroutine metaTimeoutRoutine;

        public event Action<string> SpeechStarted;
        public event Action<string> SpeechError;
        public event Action SpeechFinished;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (metaTextToSpeechAgent != null)
            {
                metaTextToSpeechAgent.onClipReady.AddListener(HandleMetaClipReady);
                metaTextToSpeechAgent.onSpeakStarting.AddListener(HandleMetaSpeakStarting);
                metaTextToSpeechAgent.onSpeakFinished.AddListener(HandleMetaSpeechFinished);
            }
        }

        private void OnDisable()
        {
            StopSpeaking();
            if (metaTextToSpeechAgent != null)
            {
                metaTextToSpeechAgent.onClipReady.RemoveListener(HandleMetaClipReady);
                metaTextToSpeechAgent.onSpeakStarting.RemoveListener(HandleMetaSpeakStarting);
                metaTextToSpeechAgent.onSpeakFinished.RemoveListener(HandleMetaSpeechFinished);
            }
        }

        public void SpeakRecommendation(Recommendation recommendation)
        {
            if (recommendation == null)
            {
                ReportSpeechError("Recommendation was null.");
                return;
            }

            Debug.Log("[SpeechOutputManager] Summary: " + recommendation.summary, this);
            if (recommendation.steps != null)
            {
                for (int i = 0; i < recommendation.steps.Count; i++)
                {
                    Debug.Log("[SpeechOutputManager] Step " + (i + 1) + ": " + recommendation.steps[i], this);
                }
            }

            string speechText = !string.IsNullOrWhiteSpace(recommendation.speak)
                ? recommendation.speak
                : string.Join(" ", recommendation.steps ?? new List<string>());

            Speak(speechText);
        }

        public void SpeakRecommendation(global::WorkspaceArrangementRecommendation recommendation)
        {
            if (recommendation == null)
            {
                ReportSpeechError("Recommendation was null.");
                return;
            }

            Debug.Log("[SpeechOutputManager] Summary: " + recommendation.summary, this);
            if (recommendation.steps != null)
            {
                for (int i = 0; i < recommendation.steps.Length; i++)
                {
                    Debug.Log("[SpeechOutputManager] Step " + (i + 1) + ": " + recommendation.steps[i], this);
                }
            }

            string speechText = !string.IsNullOrWhiteSpace(recommendation.speak)
                ? recommendation.speak
                : string.Join(" ", recommendation.steps ?? Array.Empty<string>());

            Speak(speechText);
        }

        public void Speak(string text)
        {
            ResolveReferences();

            if (string.IsNullOrWhiteSpace(text))
            {
                ReportSpeechError("Speech text was empty.");
                return;
            }

            StopSpeaking();

            if (metaTextToSpeechAgent != null)
            {
                Debug.Log("[SpeechOutputManager] Sending text to Meta TextToSpeechAgent. Characters: " + text.Length, this);
                onSpeechStarted.Invoke(text);
                SpeechStarted?.Invoke(text);
                metaTextToSpeechAgent.SpeakText(text);
                metaTimeoutRoutine = StartCoroutine(ReportMetaTimeout());
                return;
            }

            if (TryInvokeFallbackSpeak(text))
            {
                onSpeechStarted.Invoke(text);
                SpeechStarted?.Invoke(text);
                fallbackFinishRoutine = StartCoroutine(NotifyFinishedAfterDelay(genericSpeechFinishedDelaySeconds));
                return;
            }

            ReportSpeechError("No compatible TTS speaker is assigned. Use Meta TextToSpeechAgent or a component with Speak(string).");
        }

        public void StopSpeaking()
        {
            if (metaTimeoutRoutine != null)
            {
                StopCoroutine(metaTimeoutRoutine);
                metaTimeoutRoutine = null;
            }

            if (fallbackFinishRoutine != null)
            {
                StopCoroutine(fallbackFinishRoutine);
                fallbackFinishRoutine = null;
            }

            if (metaTextToSpeechAgent != null)
            {
                metaTextToSpeechAgent.StopSpeaking();
            }

            TryInvokeFallbackStop();
        }

        public IEnumerator SpeakSteps(List<string> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                yield break;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                string step = steps[i];
                if (string.IsNullOrWhiteSpace(step))
                {
                    continue;
                }

                Speak(step);

                if (metaTextToSpeechAgent != null)
                {
                    float deadline = Time.unscaledTime + metaSpeechTimeoutSeconds;
                    bool finished = false;

                    UnityAction finishedListener = () => finished = true;
                    metaTextToSpeechAgent.onSpeakFinished.AddListener(finishedListener);

                    while (!finished && Time.unscaledTime < deadline)
                    {
                        yield return null;
                    }

                    metaTextToSpeechAgent.onSpeakFinished.RemoveListener(finishedListener);
                }
                else
                {
                    yield return new WaitForSecondsRealtime(genericSpeechFinishedDelaySeconds);
                }

                if (i < steps.Count - 1)
                {
                    yield return new WaitForSecondsRealtime(stepPauseSeconds);
                }
            }
        }

        private void ResolveReferences()
        {
            if (metaTextToSpeechAgent == null)
            {
                metaTextToSpeechAgent = FindAnyObjectByType<TextToSpeechAgent>();
            }
        }

        private void HandleMetaSpeechFinished()
        {
            if (metaTimeoutRoutine != null)
            {
                StopCoroutine(metaTimeoutRoutine);
                metaTimeoutRoutine = null;
            }
            Debug.Log("[SpeechOutputManager] Meta TextToSpeechAgent finished playback.", this);
            onSpeechFinished.Invoke();
            SpeechFinished?.Invoke();
        }

        private void HandleMetaClipReady(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogWarning("[SpeechOutputManager] Meta TextToSpeechAgent reported a null AudioClip.", this);
                return;
            }

            if (metaTimeoutRoutine != null)
            {
                StopCoroutine(metaTimeoutRoutine);
                metaTimeoutRoutine = null;
            }

            Debug.Log(
                "[SpeechOutputManager] Meta TextToSpeechAgent clip ready. Length: " +
                clip.length.ToString("0.00") + "s, samples: " + clip.samples + ", channels: " + clip.channels,
                this);

            float[] samples = new float[Mathf.Min(4096, clip.samples * clip.channels)];
            if (samples.Length > 0 && clip.GetData(samples, Mathf.Max(0, clip.samples / 2 - samples.Length / clip.channels / 2)))
            {
                float peak = 0f;
                foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
                Debug.Log("[SpeechOutputManager] Clip peak: " + peak.ToString("0.000")
                    + ", listener volume: " + AudioListener.volume.ToString("0.00"), this);
            }
            else
            {
                Debug.LogWarning("[SpeechOutputManager] Could not read synthesized clip samples.", this);
            }

        }

        private void HandleMetaSpeakStarting(string spokenText)
        {
            Debug.Log("[SpeechOutputManager] Meta TextToSpeechAgent started playback.", this);
        }

        private IEnumerator NotifyFinishedAfterDelay(float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            fallbackFinishRoutine = null;
            onSpeechFinished.Invoke();
            SpeechFinished?.Invoke();
        }

        private IEnumerator ReportMetaTimeout()
        {
            yield return new WaitForSecondsRealtime(metaSpeechTimeoutSeconds);
            metaTimeoutRoutine = null;
            ReportSpeechError("Speech service did not return audio before timeout.");
        }

        private bool TryInvokeFallbackSpeak(string text)
        {
            if (fallbackSpeaker == null)
            {
                return false;
            }

            return TryInvokeStringMethod(fallbackSpeaker, "Speak", text)
                || TryInvokeStringMethod(fallbackSpeaker, "SpeakText", text);
        }

        private void TryInvokeFallbackStop()
        {
            if (fallbackSpeaker == null)
            {
                return;
            }

            TryInvokeParameterlessMethod(fallbackSpeaker, "StopSpeaking");
            TryInvokeParameterlessMethod(fallbackSpeaker, "Stop");
        }

        private static bool TryInvokeStringMethod(MonoBehaviour target, string methodName, string argument)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);

            if (method == null)
            {
                return false;
            }

            method.Invoke(target, new object[] { argument });
            return true;
        }

        private static bool TryInvokeParameterlessMethod(MonoBehaviour target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            if (method == null)
            {
                return false;
            }

            method.Invoke(target, null);
            return true;
        }

        private void ReportSpeechError(string message)
        {
            Debug.LogWarning("[SpeechOutputManager] " + message, this);
            onSpeechError.Invoke(message);
            SpeechError?.Invoke(message);
        }
    }
}
