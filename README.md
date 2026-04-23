# NPC histórico con voz para Unity VR

Este repositorio contiene un prototipo de conversación por voz para un NPC histórico en VR con Unity. El proyecto soporta actualmente **push-to-talk**, transcripción de voz a texto (STT), generación de respuesta con un modelo de lenguaje, síntesis de voz (TTS), cambios de emoción en el avatar e instrumentación de latencia para cada turno de conversación.

## Visión general

La implementación actual usa un **pipeline de voz por turnos**, no conversación full-duplex en tiempo real.

El flujo es:

1. La persona jugadora mantiene pulsada una tecla para hablar.
2. El sistema graba el audio del micrófono.
3. Al soltar la tecla, el audio se envía para:
   - transcribir,
   - generar respuesta,
   - sintetizar la voz del NPC.
4. La respuesta se reproduce en el avatar dentro de la escena.

La arquitectura es modular: chat, STT y TTS están abstraídos detrás de interfaces, de forma que el controlador puede trabajar tanto con clientes directos a OpenAI como con un backend propio sin cambiar la lógica de la escena.

## Arquitectura actual

### Flujo principal en runtime

El orquestador central es `OpenAIConversationController`. Se encarga de:

- gestionar el push-to-talk por teclado,
- manejar la entrada de texto de depuración,
- coordinar el flujo de voz,
- reproducir el TTS,
- registrar métricas de tiempo,
- y enviar información de debug a la UI y a la consola de Unity.

El flujo completo de voz es:

1. `BeginPushToTalk()` inicia la grabación de micrófono a través de `MicrophoneRecorder`.
2. `EndPushToTalkAndSend()` detiene la grabación, comprueba la duración mínima de pulsación y arranca `RunVoiceConversationFromClip()`.
3. El clip grabado se pasa por `AudioTrimmingUtility.TrimSilence(...)` para recortar silencios antes del STT.
4. El clip se convierte a bytes WAV con `WavUtility.FromAudioClip(...)` y se envía al servicio STT.
5. El servicio STT devuelve el texto del usuario.
6. `IChatService.RequestChatRich(...)` genera el texto de respuesta del personaje, junto con emoción e intent tags.
7. `ITTSService.RequestSpeech(...)` sintetiza la respuesta en un `AudioClip`.
8. El clip se asigna a `avatarAudioSource` y se reproduce en el avatar.
9. Al final del turno se registran y muestran las métricas de tiempo.

### Scripts principales

| Script | Responsabilidad |
|---|---|
| `OpenAIConversationController.cs` | Orquestación principal de la conversación, push-to-talk, timing, debug y reproducción de TTS. |
| `MicrophoneRecorder.cs` | Gestión del micrófono (start/stop) y entrega de `AudioClip`. |
| `OpenAISTTClient.cs` | Cliente STT que envía audio WAV para transcripción. |
| `OpenAIChatClient.cs` | Cliente directo de chat con OpenAI que implementa `IChatService`. |
| `CharacterBackendChatClient.cs` | Cliente alternativo hacia un backend propio con emoción e intent tags. |
| `OpenAITTSClient.cs` | Wrapper TTS que solicita audio al endpoint y lo convierte a `AudioClip`. |
| `ConversationContracts.cs` | Interfaces `IChatService`, `ISTTService` y `ITTSService`. |
| `ConversationStateTypes.cs` | Modelos de estado, resultado y tiempos de conversación. |
| `ConversationType.cs` | Enums `CharacterEmotion`, `IntentTag` y `ChatServiceResult`. |
| `ConversationDebugView.cs` | Escritura de estado y mensajes de debug en UI TMP. |
| `AvatarEmotionController.cs` | Aplicación de emociones al avatar. |
| `WavUtility.cs` | Conversión de `AudioClip` a WAV para STT. |

## Abstracción de servicios

El proyecto se apoya en interfaces para desacoplar el controlador de las implementaciones concretas:

- `IChatService`
- `ISTTService`
- `ITTSService`

Gracias a esto, el mismo controlador puede trabajar con:

- clientes directos a OpenAI,
- o un backend de personaje con lógica adicional de contexto, emoción, etiquetas e incluso RAG en el futuro.

## Pipeline de entrada de voz

`MicrophoneRecorder` encapsula la lógica de captura de audio y expone métodos de inicio/fin de grabación que el controlador usa con push-to-talk.

El controlador también aplica un tiempo mínimo de pulsación para evitar llamadas innecesarias por toques accidentales.

Antes del STT se ejecuta:

```csharp
AudioTrimmingUtility.TrimSilence(...)
```

para recortar silencios y reducir audio inútil enviado a transcripción, mejorando la latencia percibida en frases cortas.

Luego el audio se convierte con:

```csharp
byte[] wavBytes = WavUtility.FromAudioClip(clipToSend);
```

## Capa de chat

Existen dos caminos de chat en el repositorio:

### `OpenAIChatClient`

Llama directamente al endpoint de chat de OpenAI con:

- un `systemPrompt`,
- y el mensaje del usuario.

Devuelve solo texto y usa emoción neutra por defecto.

### `CharacterBackendChatClient`

Envía la petición a un backend propio que puede devolver:

- `response_text`
- `emotion`
- `intent_tags`
- metadatos

Este cliente mapea los valores del backend a los enums locales `CharacterEmotion` e `IntentTag`.

## Capa de TTS

`OpenAITTSClient` envía una petición JSON al endpoint de audio TTS, recibe los bytes del audio y:

1. los escribe en un archivo temporal,
2. los carga de nuevo como `AudioClip`.

Se ha añadido un parámetro configurable en el inspector:

```csharp
[SerializeField] [Range(0.5f, 2.0f)]
private float speed = 1.10f;
```

Esto permite ajustar la velocidad de voz sin tocar código y reducir tanto el tiempo de síntesis como la duración total del turno hablado.

## Métricas de tiempo y diagnóstico

`ConversationTiming` guarda los principales timestamps del turno:

- `requestStartTime`
- `sttStartTime`
- `chatStartTime`
- `ttsStartTime`
- `llmResponseTime`
- `ttsReadyTime`
- `playbackStartTime`
- `turnCompleteTime`

Y expone métricas derivadas como:

- `SttSeconds`
- `ChatSeconds`
- `TtsSeconds`
- `TimeToFirstAudioSeconds`
- `TurnCompleteSeconds`

La salida actual en consola tiene este formato:

```text
STT: 0.65s | Chat: 0.90s | TTS: 3.13s
Time to first audio: 4.68s | Turn complete: 18.25s
AI: ...
```

Esto permite distinguir claramente entre:

- el tiempo hasta que empieza a sonar la respuesta,
- y el tiempo total hasta que el avatar termina de hablar.

## Hooks de avatar y UI

El controlador actualiza `ConversationDebugView` con estados como:

- `Recording`
- `STT`
- `Chat`
- `TTS`
- `Speaking`
- `Idle`
- `Error`

Esto facilita depurar visualmente el pipeline durante pruebas en editor o en dispositivo.

Los metadatos de emoción se pasan a:

```csharp
emotionController?.ApplyEmotion(result.emotion, result.intentTags);
```

que es el punto de integración para blendshapes, animaciones o cambios de expresión del avatar.

## Estado actual del rendimiento

Después de varias rondas de pruebas y optimización, el sistema ha llegado a valores aproximados como:

- **STT:** ~0.6–0.9 s
- **Chat:** ~0.8–1.5 s
- **TTS:** ~2.8–3.6 s
- **Time to first audio:** ~4.6–5.3 s

La parte lenta ya no suele ser el chat, sino:

- el TTS,
- y la duración total de respuestas habladas largas.

## Optimizaciones realizadas hasta ahora

Hasta el momento se ha implementado o ajustado:

- recorte de silencios antes de STT,
- instrumentación completa de tiempos,
- separación entre `Time to first audio` y `Turn complete`,
- reducción del tamaño del prompt,
- limitación de longitud de respuestas,
- pruebas con modelos de chat más ligeros,
- control de `speed` en TTS.

## Limitaciones actuales

El sistema sigue siendo **turn-based**:

- primero se graba,
- luego se transcribe,
- luego se consulta al modelo,
- luego se sintetiza,
- y finalmente se reproduce.

Esto implica varias limitaciones:

- no hay streaming de audio,
- no hay conversación full-duplex,
- no se puede interrumpir al personaje a mitad de frase,
- no hay VAD automático,
- el cliente directo de chat no mantiene aún historial conversacional completo.

## Próximo paso: streaming / Realtime API

El siguiente salto de arquitectura sería migrar hacia un sistema de **streaming / Realtime API**, donde:

- el audio del micrófono se envía en pequeños chunks,
- se mantiene una conexión persistente,
- se recibe texto y/o audio incremental,
- la reproducción puede empezar antes de que la respuesta completa esté terminada.

Eso permitiría:

- reducir más el `Time to first audio`,
- soportar interrupciones,
- mejorar la naturalidad del turn-taking,
- acercarse a una experiencia conversacional real.

## Resumen del estado del proyecto

### Ya implementado

- Arquitectura modular basada en interfaces.
- Push-to-talk funcional.
- Grabación por micrófono.
- Silence trimming antes del STT.
- STT con cliente dedicado.
- Chat con cliente directo a OpenAI.
- Opción de backend propio para personaje.
- TTS con síntesis y reproducción en avatar.
- Emociones e intent tags conectados al avatar.
- UI de debug en escena.
- Métricas detalladas de latencia.
- Primeras rondas de optimización de modelos, prompt y velocidad TTS.

### Pendiente / futuro

- Streaming de audio real.
- Migración a Realtime API.
- Interrupción del personaje mientras habla.
- VAD automático.
- Historial conversacional persistente.
- Gestión más robusta de secretos y configuración de entorno.

## Posible organización futura del repositorio

A medida que el proyecto crezca, se podría reorganizar la carpeta de scripts en algo como:

```text
Scripts/
  Conversation/
    Core/
    Services/
      Chat/
      STT/
      TTS/
    Avatar/
    UI/
```

No es obligatorio, pero ayudaría bastante cuando se añada soporte realtime o más proveedores.

## Notas de desarrollo

Este prototipo se ha ido afinando de forma iterativa midiendo latencia real en Unity turno a turno. El paso más importante realizado hasta ahora ha sido separar correctamente:

- lo que tarda el sistema en empezar a responder,
- y lo que tarda en terminar de reproducir toda la locución.

Eso ha permitido identificar con claridad el cuello de botella actual: el **TTS**.
