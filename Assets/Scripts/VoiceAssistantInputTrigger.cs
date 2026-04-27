using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WorkspaceVoiceAssistant
{
    /// <summary>
    /// Small runtime trigger for the speech-to-speech flow.
    /// </summary>
    public sealed class VoiceAssistantInputTrigger : MonoBehaviour
    {
        [SerializeField] private VoiceAssistantOrchestrator voiceAssistant;
        [SerializeField] private bool listenOnRightControllerB = true;
        [SerializeField] private bool listenOnKeyboardV = true;
        [SerializeField, Min(0.1f)] private float cooldownSeconds = 0.75f;

        private readonly List<UnityEngine.XR.InputDevice> rightHandDevices = new List<UnityEngine.XR.InputDevice>();
        private bool wasPressed;
        private float nextAllowedTriggerTime;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            ResolveReferences();

            if (voiceAssistant == null)
            {
                return;
            }

            bool pressed = IsTriggerPressed();
            bool pressedThisFrame = pressed && !wasPressed;
            wasPressed = pressed;

            if (!pressedThisFrame || Time.unscaledTime < nextAllowedTriggerTime)
            {
                return;
            }

            nextAllowedTriggerTime = Time.unscaledTime + cooldownSeconds;
            voiceAssistant.StartListening();
        }

        private void ResolveReferences()
        {
            if (voiceAssistant == null)
            {
                voiceAssistant = GetComponent<VoiceAssistantOrchestrator>();
            }

            if (voiceAssistant == null)
            {
                voiceAssistant = FindAnyObjectByType<VoiceAssistantOrchestrator>();
            }
        }

        private bool IsTriggerPressed()
        {
            return (listenOnRightControllerB && IsQuestRightControllerBPressed())
                || (listenOnKeyboardV && IsKeyboardVPressed());
        }

        private bool IsQuestRightControllerBPressed()
        {
            rightHandDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right,
                rightHandDevices);

            for (int i = 0; i < rightHandDevices.Count; i++)
            {
                if (rightHandDevices[i].TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool isPressed) && isPressed)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKeyboardVPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.vKey.isPressed)
            {
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.V);
#else
            return false;
#endif
        }
    }
}
