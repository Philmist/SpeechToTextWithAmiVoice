# AGENTS.md

## Conversation Rule
- Always communicate with the user in Japanese.

## Top-Level Policy
- Prioritize satisfying the actual user need with the minimum necessary implementation.

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
- Implementing unsupported platforms before the core user need works on target platform(s).

## Refactoring Direction
- For major cleanup, prioritize a full app-level refactor path.
- Redesign unstable components (for example `VoiceRecognizerWithAmiVoiceCloud`) with clear boundaries, even if that requires breaking old interfaces.

## Project Structure
- `SpeechToTextWithAmiVoice/`: Avalonia implementation.
- `SpeechToText.Core/`: shared domain/application logic used by both projects.

## Current Canonical Architecture
- `SpeechToText.Core/VoiceRecognizerWithAmiVoiceCloud.cs` is the public recognizer facade.
- `SpeechToText.Core/AmiVoiceSession.cs` owns session loop, reconnect behavior, and `TryFeedRawWave` ingestion.
- Audio input to recognizer is unified to `TryFeedRawWave` (raw PCM16 bytes).
- Typed state/diagnostic events are canonical:
  - `StateChanged`
  - `Disconnected` (`DisconnectionReason`, `DisconnectInfo`)
  - `AudioFeedStatsChanged` (`AudioFeedStats`, including dropped backpressure count)

## Current UI Policy
- Main screen keeps only runtime-tweakable options:
  - Engine
  - Profile ID
  - Filler handling
  - Audio interface selection
  - Enable switches for HTTP POST and Bouyomi-chan
- Fixed/rarely changed connection settings live in Settings Page:
  - WebSocket URI
  - API key
  - HTTP POST URI
  - Bouyomi host/port/prefix/voice

## Important Technical Assumptions
- Audio format for AmiVoice streaming: 16kHz / 16bit / mono PCM.
- AmiVoice connection payload must include `authorization`.
- Keep platform-dependent audio capture behind abstractions (`IAudioCaptureService`, `IAudioCaptureServiceFactory`).
- Windows audio capture is currently implemented via NAudio-backed WASAPI factory.

## Testing Baseline
- `SpeechToText.Core.Tests` is the regression baseline for core behavior.
- Use xUnit + FluentAssertions + NSubstitute.
- Existing core tests (including reconnect/audio-feed behavior) must stay passing when refactoring.

## Context To Drop In Future Sessions
- Do not revert to monolithic `MainPageViewModel` with direct infra handling.
