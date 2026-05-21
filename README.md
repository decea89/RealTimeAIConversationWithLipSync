# VR Conversational Avatar

Unity project for a VR conversational avatar with STT, chat, TTS, lip sync, latency telemetry, and a world-space debug panel. The goal is to keep it portfolio-ready: clear architecture, observable flow, and credentials out of the code.

## What It Does

The user speaks with push-to-talk or types text. The system transcribes audio, queries the chat backend, synthesizes speech, and plays the response on the avatar. During the flow it records timings, playback state, and errors that are useful for debugging.

## Architecture

The conversation layer is decoupled through contracts:

- `ISTTService` for transcription.
- `IChatService` for response generation.
- `ITTSService` for batch TTS.
- `IStreamingTTSService` for progressive playback.
- `IInterruptibleTTSService` for cancelling active generations.

The main orchestrator is `OpenAIConversationController`. In the scene, the active path uses `OpenAISTTClient`, `OpenAIChatClient`, `RealtimeOpenAITTSClient`, and the streaming clients when assigned. `StreamingElevenLabsTTSClient` and `StreamingOpenAITTSClient` remain as alternative implementations for comparison testing.

Editable conversation settings live in a single shared asset: `Assets/Resources/ConversationSettings.asset`. Chat, STT, TTS, buffer, telemetry, and debug parameters are centralized there so the scene only keeps wiring references.

## Script Structure

The codebase is organized as follows:

- `Assets/Scripts/Voice`: microphone capture and audio utilities.
- `Assets/Scripts`: contracts, orchestrators, chat/STT/TTS clients, and telemetry.
- `Assets/Scripts/Infrastructure`: cross-cutting utilities such as safe API key resolution.
- `Assets/Oculus/LipSync`: Oculus Lip Sync integration.

## Secrets

There are no real keys in the repository. Clients read local environment variables and do not store secrets in scenes or prefabs:

- `OPENAI_API_KEY`
- `ELEVENLABS_API_KEY`

This avoids exposing credentials in the project and keeps the repo clean for portfolio use.

## Main Scenes

- `Assets/Scenes/FranciscoVR.unity`: main project scene.
- `Assets/Scenes/SampleScene.unity`: support or test scene.

## Usage Flow

1. Configure the environment variables or fill the local private API key fields.
2. Open `FranciscoVR.unity`.
3. Adjust `Assets/Resources/ConversationSettings.asset` if you want to change conversation behavior.
4. Assign the active controller services in the inspector.
5. Use push-to-talk and check the world-space debug panel for state, timings, and errors.

## Quality Notes

- Communication between layers uses interfaces, not direct dependencies on concrete implementations.
- Telemetry and streaming logs help diagnose latency, gaps, and HTTP failures.
- Several old prototypes and duplicates were removed to leave only the current execution path.

## Current State

The main path is ready for testing in the Editor and on Quest, with an emphasis on audio stability, observability, and credentials outside the code.
