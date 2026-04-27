using UnityEngine;
using WorkspaceVoiceAssistant;

public sealed class OrganizerSpeechOutputBridge : MonoBehaviour
{
    [SerializeField] private OllamaWorkspaceOrganizer organizer;
    [SerializeField] private SpeechOutputManager speechOutputManager;
    [SerializeField] private bool speakOrganizerRecommendations = true;

    private bool suppressNextRecommendationSpeech;

    public void SuppressNextRecommendationSpeech()
    {
        suppressNextRecommendationSpeech = true;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (organizer != null)
        {
            organizer.RecommendationReceived += HandleRecommendationReceived;
        }
    }

    private void OnDisable()
    {
        if (organizer != null)
        {
            organizer.RecommendationReceived -= HandleRecommendationReceived;
        }
    }

    private void HandleRecommendationReceived(WorkspaceArrangementRecommendation recommendation)
    {
        if (suppressNextRecommendationSpeech)
        {
            suppressNextRecommendationSpeech = false;
            Debug.Log("[OrganizerSpeechOutputBridge] Skipping speech for this recommendation.", this);
            return;
        }

        if (!speakOrganizerRecommendations)
        {
            return;
        }

        ResolveReferences();
        if (speechOutputManager != null)
        {
            speechOutputManager.SpeakRecommendation(recommendation);
        }
    }

    private void ResolveReferences()
    {
        if (organizer == null)
        {
            organizer = GetComponent<OllamaWorkspaceOrganizer>();
        }

        if (speechOutputManager == null)
        {
            speechOutputManager = GetComponent<SpeechOutputManager>();
        }

        if (organizer == null)
        {
            organizer = FindAnyObjectByType<OllamaWorkspaceOrganizer>();
        }

        if (speechOutputManager == null)
        {
            speechOutputManager = FindAnyObjectByType<SpeechOutputManager>();
        }
    }
}
