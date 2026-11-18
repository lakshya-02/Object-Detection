# Repository Guidelines

## Project Structure & Module Organization

This is a Unity 6 (`6000.3.19f1`) XR project. Runtime C# code lives in `Assets/Scripts/`; keep one primary `MonoBehaviour` or data model per file and match the filename to its main type. The active scene is `Assets/Scenes/SampleScene.unity`. Meta XR profiles and the Sentis model are under `Assets/MetaXR/`, while render-pipeline settings are in `Assets/Settings/`. Editor-only automation belongs in `Assets/Editor/`. Package versions and project-wide Unity settings are tracked in `Packages/` and `ProjectSettings/`. Do not edit generated `Library/`, `Temp/`, `Logs/`, or IDE project files.

## Build, Test, and Development Commands

Open the repository root in Unity Hub with Unity `6000.3.19f1`. In the Editor, use **Build > Codex > Build Quest APK**; the default output is `Builds/ObjectDetection-Quest.apk`.

For automation, replace `<Unity>` with the Unity Editor executable:

```powershell
<Unity> -batchmode -projectPath . -executeMethod CodexAndroidBuilder.BuildQuestApk -quit -logFile -
<Unity> -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults.xml -quit
```

Set `CODEX_BUILD_PATH` to override the APK destination. Start Ollama and verify `llama3.2` is installed before exercising recommendation flows.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, braces on new lines, `PascalCase` for types/methods/properties, and `camelCase` for locals and private fields. Use `[SerializeField] private` for Inspector state rather than public fields. Keep editor APIs inside `Assets/Editor/`. Preserve Unity `.meta` files when moving or adding assets. No repository formatter or linter is configured; use IDE formatting and keep diffs focused.

## Testing Guidelines

The Unity Test Framework is installed, but this checkout has no project-owned tests. Add Edit Mode or Play Mode tests under `Assets/Tests/`, using names such as `SceneContextBuilderTests.cs`. Test JSON parsing and prompt construction without network access; reserve Quest, passthrough, controller, speech, and Ollama checks for documented manual testing.

## Commit & Pull Request Guidelines

Git history is not included in this checkout, so no existing commit convention can be inferred. Use short, imperative subjects (for example, `Validate Ollama response shape`) and keep commits single-purpose. Pull requests should describe behavior changes, list Unity and Quest test results, link relevant issues, and include screenshots or video for scene/UI changes. Call out scene, prefab, package, endpoint, or model-profile changes explicitly.

## Security & Configuration

Do not commit API keys, provider credentials, machine-specific LAN addresses, or generated builds. Configure the Ollama endpoint in the Inspector for the target network; remember that `localhost` on Quest refers to the headset.
