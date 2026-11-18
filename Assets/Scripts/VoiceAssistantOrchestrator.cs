using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.BuildingBlocks.AIBlocks;
using Meta.XR;
using UnityEngine;
using UnityEngine.Events;

namespace WorkspaceVoiceAssistant
{
    /// <summary>
    /// Main speech-to-speech glue component:
    /// transcript -> scene snapshot -> Ollama -> recommendation -> TTS.
    /// </summary>
    public sealed class VoiceAssistantOrchestrator : MonoBehaviour
    {
        public enum VoiceAssistantState
        {
            Idle,
            Listening,
            Thinking,
            Speaking,
            Error
        }

        [Header("Managers")]
        [SerializeField] private SpeechInputManager speechInputManager;
        [SerializeField] private SceneContextBuilder sceneContextBuilder;
        [SerializeField] private OllamaSpeechPlanner ollamaSpeechPlanner;
        [SerializeField] private SpeechOutputManager speechOutputManager;

        [Header("Object Detection")]
        [SerializeField] private ObjectDetectionAgent objectDetectionAgent;
        [SerializeField] private bool requestFreshSceneOnSpeech = true;
        [SerializeField, Min(0.5f)] private float sceneCaptureTimeoutSeconds = 4f;

        [Header("Output")]
        [SerializeField] private bool autoSpeakRecommendation = true;
        [TextArea(4, 8)]
        [SerializeField] private string latestSceneJson = string.Empty;
        [SerializeField] private VoiceAssistantState currentState = VoiceAssistantState.Idle;

        [Header("Events")]
        [SerializeField] private RecommendationEvent onRecommendationReady = new RecommendationEvent();
        [SerializeField] private StringEvent onError = new StringEvent();
        [SerializeField] private StringEvent onStatusChanged = new StringEvent();

        private Coroutine activeSpeechRoutine;
        private bool isRequestInFlight;
        private UnityAction<List<BoxData>> detectionListener;

        public event Action<Recommendation> RecommendationReady;
        public event Action<string> ErrorReceived;
        public event Action<string> StatusChanged;

        public RecommendationEvent OnRecommendationReady => onRecommendationReady;
        public StringEvent OnError => onError;
        public StringEvent OnStatusChanged => onStatusChanged;

        public string LatestSceneJson => latestSceneJson;
        public VoiceAssistantState CurrentState => currentState;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (speechInputManager != null)
            {
                speechInputManager.SpeechRecognized += HandleSpeechRecognized;
                speechInputManager.SpeechError += HandleError;
                speechInputManager.ListeningStarted += HandleListeningStarted;
                speechInputManager.ListeningStopped += HandleListeningStopped;
            }

            if (ollamaSpeechPlanner != null)
            {
                ollamaSpeechPlanner.RecommendationReceived += HandleRecommendationReceived;
                ollamaSpeechPlanner.RequestFailed += HandleError;
                ollamaSpeechPlanner.RequestStarted += HandlePlannerStatus;
            }

            if (speechOutputManager != null)
            {
                speechOutputManager.SpeechStarted += HandleSpeechStarted;
                speechOutputManager.SpeechFinished += HandleSpeechFinished;
                speechOutputManager.SpeechError += HandleError;
            }
        }

        private void OnDisable()
        {
            if (detectionListener != null && objectDetectionAgent != null)
            {
                objectDetectionAgent.OnDetectionResponseReceived.RemoveListener(detectionListener);
                detectionListener = null;
            }

            if (activeSpeechRoutine != null) StopCoroutine(activeSpeechRoutine);
            activeSpeechRoutine = null;
            isRequestInFlight = false;
            ollamaSpeechPlanner?.CancelActiveRequest();

            if (speechInputManager != null)
            {
                speechInputManager.SpeechRecognized -= HandleSpeechRecognized;
                speechInputManager.SpeechError -= HandleError;
                speechInputManager.ListeningStarted -= HandleListeningStarted;
                speechInputManager.ListeningStopped -= HandleListeningStopped;
            }

            if (ollamaSpeechPlanner != null)
            {
                ollamaSpeechPlanner.RecommendationReceived -= HandleRecommendationReceived;
                ollamaSpeechPlanner.RequestFailed -= HandleError;
                ollamaSpeechPlanner.RequestStarted -= HandlePlannerStatus;
            }

            if (speechOutputManager != null)
            {
                speechOutputManager.SpeechStarted -= HandleSpeechStarted;
                speechOutputManager.SpeechFinished -= HandleSpeechFinished;
                speechOutputManager.SpeechError -= HandleError;
            }
        }

        [ContextMenu("Voice Assistant/Start Listening")]
        public void StartListening()
        {
            ResolveReferences();

            if (speechInputManager == null)
            {
                HandleError("SpeechInputManager is not assigned.");
                return;
            }

            if (isRequestInFlight)
            {
                Debug.LogWarning("[VoiceAssistantOrchestrator] Ignoring StartListening because a request is already running.", this);
                return;
            }

            SetState(VoiceAssistantState.Listening, "Listening...");
            speechInputManager.StartListening();
        }

        [ContextMenu("Voice Assistant/Stop Listening")]
        public void StopListening()
        {
            if (speechInputManager != null)
            {
                speechInputManager.StopListening();
            }

            if (!isRequestInFlight && currentState != VoiceAssistantState.Speaking)
            {
                SetState(VoiceAssistantState.Idle, "Idle");
            }
        }

        [ContextMenu("Voice Assistant/Run Debug Transcript")]
        public void RunDebugTranscript()
        {
            ResolveReferences();

            if (speechInputManager != null)
            {
                speechInputManager.SubmitDebugTranscript();
                return;
            }

            HandleSpeechRecognized("Organize this desk efficiently");
        }

        public void RunDefaultQuestion()
        {
            HandleSpeechRecognized("What objects can you see?");
        }

        public void SetLatestSceneJson(string sceneJson)
        {
            latestSceneJson = string.IsNullOrWhiteSpace(sceneJson) ? string.Empty : sceneJson.Trim();
        }

        public void SetLatestSceneData(SceneData sceneData)
        {
            ResolveReferences();
            if (sceneContextBuilder == null)
            {
                HandleError("SceneContextBuilder is not assigned.");
                return;
            }

            latestSceneJson = sceneContextBuilder.BuildSceneJson(sceneData);
        }

        private void HandleSpeechRecognized(string transcript)
        {
            if (isRequestInFlight || activeSpeechRoutine != null)
            {
                Debug.LogWarning("[VoiceAssistantOrchestrator] A speech request is already in progress. Ignoring new transcript.", this);
                return;
            }

            activeSpeechRoutine = StartCoroutine(ProcessSpeechRequest(transcript));
        }

        private IEnumerator ProcessSpeechRequest(string transcript)
        {
            ResolveReferences();
            isRequestInFlight = true;
            SetState(VoiceAssistantState.Thinking, "Preparing scene context...");

            string sceneJsonToUse = requestFreshSceneOnSpeech ? null : latestSceneJson;

            if (requestFreshSceneOnSpeech)
            {
                SetState(VoiceAssistantState.Thinking, "Detecting objects...");
                yield return CaptureFreshSceneJson(sceneJson =>
                {
                    sceneJsonToUse = sceneJson;
                    latestSceneJson = sceneJson;
                });
            }

            if (string.IsNullOrWhiteSpace(sceneJsonToUse))
            {
                activeSpeechRoutine = null;
                HandleError("A fresh detection did not return. Please try again while looking at the objects.");
                yield break;
            }

            SceneData scene;
            try
            {
                scene = JsonUtility.FromJson<SceneData>(sceneJsonToUse);
            }
            catch (ArgumentException)
            {
                scene = null;
            }

            if (scene == null || scene.objects == null)
            {
                activeSpeechRoutine = null;
                HandleError("The detection snapshot was invalid.");
                yield break;
            }

            if (sceneContextBuilder != null && sceneContextBuilder.TryAnswer(scene, transcript, out Recommendation localAnswer))
            {
                activeSpeechRoutine = null;
                HandleRecommendationReceived(localAnswer);
                yield break;
            }

            if (ollamaSpeechPlanner == null)
            {
                isRequestInFlight = false;
                activeSpeechRoutine = null;
                HandleError("OllamaSpeechPlanner is not assigned.");
                yield break;
            }

            ollamaSpeechPlanner.RequestRecommendation(sceneJsonToUse, transcript);
            activeSpeechRoutine = null;
        }

        private IEnumerator CaptureFreshSceneJson(Action<string> onSceneReady)
        {
            if (objectDetectionAgent == null || sceneContextBuilder == null)
            {
                yield break;
            }

            bool received = false;
            List<BoxData> detections = null;

            detectionListener = boxes =>
            {
                detections = boxes != null ? new List<BoxData>(boxes) : new List<BoxData>();
                received = true;
            };

            objectDetectionAgent.OnDetectionResponseReceived.AddListener(detectionListener);
            objectDetectionAgent.CallInference();

            float deadline = Time.unscaledTime + sceneCaptureTimeoutSeconds;
            while (!received && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            objectDetectionAgent.OnDetectionResponseReceived.RemoveListener(detectionListener);
            detectionListener = null;

            if (!received)
            {
                yield break;
            }

            PassthroughCameraAccess cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
            Texture cameraTexture = cameraAccess != null ? cameraAccess.GetTexture() : null;
            SceneData scene = sceneContextBuilder.BuildSceneData(
                detections, cameraTexture != null ? cameraTexture.width : 0,
                cameraTexture != null ? cameraTexture.height : 0);
            if (cameraTexture != null && cameraAccess.IsPlaying)
            {
                // shortcut: one synchronous readback per spoken question may pause a frame; use async readback if measured latency is noticeable.
                sceneContextBuilder.ApplyColors(scene, cameraAccess.GetColors(), cameraTexture.width, cameraTexture.height);
            }
            string sceneJson = sceneContextBuilder.BuildSceneJson(scene);
            onSceneReady?.Invoke(sceneJson);
        }

        private void HandleRecommendationReceived(Recommendation recommendation)
        {
            isRequestInFlight = false;
            onRecommendationReady.Invoke(recommendation);
            RecommendationReady?.Invoke(recommendation);

            if (autoSpeakRecommendation && speechOutputManager != null)
            {
                SetState(VoiceAssistantState.Speaking, "Speaking recommendation...");
                speechOutputManager.SpeakRecommendation(recommendation);
                return;
            }

            SetState(VoiceAssistantState.Idle, "Recommendation ready");
        }

        private void HandleListeningStarted()
        {
            SetState(VoiceAssistantState.Listening, "Listening...");
        }

        private void HandleListeningStopped()
        {
            if (!isRequestInFlight && currentState == VoiceAssistantState.Listening)
            {
                SetState(VoiceAssistantState.Idle, "Idle");
            }
        }

        private void HandlePlannerStatus(string status)
        {
            SetState(VoiceAssistantState.Thinking, status);
        }

        private void HandleSpeechStarted(string spokenText)
        {
            SetState(VoiceAssistantState.Speaking, "Speaking recommendation...");
        }

        private void HandleSpeechFinished()
        {
            if (currentState != VoiceAssistantState.Error)
            {
                SetState(VoiceAssistantState.Idle, "Ready");
            }
        }

        private void HandleError(string message)
        {
            isRequestInFlight = false;
            activeSpeechRoutine = null;
            SetState(VoiceAssistantState.Error, message);
            onError.Invoke(message);
            ErrorReceived?.Invoke(message);
        }

        private void SetState(VoiceAssistantState newState, string statusMessage)
        {
            currentState = newState;
            onStatusChanged.Invoke(statusMessage);
            StatusChanged?.Invoke(statusMessage);
            Debug.Log("[VoiceAssistantOrchestrator] " + newState + ": " + statusMessage, this);
        }

        private void ResolveReferences()
        {
            if (speechInputManager == null)
            {
                speechInputManager = GetComponent<SpeechInputManager>();
            }

            if (sceneContextBuilder == null)
            {
                sceneContextBuilder = GetComponent<SceneContextBuilder>();
            }

            if (ollamaSpeechPlanner == null)
            {
                ollamaSpeechPlanner = GetComponent<OllamaSpeechPlanner>();
            }

            if (speechOutputManager == null)
            {
                speechOutputManager = GetComponent<SpeechOutputManager>();
            }

            if (objectDetectionAgent == null)
            {
                objectDetectionAgent = FindAnyObjectByType<ObjectDetectionAgent>();
            }

            if (speechInputManager == null)
            {
                speechInputManager = FindAnyObjectByType<SpeechInputManager>();
            }

            if (sceneContextBuilder == null)
            {
                sceneContextBuilder = FindAnyObjectByType<SceneContextBuilder>();
            }

            if (ollamaSpeechPlanner == null)
            {
                ollamaSpeechPlanner = FindAnyObjectByType<OllamaSpeechPlanner>();
            }

            if (speechOutputManager == null)
            {
                speechOutputManager = FindAnyObjectByType<SpeechOutputManager>();
            }
        }
    }
}
