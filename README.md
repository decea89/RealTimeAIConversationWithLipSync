# VR Conversational Avatar – Runtime Pipeline

Este repositorio contiene el pipeline actual de conversación para un avatar VR en Unity: entrada por voz o texto, STT, chat/backend, TTS segmentado, reproducción en el avatar, emociones, lipsync, métricas de latencia y gestión de sesiones anónimas.

## Estado actual

El proyecto funciona con un flujo completo de conversación: captura de audio, transcripción STT, petición de chat, síntesis TTS y reproducción en el avatar. El orquestador principal es `OpenAIConversationController`, que coordina el turno completo de principio a fin.

La arquitectura actual ya soporta TTS pseudo realtime por segmentos, panel de debug world-space, cambio de usuario por sesión anónima y desacoplamiento por interfaces (`ISTTService`, `IChatService`, `ITTSService`, `IStreamingTTSService`).

## Flujo por turno

1. El usuario habla con push-to-talk o escribe texto en una entrada de debug.
2. Si el input es voz, se captura con `MicrophoneRecorder`, se puede recortar con `AudioTrimmingUtility` y se serializa a WAV mediante `WavUtility`.
3. El audio WAV se envía al servicio STT (`ISTTService`), normalmente `OpenAISTTClient`.
4. El texto resultante se envía al servicio de chat (`IChatService`), que puede ser `CharacterBackendChatClient` o `OpenAIChatClient`.
5. La respuesta del asistente se sintetiza por TTS y se reproduce en el `AudioSource` del avatar.
6. El sistema actualiza emoción, transcript, métricas y backend info en el panel de debug world-space.

## Orquestador principal

`OpenAIConversationController` es el punto central de orquestación. Resuelve dependencias de chat, STT y TTS; gestiona push-to-talk con teclado debug o Input System XR; crea nuevas sesiones; dispara los caminos de voz o texto; y mide tiempos de STT, chat, TTS y reproducción.

Actualmente el controller maneja dos caminos principales:

- `RunTextConversation(string userText)` para entradas de texto.
- `RunVoiceConversationFromClip(AudioClip clip)` para entradas por micrófono.

Ambos flujos convergen en `PlayAssistantReplyWithTts(ConversationResult result)`, que decide entre TTS segmentado/streaming o TTS batch según la implementación disponible.

## Entrada de voz

La entrada de voz se captura con `MicrophoneRecorder`, que inicia y detiene la grabación asociada al gesto de push-to-talk.

Después de grabar, el audio puede pasar por `AudioTrimmingUtility.TrimSilence(...)` para reducir silencio inicial y final antes de serializarlo con `WavUtility.FromAudioClip(...)`. Esta parte del pipeline ayuda con la latencia, aunque también es sensible a thresholds demasiado agresivos si el audio viene bajo o con ruido.

## STT

La interfaz `ISTTService` abstrae la transcripción. La implementación actual relevante es `OpenAISTTClient`, que construye manualmente la request multipart, envía el WAV a `POST /v1/audio/transcriptions` y parsea la respuesta JSON del modelo de transcripción.

En la versión actual, el uso de `response_format = "json"` es la opción recomendada para depuración, observabilidad y validación del contenido devuelto por STT.

## Capa de chat / backend

La capa de chat se abstrae con `IChatService`. Hay dos implementaciones principales en el proyecto:

- `CharacterBackendChatClient`, pensado para el contrato del backend del cliente.
- `OpenAIChatClient`, útil para debug o pruebas directas.

`RequestChatRich(...)` devuelve un `ChatServiceResult` con texto del asistente, emoción, intent tags y metadatos como latencia, modelo y títulos de fuentes. `OpenAIConversationController` usa esos datos para actualizar emoción, transcript y panel de debug.

## TTS actual

El proyecto ya no depende solo de un TTS batch completo; la pieza principal para mejorar la fluidez es `SegmentedBufferedTTSClient`, que implementa `IStreamingTTSService` sobre un TTS interno `ITTSService`.

El comportamiento actual es:

- divide la respuesta en segmentos de texto,
- solicita TTS para cada segmento al servicio interno,
- reproduce el primer segmento en cuanto está listo,
- solicita el siguiente en paralelo para encadenarlo con una transición corta.

Esto reduce la latencia percibida frente a pedir una sola respuesta larga, aunque todavía puede generar pausas audibles entre segmentos y aún necesita una capa mejor de cancelación/interrupción.

## Servicios e interfaces

Las interfaces principales del runtime viven en `ConversationContracts.cs`:

- `ISTTService`: transcripción de audio a texto.
- `IChatService`: chat simple o enriquecido.
- `ITTSService`: síntesis batch que devuelve un `AudioClip`.
- `IStreamingTTSService`: síntesis/orquestación de reproducción progresiva sobre un `AudioSource`.
- `IConversationResettable`: reseteo opcional de contexto conversacional.

Esta separación permite cambiar proveedores o estrategias internas sin romper el controller principal.

## Métricas y estado conversacional

`ConversationTiming` y `ConversationResult` almacenan tiempos de STT, chat, TTS, tiempo hasta primer audio, duración de playback y tiempo total del turno, además de metadatos del backend como modelo, latencia y número de resultados RAG.

`OpenAIConversationController.LogTiming(...)` vuelca esos datos al log y al panel world-space.

## Panel de debug world-space

`WorldSpaceDebugPanelController` muestra estado, sesión, transcript del usuario, transcript del asistente, métricas, backend info y una ventana de logs de Unity. También expone controles de runtime para el TTS segmentado, como `transitionPaddingSeconds`, `maxWaitForNextSegmentSeconds`, `maxSegmentChars` y logging de segmentos.

Este panel es actualmente la herramienta principal para iterar en Quest/standalone sin depender del inspector del Editor.

## Gestión de sesiones anónimas

El flujo mantiene sesiones anónimas basadas en `session_id` para separar el contexto cuando el visor pasa de un usuario a otro. `OpenAIConversationController.StartNewAnonymousUserSession()` detiene la conversación actual, para el audio del avatar, resetea contexto si el chat service lo soporta y arranca una sesión nueva en `SessionManager`.

## Limitaciones actuales

Aunque el pipeline principal ya funciona, todavía hay tres limitaciones importantes:

- el avatar no soporta barge-in real al pulsar PTT mientras está hablando, porque el controller bloquea nuevas grabaciones mientras `isRunningConversation` está activo,
- el TTS segmentado no tiene todavía un mecanismo explícito de invalidación o cancelación de segmentos ya pedidos,
- pueden aparecer silencios perceptibles entre segmentos según longitud del texto, tiempos de red o disponibilidad del siguiente clip.

## Próximo refactor conservador

La siguiente iteración recomendada es un refactor conservador del orquestador y del TTS segmentado para añadir:

- barge-in al pulsar PTT durante la reproducción del avatar,
- cancelación real del playback y de colas/segmentos pendientes,
- invalidación por generación o `turnId` para evitar que audio viejo “resucite”,
- mejor separación entre estado de procesamiento y estado de speaking en el controller.

Este refactor mantiene la arquitectura actual y prepara el proyecto para una futura rama más ambiciosa orientada a realtime voice end-to-end si más adelante se considera necesaria.