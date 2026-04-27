using System;
using System.Collections.Generic;
using UnityEngine.Events;

namespace WorkspaceVoiceAssistant
{
    [Serializable]
    public sealed class DetectedObject
    {
        public string name;
        public string zone;
        public float confidence;
        public string rawLabel;
        public int detectionIndex;
        public float x;
        public float y;
        public float width;
        public float height;
        public string horizontalPosition;
        public string verticalPosition;
        public string size;
    }

    [Serializable]
    public sealed class SceneData
    {
        public string surface = "desk";
        public List<DetectedObject> objects = new List<DetectedObject>();
        public List<string> zones = new List<string>();
    }

    [Serializable]
    public sealed class OllamaGenerateRequest
    {
        public string model;
        public string prompt;
        public bool stream;
        public string format;
        public OllamaGenerateOptions options;

        public OllamaGenerateRequest(string model, string prompt)
        {
            this.model = model;
            this.prompt = prompt;
            stream = false;
            format = "json";
            options = new OllamaGenerateOptions();
        }
    }

    [Serializable]
    public sealed class OllamaGenerateOptions
    {
        public float temperature = 0.2f;
    }

    [Serializable]
    public sealed class OllamaGenerateResponse
    {
        public string model;
        public string created_at;
        public string response;
        public bool done;
        public string error;
    }

    [Serializable]
    public sealed class Recommendation
    {
        public string summary;
        public List<string> steps = new List<string>();
        public string speak;
    }

    [Serializable]
    public sealed class RecommendationEvent : UnityEvent<Recommendation>
    {
    }

    [Serializable]
    public sealed class StringEvent : UnityEvent<string>
    {
    }
}
