using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using WorkspaceVoiceAssistant;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class WorkspaceRequestInputTrigger : MonoBehaviour
{
    [SerializeField] private OllamaWorkspaceOrganizer organizer;
    [SerializeField] private VoiceAssistantOrchestrator voiceAssistant;
    [SerializeField] private OrganizerSpeechOutputBridge speechBridge;
    [SerializeField] private bool listenForPromptOnLeftX = true;
    [SerializeField] private bool requestDefaultOnLeftY = true;
    [SerializeField] private bool listenForPromptOnKeyboardX = true;
    [SerializeField] private bool requestDefaultOnKeyboardY = true;
    [SerializeField] private bool suppressSpeechOnDefaultRequest;
    [SerializeField, Min(0.1f)] private float cooldownSeconds = 1f;

    private readonly List<UnityEngine.XR.InputDevice> leftHandDevices = new List<UnityEngine.XR.InputDevice>();
    private bool wasVoicePressed;
    private bool wasUiOnlyPressed;
    private float nextAllowedRequestTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();

        if (organizer == null && voiceAssistant == null)
        {
            return;
        }

        bool voicePromptPressed = IsVoicePromptPressed();
        bool defaultPressed = IsDefaultRequestPressed();
        bool voicePressedThisFrame = voicePromptPressed && !wasVoicePressed;
        bool uiOnlyPressedThisFrame = defaultPressed && !wasUiOnlyPressed;

        wasVoicePressed = voicePromptPressed;
        wasUiOnlyPressed = defaultPressed;

        if (Time.unscaledTime < nextAllowedRequestTime)
        {
            return;
        }

        if (voicePressedThisFrame)
        {
            nextAllowedRequestTime = Time.unscaledTime + cooldownSeconds;

            if (voiceAssistant != null)
            {
                if (voiceAssistant.CurrentState == VoiceAssistantOrchestrator.VoiceAssistantState.Listening)
                {
                    Debug.Log("[WorkspaceRequestInputTrigger] X pressed while listening. Stopping voice capture so STT can process.", this);
                    voiceAssistant.StopListening();
                }
                else
                {
                    Debug.Log("[WorkspaceRequestInputTrigger] X pressed. Starting voice prompt, then object detection and Ollama.", this);
                    voiceAssistant.StartListening();
                }
            }
            else if (organizer != null)
            {
                Debug.Log("[WorkspaceRequestInputTrigger] X pressed without a voice assistant. Requesting default object detection and Ollama guidance.", this);
                organizer.RequestArrangementFromDetectedObjects();
            }

            return;
        }

        if (uiOnlyPressedThisFrame)
        {
            nextAllowedRequestTime = Time.unscaledTime + cooldownSeconds;
            Debug.Log("[WorkspaceRequestInputTrigger] Y pressed. Requesting default object detection and Ollama guidance.", this);

            if (suppressSpeechOnDefaultRequest && speechBridge != null)
            {
                speechBridge.SuppressNextRecommendationSpeech();
            }

            if (organizer != null)
            {
                organizer.RequestArrangementFromDetectedObjects();
            }
            else
            {
                Debug.LogWarning("[WorkspaceRequestInputTrigger] Cannot run default request because OllamaWorkspaceOrganizer is missing.", this);
            }
        }
    }

    private void ResolveReferences()
    {
        if (organizer == null)
        {
            organizer = GetComponent<OllamaWorkspaceOrganizer>();
        }

        if (speechBridge == null)
        {
            speechBridge = GetComponent<OrganizerSpeechOutputBridge>();
        }

        if (voiceAssistant == null)
        {
            voiceAssistant = GetComponent<VoiceAssistantOrchestrator>();
        }

        if (organizer == null)
        {
            organizer = FindAnyObjectByType<OllamaWorkspaceOrganizer>();
        }

        if (speechBridge == null)
        {
            speechBridge = FindAnyObjectByType<OrganizerSpeechOutputBridge>();
        }

        if (voiceAssistant == null)
        {
            voiceAssistant = FindAnyObjectByType<VoiceAssistantOrchestrator>();
        }
    }

    private bool IsVoicePromptPressed()
    {
        return (listenForPromptOnLeftX && IsLeftPrimaryButtonPressed())
            || (listenForPromptOnKeyboardX && IsKeyboardXPressed());
    }

    private bool IsDefaultRequestPressed()
    {
        return (requestDefaultOnLeftY && IsLeftSecondaryButtonPressed())
            || (requestDefaultOnKeyboardY && IsKeyboardYPressed());
    }

    private bool IsLeftPrimaryButtonPressed()
    {
        return IsLeftButtonPressed(UnityEngine.XR.CommonUsages.primaryButton);
    }

    private bool IsLeftSecondaryButtonPressed()
    {
        return IsLeftButtonPressed(UnityEngine.XR.CommonUsages.secondaryButton);
    }

    private bool IsLeftButtonPressed(InputFeatureUsage<bool> button)
    {
        leftHandDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left,
            leftHandDevices);

        for (int i = 0; i < leftHandDevices.Count; i++)
        {
            if (leftHandDevices[i].TryGetFeatureValue(button, out bool isPressed) && isPressed)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKeyboardXPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.xKey.isPressed)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.X);
#else
        return false;
#endif
    }

    private static bool IsKeyboardYPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.yKey.isPressed)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.Y);
#else
        return false;
#endif
    }
}
