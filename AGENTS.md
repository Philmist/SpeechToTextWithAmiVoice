# AGENTS.md

## Conversation Rule
- Always communicate with the user in Japanese.

## Top-Level Policy
- Do not prioritize a "complete migration" by itself.
- Prioritize satisfying the actual user need with the minimum necessary implementation.
- Parallel development of MAUI and Avalonia is currently required.
- For shared changes, prefer implementations that keep both frontends functional unless explicitly waived.

## Canonical User Need (Shared Across Sessions)
- Capture microphone audio input.
- Send audio to AmiVoice Cloud via WebSocket API.
- Receive recognition results in real time.
- If configured, send recognized text to a specified URI via HTTP POST.
- If configured, send recognized text via normal socket communication to Bouyomi-chan so it is spoken by speech synthesis.

## Success Criteria (Definition of Done)
- The end-to-end flow above works reliably.
- Optional outputs (HTTP POST and Bouyomi-chan) are only required when enabled/configured.
- This app-level user need must remain satisfiable on the actively shipped frontend(s), currently including Avalonia.

## Non-Goals (Unless Explicitly Requested)
- Perfect one-to-one UI parity with Avalonia.
- Porting all legacy settings screens and dormant features.
- Implementing unsupported platforms before the core user need works on target platform(s).
- Preserving every legacy API/class shape when it blocks progress.

## Refactoring Direction
- For major cleanup, prioritize a full app-level refactor path.
- Compatibility layers between MAUI and Avalonia are acceptable when they reduce duplicated logic and keep both apps operational.
- Redesign unstable components (for example `VoiceRecognizerWithAmiVoiceCloud`) with clear boundaries, even if that requires breaking old interfaces.

## Project Structure
- `SpeechToTextWithAmiVoice/`: Avalonia implementation (parallel development target).
- `SpeechToTextAmiVoiceMAUI/`: MAUI implementation (parallel development target).
- `SpeechToText.Core/`: shared domain/application logic used by both projects.

## Current Delivery Constraint
- MAUI self-contained single-file publish (`PublishSingleFile`) is currently unreliable in this project context.
- Until this constraint is resolved, Avalonia must continue to be developed and kept releasable in parallel.

## Current Canonical Architecture (MAUI + Core)
- `SpeechToText.Core/VoiceRecognizerWithAmiVoiceCloud.cs` is the public recognizer facade.
- `SpeechToText.Core/AmiVoiceSession.cs` owns session loop, reconnect behavior, and `TryFeedRawWave` ingestion.
- Audio input to recognizer is unified to `TryFeedRawWave` (raw PCM16 bytes).
- Typed state/diagnostic events are canonical:
  - `StateChanged`
  - `Disconnected` (`DisconnectionReason`, `DisconnectInfo`)
  - `AudioFeedStatsChanged` (`AudioFeedStats`, including dropped backpressure count)

## Current MAUI UI Policy
- Main screen (`MainPage`) keeps only runtime-tweakable options:
  - Engine
  - Profile ID
  - Filler handling
  - Audio interface selection
  - Enable switches for HTTP POST and Bouyomi-chan
- Fixed/rarely changed connection settings live in `SettingsPage`:
  - WebSocket URI
  - API key
  - HTTP POST URI
  - Bouyomi host/port/prefix/voice
- `MainPage` opens `SettingsPage` via toolbar navigation.

## MAUI Service Boundaries (Do Not Collapse Back)
- `SpeechToTextAmiVoiceMAUI/Services/SettingsStore.cs`
  - Single source of truth for `Preferences` read/write.
  - Uses `ConnectionSettings` and `RuntimeOptions`.
- `SpeechToTextAmiVoiceMAUI/Services/RecognitionSessionCoordinator.cs`
  - Owns recognizer/capture lifecycle and session events.
- `SpeechToTextAmiVoiceMAUI/Services/RecognitionResultDispatcher.cs`
  - Owns optional HTTP POST and Bouyomi dispatch.
- `SpeechToTextAmiVoiceMAUI/ViewModels/MainPageViewModel.cs`
  - Should remain a thin UI-state orchestrator that delegates to services.

## Persistent Settings Model
- Connection-side settings: `ConnectionSettings`.
- Runtime-side settings: `RuntimeOptions`.
- Avoid direct `Preferences.*` calls from view models; use `ISettingsStore`.

## Important Technical Assumptions
- Audio format for AmiVoice streaming: 16kHz / 16bit / mono PCM.
- AmiVoice connection payload must include `authorization`.
- Keep platform-dependent audio capture behind abstractions (`IAudioCaptureService`, `IAudioCaptureServiceFactory`).
- Windows MAUI audio capture is currently implemented via NAudio-backed WASAPI factory.

## Testing Baseline
- `SpeechToText.Core.Tests` is the regression baseline for core behavior.
- Use xUnit + FluentAssertions + NSubstitute.
- Existing core tests (including reconnect/audio-feed behavior) must stay passing when refactoring.

## Context To Drop In Future Sessions
- Do not revert to monolithic `MainPageViewModel` with direct infra handling.
- Do not reintroduce direct `Preferences` access in multiple UI layers.
- Do not assume "MAUI-only deliverable" while the MAUI single-file publish issue remains unresolved.
