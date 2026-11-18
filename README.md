# Quest Vision Assistant

Unity 6 mixed reality object assistant for Meta Quest 3. The existing Meta XR camera block runs a bundled YOLO12n model through Unity Sentis. Current detections are converted into class counts, screen relative positions, and cautious color estimates. Direct visual questions are answered locally; organization requests can use Google AI through a small PC gateway. ElevenLabs handles speech recognition and synthesis through the same gateway.

## Requirements

- Unity **6000.3.19f1** with Android build support
- Meta Quest 3 with camera and microphone permissions
- Python 3 on a PC reachable from the headset
- Google AI Studio and ElevenLabs keys, stored in the PC process environment

The active scene is `Assets/Scenes/SampleScene.unity`. The model and Meta XR profiles are in `Assets/MetaXR/`, runtime logic in `Assets/Scripts/`, Editor tests in `Assets/Tests/Editor/`, and gateway code in `tools/`.

## Run

1. Open the project in Unity and confirm `SampleScene` is enabled in Build Settings.
2. Put `GEMINI_API_KEY=...` and `ELEVENLABS_API_KEY=...` in the Git-ignored `global.env` file. Load them into the current PowerShell process without displaying their values:

   ```powershell
   Get-Content global.env | Where-Object { $_ -match '^[A-Z_]+=' } | ForEach-Object { $key, $value = $_ -split '=', 2; [Environment]::SetEnvironmentVariable($key, $value, 'Process') }
   ```

3. For USB testing, run `adb reverse tcp:8765 tcp:8765`, then `python tools/assistant_gateway.py --host 127.0.0.1`. The scene's `localhost:8765` endpoints work through this USB route. For wireless testing, start the gateway with `--host <PC_LAN_IP> --allow-client <QUEST_IP>` and set the AI and speech endpoints in the scene Inspector to the PC LAN address on port `8765`.
4. Build with **Build > Codex > Build Quest APK** and install the APK on Quest. The default output is `Builds/ObjectDetection-Quest.apk`.
5. In the headset, press **left X** to ask a spoken question or **left Y** for a visible-object inventory.

The gateway also supports OpenRouter or Grok when their respective PC environment keys and a compatible model are configured. The current demonstration path is Google AI Studio.

## Verify

```powershell
python -m unittest discover -s tools -p test_assistant_gateway.py
<Unity> -batchmode -projectPath . -runTests -testPlatform EditMode -testResults <absolute-path-to-results.xml>
<Unity> -batchmode -projectPath . -executeMethod CodexAndroidBuilder.BuildQuestApk -quit -logFile -
```

Replace `<Unity>` with the Unity Editor executable path. Run Edit Mode tests with graphics enabled because the model smoke test needs a compute shader. Saved verification evidence and the IEEE paper are in `paper/`. The final run passed 36 Unity Edit Mode and six gateway tests. A Quest 3 check on November 18 confirmed live labels, text, and spoken responses to left X and Y. Detector accuracy, frame rate, memory, and power were not measured in a controlled trial.

## Security

The gateway reads keys from its PC environment. The public repository contains no current gateway keys. An older ElevenLabs key was previously stored in a Unity profile and remains in Git history; revoke it in the ElevenLabs dashboard. Removing a key from the current version does not remove it from Git history.
