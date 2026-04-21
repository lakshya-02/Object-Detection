using System;
using Meta.XR.BuildingBlocks.AIBlocks;
using Oculus.Voice.Dictation;
using UnityEngine;

namespace WorkspaceVoiceAssistant
{
    /// <summary>
    /// Normalizes speech recognition callbacks so the rest of the app can work
    /// with one transcript event, whether it comes from Meta dictation,
    /// the Meta speech-to-text block, or a manual debug trigger.
    /// </summary>
    public sealed class SpeechInputManager : MonoBehaviour
    {
        public enum SpeechInputBackend
        {
            Auto,
            MetaDictationExperience,
            MetaSpeechToTextAgent,
            ExternalCallbackOnly
        }

        [Header("Input Source")]
        [SerializeField] private SpeechInputBackend backend = SpeechInputBackend.Auto;
        [SerializeField] private AppDictationExperience dictationExperience;
        [SerializeField] private SpeechToTextAgent speechToTextAgent;
        [SerializeField] private bool autoFindSceneComponents = true;

        [Header("Transcript")]
        [TextArea(2, 4)]
        [SerializeField] private string latestTranscript = string.Empty;

        [Header("Editor Debug")]
        [TextArea(2, 4)]
        [SerializeField] private string debugTranscript = "Organize this desk for studying";

        [Header("Events")]
        [SerializeField] private StringEvent onSpeechRecognized = new StringEvent();
        [SerializeField] private StringEvent onSpeechError = new StringEvent();

        private bool isListening;

        public event Action<string> SpeechRecognized;
        public event Action<string> SpeechError;
        public event Action ListeningStarted;
        public event Action ListeningStopped;

        public string LatestTranscript => latestTranscript;

        public string DebugTranscript
        {
            get => debugTranscript;
            set => debugTranscript = value;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeToSpeechSources();
        }

        private void OnDisable()
        {
            UnsubscribeFromSpeechSources();
        }

        [ContextMenu("Speech Input/Start Listening")]
        public void StartListening()
        {
            ResolveReferences();

            SpeechInputBackend activeBackend = GetActiveBackend();
            switch (activeBackend)
            {
                case SpeechInputBackend.MetaDictationExperience:
                    if (dictationExperience == null)
                    {
                        ReportError("AppDictationExperience is not assigned.");
                        return;
                    }

                    isListening = true;
                    ListeningStarted?.Invoke();
                    dictationExperience.Activate();
                    break;

                case SpeechInputBackend.MetaSpeechToTextAgent:
                    if (speechToTextAgent == null)
                    {
                        ReportError("SpeechToTextAgent is not assigned.");
                        return;
                    }

                    isListening = true;
                    ListeningStarted?.Invoke();
                    speechToTextAgent.StartListening();
                    break;

                case SpeechInputBackend.ExternalCallbackOnly:
                    isListening = true;
                    ListeningStarted?.Invoke();
                    Debug.Log("[SpeechInputManager] Waiting for an external OnSpeechRecognized callback.", this);
                    break;
            }
        }

        [ContextMenu("Speech Input/Stop Listening")]
        public void StopListening()
        {
            ResolveReferences();

            if (dictationExperience != null)
            {
                dictationExperience.Deactivate();
            }

            if (speechToTextAgent != null)
            {
                speechToTextAgent.StopNow();
            }

            if (!isListening)
            {
                return;
            }

            isListening = false;
            ListeningStopped?.Invoke();
        }

        /// <summary>
        /// Entry point for Meta Voice SDK blocks, UnityEvents, or any external STT source.
        /// </summary>
        public void OnSpeechRecognized(string transcript)
        {
            string normalizedTranscript = string.IsNullOrWhiteSpace(transcript)
                ? string.Empty
                : transcript.Trim();

            if (string.IsNullOrWhiteSpace(normalizedTranscript))
            {
                ReportError("Speech input was empty.");
                return;
            }

            latestTranscript = normalizedTranscript;

            if (isListening)
            {
                isListening = false;
                ListeningStopped?.Invoke();
            }

            onSpeechRecognized.Invoke(normalizedTranscript);
            SpeechRecognized?.Invoke(normalizedTranscript);
            Debug.Log("[SpeechInputManager] Transcript: " + normalizedTranscript, this);
        }

        [ContextMenu("Speech Input/Submit Debug Transcript")]
        public void SubmitDebugTranscript()
        {
            OnSpeechRecognized(debugTranscript);
        }

        public void ClearLatestTranscript()
        {
            latestTranscript = string.Empty;
        }

        private void ResolveReferences()
        {
            if (!autoFindSceneComponents)
            {
                return;
            }

            if (dictationExperience == null)
            {
                dictationExperience = FindAnyObjectByType<AppDictationExperience>();
            }

            if (speechToTextAgent == null)
            {
                speechToTextAgent = FindAnyObjectByType<SpeechToTextAgent>();
            }
        }

        private SpeechInputBackend GetActiveBackend()
        {
            if (backend != SpeechInputBackend.Auto)
            {
                return backend;
            }

            if (dictationExperience != null)
            {
                return SpeechInputBackend.MetaDictationExperience;
            }

            if (speechToTextAgent != null)
            {
                return SpeechInputBackend.MetaSpeechToTextAgent;
            }

            return SpeechInputBackend.ExternalCallbackOnly;
        }

        private void SubscribeToSpeechSources()
        {
            if (dictationExperience != null)
            {
                dictationExperience.DictationEvents.OnFullTranscription.AddListener(OnSpeechRecognized);
            }

            if (speechToTextAgent != null)
            {
                speechToTextAgent.onTranscript.AddListener(OnSpeechRecognized);
            }
        }

        private void UnsubscribeFromSpeechSources()
        {
            if (dictationExperience != null)
            {
                dictationExperience.DictationEvents.OnFullTranscription.RemoveListener(OnSpeechRecognized);
            }

            if (speechToTextAgent != null)
            {
                speechToTextAgent.onTranscript.RemoveListener(OnSpeechRecognized);
            }
        }

        private void ReportError(string message)
        {
            Debug.LogWarning("[SpeechInputManager] " + message, this);
            onSpeechError.Invoke(message);
            SpeechError?.Invoke(message);
        }
    }
}
