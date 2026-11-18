using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEngine;

namespace WorkspaceVoiceAssistant
{
    /// <summary>
    /// Converts current detections into grounded answers and AI advice.
    /// </summary>
    public sealed class SceneContextBuilder : MonoBehaviour
    {
        private const string SceneJsonPlaceholder = "{SCENE_JSON_HERE}";
        private const string GoalPlaceholder = "{USER_GOAL_HERE}";
        private const string DefaultPromptTemplate =
            "Choose up to three useful organization actions using only detectionIndex values in Scene JSON.\n" +
            "Each object is one current-frame detection; zones are screen-relative, not room locations.\n" +
            "Colors are approximate camera estimates; use them only when known.\n" +
            "If no supported placement follows from the detections, return {\"actions\":[]}.\n" +
            "Return JSON only: {\"actions\":[{\"objectIndex\":0,\"action\":\"move_left\",\"targetIndex\":-1}]}.\n" +
            "Allowed actions: move_left, move_right, move_center, group, separate.\n" +
            "Use targetIndex only for group or separate, and never invent indices.\n\n" +
            "Question: " + GoalPlaceholder + "\n\nScene JSON:\n" + SceneJsonPlaceholder;

        [Header("Prompt")]
        [TextArea(10, 20)]
        [SerializeField] private string promptTemplate = DefaultPromptTemplate;
        [SerializeField] private string defaultGoal = "organize the desk efficiently";

        [Header("Scene Defaults")]
        [SerializeField] private string defaultSurface = "unknown";
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

        public string BuildSceneJson(List<BoxData> detections, int imageWidth = 0, int imageHeight = 0)
        {
            SceneData sceneData = BuildSceneData(detections, imageWidth, imageHeight);
            return JsonUtility.ToJson(sceneData, prettyPrintSceneJson);
        }

        public string BuildSceneJson(SceneData sceneData)
        {
            SceneData safeSceneData = sceneData ?? CreateEmptySceneData();
            return JsonUtility.ToJson(safeSceneData, prettyPrintSceneJson);
        }

        public SceneData BuildSceneData(List<BoxData> detections, int imageWidth = 0, int imageHeight = 0)
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
                float centerX = imageWidth > 0 ? (detection.position.x + detection.scale.x) / (2f * imageWidth) : float.NaN;
                float centerY = imageHeight > 0 ? (detection.position.y + detection.scale.y) / (2f * imageHeight) : float.NaN;
                string horizontal = InferHorizontalPosition(centerX);
                string vertical = InferVerticalPosition(centerY);
                string zone = InferZone(horizontal, vertical);
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
                    rawLabel = rawLabel,
                    detectionIndex = i,
                    x = detection.position.x,
                    y = detection.position.y,
                    width = detection.scale.x - detection.position.x,
                    height = detection.scale.y - detection.position.y,
                    horizontalPosition = horizontal,
                    verticalPosition = vertical,
                    size = InferSize(detection, imageWidth, imageHeight)
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
                surface = string.IsNullOrWhiteSpace(defaultSurface) ? "unknown" : defaultSurface.Trim()
            };
            sceneData.zones.Add("camera view");
            return sceneData;
        }

        public void ApplyColors(SceneData scene, NativeArray<Color32> pixels, int width, int height)
        {
            if (scene?.objects == null || !pixels.IsCreated || width <= 0 || height <= 0
                || pixels.Length < width * height) return;

            foreach (DetectedObject item in scene.objects)
            {
                if (item == null || item.width <= 0 || item.height <= 0) continue;

                int[] votes = new int[6];
                for (int row = 0; row < 5; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        int x = Mathf.Clamp(Mathf.RoundToInt(item.x + item.width * (0.25f + col * 0.125f)), 0, width - 1);
                        int y = Mathf.Clamp(Mathf.RoundToInt(item.y + item.height * (0.25f + row * 0.125f)), 0, height - 1);
                        Color.RGBToHSV(pixels[y * width + x], out float hue, out float saturation, out float value);
                        if (saturation < 0.35f || value < 0.18f) continue;
                        int color = hue < 0.05f || hue >= 0.95f ? 0
                            : hue < 0.11f ? 1 : hue < 0.19f ? 2 : hue < 0.46f ? 3
                            : hue < 0.72f ? 4 : 5;
                        votes[color]++;
                    }
                }

                int best = 0;
                int runnerUp = 0;
                for (int i = 0; i < votes.Length; i++)
                {
                    if (votes[i] > votes[best]) best = i;
                }
                for (int i = 0; i < votes.Length; i++)
                {
                    if (i != best) runnerUp = Mathf.Max(runnerUp, votes[i]);
                }
                if (votes[best] >= 9 && votes[best] >= runnerUp * 2)
                    item.color = new[] { "red", "orange", "yellow", "green", "blue", "purple" }[best];
            }
        }

        public bool TryAnswer(SceneData scene, string transcript, out Recommendation answer)
        {
            answer = null;
            if (scene?.objects == null || string.IsNullOrWhiteSpace(transcript))
            {
                return false;
            }

            string question = transcript.Trim().TrimEnd('?', '.', '!').ToLowerInvariant();
            int questionStart = question.Length;
            foreach (string opening in new[] { "what ", "how many ", "where " })
            {
                int start = question.IndexOf(opening, StringComparison.Ordinal);
                if (start >= 0 && (start == 0 || !char.IsLetterOrDigit(question[start - 1])))
                    questionStart = Math.Min(questionStart, start);
            }
            if (questionStart < question.Length) question = question.Substring(questionStart);
            string response;

            if ((question.Contains("objects") && (question.Contains("see") || question.Contains("visible")))
                || question.Contains("what objects") || question.Contains("what can you see")
                || question.Contains("what do you see")
                || question == "what is the object" || question == "what is this object")
            {
                if (scene.objects.Count == 0)
                {
                    response = "I don't see any supported objects in the current view.";
                }
                else
                {
                    var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (DetectedObject item in scene.objects)
                    {
                        if (item != null && !string.IsNullOrWhiteSpace(item.name))
                        {
                            counts.TryGetValue(item.name, out int count);
                            counts[item.name] = count + 1;
                        }
                    }

                    var found = new List<string>();
                    foreach (var pair in counts)
                    {
                        found.Add(pair.Value + " " + Pluralize(pair.Key, pair.Value));
                    }

                    response = found.Count == 0
                        ? "I don't see any supported objects in the current view."
                        : "I can see " + string.Join(", ", found) + ".";
                }
            }
            else if (question.StartsWith("how many ", StringComparison.Ordinal))
            {
                string target = ObjectTarget(question.Substring("how many ".Length));
                if (target.Length == 0) return false;

                string[] targets = target.Split(new[] { " or ", " and " }, StringSplitOptions.RemoveEmptyEntries);
                var counts = new List<string>();
                bool foundAny = false;
                foreach (string requested in targets)
                {
                    int count = 0;
                    string detectedName = null;
                    foreach (DetectedObject item in scene.objects)
                    {
                        if (item != null && MatchesTarget(item.name, requested))
                        {
                            count++;
                            detectedName = item.name;
                        }
                    }
                    foundAny |= count > 0;
                    counts.Add(count == 0 ? "0 " + requested : count + " " + Pluralize(detectedName, count));
                }

                response = targets.Length > 1
                    ? "Current detections show " + string.Join(" and ", counts) + "."
                    : foundAny ? "I can see " + counts[0] + "."
                    : "I can't confirm any " + target + " from the current detections.";
            }
            else if (question.StartsWith("where is ", StringComparison.Ordinal)
                || question.StartsWith("where are ", StringComparison.Ordinal))
            {
                string target = ObjectTarget(question.Substring(question.StartsWith("where is ", StringComparison.Ordinal) ? 9 : 10));
                if (target.Length == 0) return false;

                var found = scene.objects.FindAll(item => item != null && MatchesTarget(item.name, target));
                if (found.Count == 0)
                    response = "I can't confirm a " + target + " from the current detections.";
                else if (found.Count == 1)
                    response = found[0].zone == "camera view" || string.IsNullOrWhiteSpace(found[0].zone)
                        ? "I can see a " + target + ", but I can't determine its screen location."
                        : "I can see a " + target + " in the " + found[0].zone + " of the camera view.";
                else
                {
                    var locations = new List<string>();
                    foreach (DetectedObject item in found)
                        locations.Add(Describe(item) + " in " + (string.IsNullOrWhiteSpace(item.zone) ? "an unknown area" : item.zone));
                    response = "I can see " + found.Count + " " + Pluralize(found[0].name, found.Count)
                        + ": " + string.Join(", ", locations) + ".";
                }
            }
            else if (question.StartsWith("which color of ", StringComparison.Ordinal)
                || question.StartsWith("which color is ", StringComparison.Ordinal)
                || question.StartsWith("what color is ", StringComparison.Ordinal)
                || question.StartsWith("what color are ", StringComparison.Ordinal)
                || question.StartsWith("what is the color of ", StringComparison.Ordinal))
            {
                int prefixLength = question.StartsWith("which color of ", StringComparison.Ordinal) ? "which color of ".Length
                    : question.StartsWith("which color is ", StringComparison.Ordinal) ? "which color is ".Length
                    : question.StartsWith("what is the color of ", StringComparison.Ordinal) ? 21
                    : question.StartsWith("what color are ", StringComparison.Ordinal) ? 15 : 14;
                string target = ObjectTarget(question.Substring(prefixLength));
                if (target.Length == 0) return false;

                var found = scene.objects.FindAll(item => item != null && MatchesTarget(item.name, target));
                if (found.Count == 0)
                    response = "I can't confirm any " + target + " from the current detections.";
                else if (found.TrueForAll(item => string.IsNullOrWhiteSpace(item.color) || item.color == "unknown"))
                    response = "I can see " + target + ", but I can't determine its color from the current view.";
                else
                {
                    var descriptions = new List<string>();
                    foreach (DetectedObject item in found)
                    {
                        string description = string.IsNullOrWhiteSpace(item.color) || item.color == "unknown"
                            ? "unknown color" : item.color;
                        if (found.Count > 1 && !string.IsNullOrWhiteSpace(item.zone) && item.zone != "camera view")
                            description += " in the " + item.zone;
                        descriptions.Add(description);
                    }
                    response = "The detected " + Pluralize(found[0].name, found.Count)
                        + (found.Count == 1 ? " is " : " are ")
                        + string.Join(", ", descriptions) + ".";
                }
            }
            else
            {
                bool asksForAdvice = question.Contains("organize") || question.Contains("arrange")
                    || question.Contains("tidy") || question.Contains("clean up");
                if (!transcript.TrimEnd().EndsWith("?", StringComparison.Ordinal) || asksForAdvice)
                {
                    return false;
                }

                response = "I can't determine that from the current object detections.";
            }

            answer = new Recommendation { summary = response, speak = response };
            answer.steps.Add(response);
            return true;
        }

        public bool TryBuildAdvice(SceneData scene, AiPlanResponse plan, out Recommendation answer)
        {
            answer = null;
            if (scene?.objects == null || plan?.actions == null || plan.actions.Count == 0 || plan.actions.Count > 3)
            {
                return false;
            }

            var steps = new List<string>();
            foreach (AiPlanAction action in plan.actions)
            {
                if (action == null || action.objectIndex < 0 || action.objectIndex >= scene.objects.Count)
                {
                    return false;
                }

                string name = Describe(scene.objects[action.objectIndex]);
                if (string.IsNullOrWhiteSpace(name)) return false;

                string step;
                switch (action.action)
                {
                    case "move_left": step = "Move the " + name + " toward the left side of your view."; break;
                    case "move_right": step = "Move the " + name + " toward the right side of your view."; break;
                    case "move_center": step = "Move the " + name + " toward the center of your view."; break;
                    case "group":
                    case "separate":
                        if (action.targetIndex < 0 || action.targetIndex >= scene.objects.Count
                            || action.targetIndex == action.objectIndex
                            || string.IsNullOrWhiteSpace(scene.objects[action.targetIndex].name)) return false;
                        string target = Describe(scene.objects[action.targetIndex]);
                        step = action.action == "group"
                            ? "Place the " + name + " beside the " + target + "."
                            : "Move the " + name + " away from the " + target + ".";
                        break;
                    default: return false;
                }

                steps.Add(step);
            }

            answer = new Recommendation
            {
                summary = "Here is a plan using the objects I can see.",
                steps = steps,
                speak = string.Join(" ", steps)
            };
            return true;
        }

        public Recommendation BuildUnavailableAdvice()
        {
            const string message = "I can't suggest a supported placement from the current detections.";
            return new Recommendation
            {
                summary = message,
                speak = message,
                steps = new List<string> { message }
            };
        }

        private static bool MatchesTarget(string label, string target)
        {
            return !string.IsNullOrWhiteSpace(label)
                && (string.Equals(target, label, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(target, Pluralize(label, 2), StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(label, "tv", StringComparison.OrdinalIgnoreCase)
                        && (target == "tv monitor" || target == "tv monitors"))
                    || (string.Equals(label, "tv monitor", StringComparison.OrdinalIgnoreCase)
                        && (target == "tv" || target == "tvs")));
        }

        private static string ObjectTarget(string text)
        {
            string target = text.Trim();
            foreach (string article in new[] { "my ", "the ", "this ", "that ", "these ", "those ", "a ", "an " })
                if (target.StartsWith(article, StringComparison.Ordinal))
                {
                    target = target.Substring(article.Length);
                    break;
                }
            int end = target.Length;
            foreach (string clause in new[] { " are ", " is ", " can ", " you ", " do ", " did ", " does ", " in ", " on ", " at ", " near ", " around ", " behind ", " beside ", " of ", " here", " there" })
            {
                int start = target.IndexOf(clause, StringComparison.Ordinal);
                if (start >= 0) end = Math.Min(end, start);
            }
            return target.Substring(0, end).Trim();
        }

        private static string Pluralize(string name, int count)
        {
            if (count == 1) return name;
            if (name == "person") return "people";
            if (name == "mouse") return "mice";
            if (name == "sheep") return "sheep";
            if (name == "knife") return "knives";
            if (name.EndsWith("ch", StringComparison.Ordinal) || name.EndsWith("sh", StringComparison.Ordinal)
                || name.EndsWith("x", StringComparison.Ordinal) || name.EndsWith("s", StringComparison.Ordinal))
                return name.EndsWith("s", StringComparison.Ordinal) && name != "bus" ? name : name + "es";
            if (name.EndsWith("y", StringComparison.Ordinal) && name.Length > 1
                && !"aeiou".Contains(name[name.Length - 2]))
                return name.Substring(0, name.Length - 1) + "ies";
            return name + "s";
        }

        private static string Describe(DetectedObject item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.name)) return null;
            return string.IsNullOrWhiteSpace(item.color) || item.color == "unknown"
                ? item.name : item.color + " " + item.name;
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

        private static string InferZone(string horizontal, string vertical)
        {
            if (horizontal == "camera view" && vertical == "camera view")
            {
                return "camera view";
            }

            if (vertical == "middle")
            {
                return horizontal;
            }

            if (horizontal == "center")
            {
                return vertical;
            }

            return vertical + " " + horizontal;
        }

        private static string InferHorizontalPosition(float x)
        {
            if (float.IsNaN(x) || float.IsInfinity(x))
            {
                return "camera view";
            }

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

        private static string InferVerticalPosition(float y)
        {
            if (float.IsNaN(y) || float.IsInfinity(y))
            {
                return "camera view";
            }

            if (y >= 0f && y <= 1f)
            {
                if (y < 0.33f)
                {
                    return "top";
                }

                if (y > 0.66f)
                {
                    return "bottom";
                }

                return "middle";
            }

            return "camera view";
        }

        private static string InferSize(BoxData detection, int imageWidth, int imageHeight)
        {
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                return "unknown";
            }

            float area = Mathf.Abs((detection.scale.x - detection.position.x)
                * (detection.scale.y - detection.position.y) / (imageWidth * (float)imageHeight));
            if (area >= 0.16f)
            {
                return "large";
            }

            if (area >= 0.05f)
            {
                return "medium";
            }

            return "small";
        }
    }
}
