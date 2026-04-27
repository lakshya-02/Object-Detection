# Object Detection + Ollama Workspace Organizer

This project is a Unity XR prototype that combines:

- Meta XR object detection
- A local Ollama server
- A `llama3.2` model
- A TextMesh Pro text box for short arrangement instructions

The current goal is:

1. Detect objects in view
2. Convert detections into JSON
3. Send that JSON to Ollama
4. Get short placement instructions back from `llama3.2`
5. Show the final instructions in a text box

## Current Flow

At runtime, pressing **A** does this:

1. `OllamaWorkspaceOrganizer` listens for:
   - Quest right-controller **A**
   - keyboard **A** for editor/PC testing
2. On button press, it requests detections from Meta `ObjectDetectionAgent`
3. The returned detections are converted into scene JSON
4. That JSON is sent to Ollama at `/api/generate`
5. Ollama returns JSON with:
   - `summary`
   - `steps`
   - `speak`
6. The UI displays only the short `steps`

## Speech-To-Speech Flow

There is now a second, additive pipeline for voice interaction:

```text
Meta XR speech-to-text input
-> transcript
-> fresh object-detection scene JSON
-> Ollama
-> structured recommendation
-> Meta TTS speech output using the ElevenLabs provider
```

This voice path does not replace the existing Sentis-based object detection flow.
It reuses the same `ObjectDetectionAgent` by subscribing to one detection event when
it needs scene context, then hands that scene JSON to Ollama.

Current scene wiring:

- `manager` has the voice assistant managers attached.
- `SpeechInputManager` is set to `MetaSpeechToTextAgent`.
- `VoiceAssistantOrchestrator` requests a fresh object-detection snapshot for each spoken prompt.
- `OllamaSpeechPlanner` sends the detected scene plus your spoken custom prompt to `llama3.2`.
- `SpeechOutputManager` speaks the `speak` response through the active Meta `TextToSpeechAgent`.
- Press Quest right-controller **B** to start listening in a build.
- Press keyboard **V** to start listening during editor/PC testing.

The scene still needs a Meta XR speech-to-text block or `SpeechToTextAgent` present.
When that block is present, `SpeechInputManager` auto-finds it and subscribes to its
transcript event. The existing text-to-speech block is already referenced for spoken output.

### New Voice Scripts

- [Assets/Scripts/VoiceAssistantModels.cs](</c:/Users/Lakshya/Object Detection/Assets/Scripts/VoiceAssistantModels.cs>)
  - Shared serializable models for scene data, Ollama payloads, and recommendations

- [Assets/Scripts/SpeechInputManager.cs](</c:/Users/Lakshya/Object Detection/Assets/Scripts/SpeechInputManager.cs>)
  - Accepts transcript input from `AppDictationExperience`, `SpeechToTextAgent`, or manual debug text

- [Assets/Scripts/SceneContextBuilder.cs](</c:/Users/Lakshya/Object Detection/Assets/Scripts/SceneContextBuilder.cs>)
  - Builds scene JSON from `BoxData`
  - Builds the final grounded Ollama prompt

- [Assets/Scripts/OllamaSpeechPlanner.cs](</c:/Users/Lakshya/Object Detection/Assets/Scripts/OllamaSpeechPlanner.cs>)
  - Sends `UnityWebRequest` POST calls to Ollama
  - Requests `format = "json"` and `stream = false`
  - Parses and validates the structured model output

- [Assets/Scripts/SpeechOutputManager.cs](</c:/Users/Lakshya/Object Detection/Assets/Scripts/SpeechOutputManager.cs>)
  - Speaks `recommendation.speak`
  - Works directly with Meta `TextToSpeechAgent`
  - Can also fall back to any component with `Speak(string)` or `SpeakText(string)`

- [Assets/Scripts/VoiceAssistantOrchestrator.cs](</c:/Users/Lakshya/Object Detection/Assets/Scripts/VoiceAssistantOrchestrator.cs>)
  - Main glue script
  - Tracks `Idle`, `Listening`, `Thinking`, `Speaking`, and `Error`
  - Prevents overlapping requests

### Suggested Voice Hierarchy

Create one GameObject, for example `Voice Assistant`, and add:

1. `SpeechInputManager`
2. `SceneContextBuilder`
3. `OllamaSpeechPlanner`
4. `SpeechOutputManager`
5. `VoiceAssistantOrchestrator`

Recommended references:

- `SpeechInputManager`
  - assign `AppDictationExperience` if using Meta dictation
  - or assign `SpeechToTextAgent` if using the Meta speech-to-text AI block

- `OllamaSpeechPlanner`
  - assign `SceneContextBuilder`
  - model name: `llama3.2`
  - endpoint: `http://localhost:11434/api/generate` in editor or same-machine testing

- `SpeechOutputManager`
  - assign Meta `TextToSpeechAgent`

- `VoiceAssistantOrchestrator`
  - assign all four managers above
  - assign the existing `ObjectDetectionAgent`

### Important Quest Network Note

The speech planner defaults to:

```text
http://localhost:11434/api/generate
```

That is correct for editor or same-machine testing.

For a Quest build talking to Ollama on your PC, `localhost` means the headset itself,
so you must override the endpoint in the Inspector with your PC LAN IP, for example:

```text
http://192.168.0.179:11434/api/generate
```

### Debug Without Live Speech

You can test the whole voice pipeline without live STT:

1. Select the object with `SpeechInputManager`
2. Enter text in `Debug Transcript`
3. Use the component context menu:

```text
Speech Input/Submit Debug Transcript
```

Or trigger the full flow from `VoiceAssistantOrchestrator`:

```text
Voice Assistant/Run Debug Transcript
```

### UI Text Output

`RecommendationTextOutput` now supports both:

- `OllamaWorkspaceOrganizer`
- `VoiceAssistantOrchestrator`

So the same TMP text box can display:

- detection/Ollama status
- speech-orchestrator status
- final short recommendation steps

## Main Scripts

- [Assets/Scripts/OllamaWorkspaceOrganizer.cs](</c:/Users/Lakshya/Object Detection/Assets/Scripts/OllamaWorkspaceOrganizer.cs>)
  - Handles A-button input
  - Calls object detection
  - Builds JSON from detections
  - Sends prompt to Ollama
  - Parses Ollama response

- [Assets/Scripts/RecommendationTextOutput.cs](</c:/Users/Lakshya/Object Detection/Assets/Scripts/RecommendationTextOutput.cs>)
  - Receives organizer events
  - Updates TMP text
  - Displays only short steps
  - Applies smaller font settings

## Scene Setup

The active scene is:

- `Assets/Scenes/SampleScene.unity`

Current important scene state:

- `ObjectDetectionAgent` exists in scene
- Object detection provider is assigned
- `detectEveryNFrames = 1`
- `minConfidence = 0.5`
- `OllamaWorkspaceOrganizer` is present on `manager`
- `RecommendationTextOutput` is wired to the TMP text
- `Prompt Template` is currently empty in the scene so the script default prompt is used

## Ollama Setup

The project expects a local Ollama server on the PC.

Current working endpoint:

```text
http://192.168.0.179:11434/api/generate
```

Expected model:

```text
llama3.2
```

Current provider asset host:

- `Assets/MetaXR/LLM.asset`

It should point to:

```text
http://192.168.0.179:11434
```

## What A Button Does Right Now

Pressing **A** now calls:

```csharp
RequestArrangementFromDetectedObjects()
```

That means it does **not** use the old fake notebook/chair/bottle test scene anymore.

Instead it:

- waits for real object detections from Meta object detection
- builds scene JSON from `BoxData`
- sends those detected labels to Ollama

## Current Detection JSON Shape

Detections are converted into JSON like this:

```json
{
  "objects": [
    {
      "name": "cup",
      "rawLabel": "cup 0.91",
      "zone": "camera view",
      "confidence": 0.91,
      "detectionIndex": 0,
      "box": {
        "x": 0.123,
        "y": 0.456,
        "width": 0.111,
        "height": 0.222
      }
    }
  ],
  "zones": ["camera view"],
  "source": "Meta ObjectDetectionAgent"
}
```

## Prompt Behavior

The built-in prompt is designed to keep Ollama constrained:

- use only detected object names
- do not invent extra objects
- do not invent zones
- return short steps only
- keep steps suitable for a text box

Expected Ollama response shape:

```json
{
  "summary": "very short plan",
  "steps": ["short action 1", "short action 2", "short action 3"],
  "speak": "one short instruction sentence"
}
```

## UI Behavior

The text output currently:

- shows `Sending request to Ollama...` or `Detecting objects...` while working
- shows `LLM Error: ...` when something fails
- shows only short instruction steps on success

Current text settings:

- smaller font size
- word wrapping enabled
- ellipsis overflow

## Known Limitations

This is the current situation, honestly:

1. Ollama connectivity works from the PC side.
2. Unity code compiles cleanly.
3. The UI update path works.
4. The detector exists in the scene.
5. The remaining real-world dependency is whether the Quest build actually returns live detections at runtime.

So the full chain is implemented, but the real build still depends on:

- passthrough being active
- object detection running on device
- visible objects being in frame
- Quest being able to reach the PC Ollama server over network

## Expected Runtime Messages

### Success path

```text
Detecting objects...
Sending request to Ollama...
<short placement instructions>
```

### If no detections arrive

```text
LLM Error:
No object detections were received before timeout...
```

### If zero objects are detected

```text
LLM Error:
Object detector returned zero objects...
```

### If Ollama is unreachable

```text
LLM Error:
Connection refused. Is Ollama running and reachable at ...
```

## How To Test

1. Start Ollama on the PC
2. Make sure `llama3.2` is available
3. Make sure Quest and PC are on the same network
4. Build and run the app
5. Look at real objects in passthrough
6. Press **A**
7. Watch the text box update

## Useful Manual Checks

Check Ollama tags:

```powershell
Invoke-RestMethod http://192.168.0.179:11434/api/tags
```

Quick model test:

```powershell
ollama run llama3.2 "Say hello"
```

## Current Status

As of now:

- fake hardcoded detection scenes have been removed from the A-button path
- A is meant to use real Meta object detections
- Ollama response is meant to be shown as short text instructions

If the final build behavior is:

```text
Press A -> detect objects -> send to Ollama -> show short instructions
```

then the target behavior is achieved.
