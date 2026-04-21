using System;
using System.Collections.Generic;
using System.Globalization;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEngine;

namespace WorkspaceVoiceAssistant
{
    /// <summary>
    /// Converts object detections and an optional user goal into a prompt that
    /// keeps Ollama grounded in the scene actually detected on-device.
    /// </summary>
    public sealed class SceneContextBuilder : MonoBehaviour
    {
        private const string SceneJsonPlaceholder = "{SCENE_JSON_HERE}";
        private const string GoalPlaceholder = "{USER_GOAL_HERE}";
        private const string DefaultPromptTemplate =
            "You are a real-world workspace organization assistant for a mixed reality app.\n" +
            "Use only the provided objects, zones, and surface data.\n" +
            "Do not invent missing objects, furniture, storage, or tools.\n" +
            "Keep liquids away from electronics.\n" +
            "Prioritize the user's goal when choosing the arrangement.\n" +
            "Return valid JSON only.\n" +
            "No markdown.\n" +
            "No explanation outside the schema.\n" +
            "Give 2 to 4 short, actionable steps.\n\n" +
            "User goal: " + GoalPlaceholder + "\n\n" +
            "Scene JSON:\n" + SceneJsonPlaceholder + "\n\n" +
            "Return exactly this JSON schema:\n" +
            "{\n" +
            "  \"summary\": \"short summary\",\n" +
            "  \"steps\": [\"step 1\", \"step 2\", \"step 3\"],\n" +
            "  \"speak\": \"natural spoken instruction\"\n" +
            "}";

        [Header("Prompt")]
        [TextArea(10, 20)]
        [SerializeField] private string promptTemplate = DefaultPromptTemplate;
        [SerializeField] private string defaultGoal = "organize the desk efficiently";

        [Header("Scene Defaults")]
        [SerializeField] private string defaultSurface = "desk";
        [SerializeField] private bool prettyPrintSceneJson = true;

        public string DefaultGoal => string.IsNullOrWhiteSpace(defaultGoal) ? "organize the desk efficiently" : defaultGoal.Trim();

        public string BuildPrompt(string sceneJson, string userTranscript)
        {
            string activePromptTemplate = string.IsNullOrWhiteSpace(promptTemplate)
                ? DefaultPromptTemplate
                : promptTemplate;
            string safeSceneJson = string.IsNullOrWhiteSpace(sceneJson)
                ? JsonUtility.ToJson(CreateEmptySceneData(), prettyPrintSceneJson)
                : sceneJson.Trim();
            string safeGoal = string.IsNullOrWhiteSpace(userTranscript)
                ? DefaultGoal
                : userTranscript.Trim();

            string prompt = activePromptTemplate.Replace(SceneJsonPlaceholder, safeSceneJson);
            prompt = prompt.Replace(GoalPlaceholder, safeGoal);

            if (!prompt.Contains(safeSceneJson, StringComparison.Ordinal))
            {
                prompt += "\n\nScene JSON:\n" + safeSceneJson;
            }

            return prompt;
        }

        public string BuildSceneJson(List<BoxData> detections)
        {
            SceneData sceneData = BuildSceneData(detections);
            return JsonUtility.ToJson(sceneData, prettyPrintSceneJson);
        }

        public string BuildSceneJson(SceneData sceneData)
        {
            SceneData safeSceneData = sceneData ?? CreateEmptySceneData();
            return JsonUtility.ToJson(safeSceneData, prettyPrintSceneJson);
        }

        public SceneData BuildSceneData(List<BoxData> detections)
        {
            SceneData sceneData = CreateEmptySceneData();

            if (detections == null || detections.Count == 0)
            {
                return sceneData;
            }

            HashSet<string> zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string inferredSurface = defaultSurface;

            for (int i = 0; i < detections.Count; i++)
            {
                BoxData detection = detections[i];
                string rawLabel = string.IsNullOrWhiteSpace(detection.label) ? "object" : detection.label.Trim();
                string name = ExtractObjectName(rawLabel);
                string zone = InferZone(detection);
                float confidence = ExtractConfidence(rawLabel);

                if (IsSurfaceName(name))
                {
                    inferredSurface = name;
                }

                sceneData.objects.Add(new DetectedObject
                {
                    name = name,
                    zone = zone,
                    confidence = confidence,
                    rawLabel = rawLabel
                });

                if (!string.IsNullOrWhiteSpace(zone) && zones.Add(zone))
                {
                    sceneData.zones.Add(zone);
                }
            }

            if (sceneData.zones.Count == 0)
            {
                sceneData.zones.Add("camera view");
            }

            sceneData.surface = string.IsNullOrWhiteSpace(inferredSurface) ? defaultSurface : inferredSurface;
            return sceneData;
        }

        public SceneData CreateEmptySceneData()
        {
            SceneData sceneData = new SceneData
            {
                surface = string.IsNullOrWhiteSpace(defaultSurface) ? "desk" : defaultSurface.Trim()
            };
            sceneData.zones.Add("camera view");
            return sceneData;
        }

        private static string ExtractObjectName(string rawLabel)
        {
            if (string.IsNullOrWhiteSpace(rawLabel))
            {
                return "object";
            }

            string label = rawLabel.Trim();
            int lastSpace = label.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                string possibleScore = label.Substring(lastSpace + 1);
                if (float.TryParse(possibleScore, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    label = label.Substring(0, lastSpace);
                }
            }

            return string.IsNullOrWhiteSpace(label) ? "object" : label.Trim().ToLowerInvariant();
        }

        private static float ExtractConfidence(string rawLabel)
        {
            if (string.IsNullOrWhiteSpace(rawLabel))
            {
                return 0f;
            }

            string label = rawLabel.Trim();
            int lastSpace = label.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                string possibleScore = label.Substring(lastSpace + 1);
                if (float.TryParse(possibleScore, NumberStyles.Float, CultureInfo.InvariantCulture, out float confidence))
                {
                    return confidence;
                }
            }

            return 0f;
        }

        private static bool IsSurfaceName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            return objectName.Contains("desk", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("table", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("counter", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("shelf", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("floor", StringComparison.OrdinalIgnoreCase);
        }

        private static string InferZone(BoxData detection)
        {
            if (float.IsNaN(detection.position.x) || float.IsInfinity(detection.position.x))
            {
                return "camera view";
            }

            float x = detection.position.x;
            if (x >= 0f && x <= 1f)
            {
                if (x < 0.33f)
                {
                    return "left";
                }

                if (x > 0.66f)
                {
                    return "right";
                }

                return "center";
            }

            return "camera view";
        }
    }
}
