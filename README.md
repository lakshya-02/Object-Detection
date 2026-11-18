# Quest Vision Assistant

Unity 6 mixed reality object assistant for Meta Quest 3. The existing Meta XR camera block runs a bundled YOLO12n model through Unity Sentis. Current detections are converted into class counts, screen relative positions, and cautious color estimates. Direct visual questions are answered locally; organization requests can use Google AI through a small PC gateway. ElevenLabs handles speech recognition and synthesis through the same gateway.

## Requirements

- Unity **6000.3.19f1** with Android build support
- Meta Quest 3 with camera and microphone permissions
- Python 3 on a PC reachable from the headset
- Google AI Studio and ElevenLabs keys, stored only in the PC process environment

The active scene is `Assets/Scenes/SampleScene.unity`. The model and Meta XR profiles are in `Assets/MetaXR/`, runtime logic in `Assets/Scripts/`, Editor tests in `Assets/Tests/Editor/`, and gateway code in `tools/`.

## Run

1. Open the project in Unity and confirm `SampleScene` is enabled in Build Settings.
2. Set `GEMINI_API_KEY` and `ELEVENLABS_API_KEY` in the PC terminal environment. Never add them to Unity assets or Git.
3. Start the gateway with `python tools/assistant_gateway.py --host <PC_LAN_IP> --allow-client <QUEST_IP>`.
4. In the scene Inspector, set the AI gateway and speech profile endpoints to the PC's LAN address on port `8765`. The committed `localhost` values are for local testing; `localhost` on Quest means the headset.
5. Build with **Build > Codex > Build Quest APK** and install the APK on Quest. The default output is `Builds/ObjectDetection-Quest.apk`.
6. In the headset, press **left X** to ask a spoken question or **left Y** for a visible-object inventory.

The gateway also supports OpenRouter or Grok when their respective PC environment keys and a compatible model are configured. The current demonstration path is Google AI Studio.

## Verify

```powershell
python -m unittest discover -s tools -p test_assistant_gateway.py
<Unity> -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults.xml -quit
<Unity> -batchmode -projectPath . -executeMethod CodexAndroidBuilder.BuildQuestApk -quit -logFile -
```

Replace `<Unity>` with the Unity Editor executable path. Saved verification evidence and the IEEE paper are in `paper/`. The documented run passed 36 Unity Edit Mode and six gateway tests. Manual Quest checks confirmed live labels and selected spoken inventory and count answers. Detector accuracy, frame rate, memory, and power were not measured in a controlled trial.

## Security

The gateway reads keys from its PC environment. Keep the repository private or grant reviewers access for assessment. Rotate any credentials that were previously stored in Unity profile assets; removing a key from the current version does not remove it from Git history.
