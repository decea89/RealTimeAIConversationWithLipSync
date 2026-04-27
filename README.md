# VR Conversational Avatar – Runtime Pipeline

Este repositorio contiene el pipeline de conversación completa para el avatar VR: entrada de voz o texto, STT, LLM y TTS con reproducción en tiempo **pseudo realtime**, incluyendo control de emociones y lipsync en Unity.

## Visión general

El flujo completo por turno es:

1. El usuario habla (push‑to‑talk) o escribe en el input de texto.
2. Si es voz, se recorta el audio y se envía a STT.
3. El texto resultante se envía al backend de chat (LLM).
4. La respuesta del LLM se sintetiza a voz mediante TTS segmentado.
5. El avatar reproduce el audio, sincroniza emociones y lipsync.

El controlador principal de orquestación es `OpenAIConversationController`.

---

## Entrada: voz y texto

### Push‑to‑talk (voz)

- `OpenAIConversationController` gestiona el push‑to‑talk con teclado (modo debug) y con `MicrophoneRecorder`.
- El audio grabado se pasa por:
  - `AudioTrimmingUtility` para recortar silencios al inicio y final.
  - `WavUtility.FromAudioClip` para empaquetar el audio en WAV.
- El resultado se envía a `ISTTService` (por ejemplo, `OpenAISTTClient`) para obtener el texto del usuario.

### Entrada de texto directa

- El mismo controller soporta entrada por `TMP_InputField` y botón de enviar (`debugInputField` + `debugSendButton`) para trabajar en silencio.
- Llamando a `StartTextConversation(text)` se salta toda la parte de micrófono/STT y usa directamente el flujo de chat + TTS.

---

## Capa de chat (LLM)

- La capa de chat se abstrae con `IChatService` (por ejemplo, `OpenAIChatClient` o `CharacterBackendChatClient`).
- El controller llama a `RequestChatRich(userText, callback)` y recibe:
  - `responseText` con la respuesta del asistente.
  - Metadatos como emoción e intent tags para el avatar.
- `AvatarEmotionController` aplica la emoción al rig del personaje antes de reproducir la voz.

---

## Nuevo enfoque TTS (pseudo realtime)

### Antes

- El TTS funcionaba de forma **batch**: se pedía un único audio para toda la respuesta y se esperaba a tener el clip completo antes de empezar a reproducir.
- Esto provocaba:
  - Tiempos de “time to first audio” más altos.
  - Riesgo de que el audio se recortase al final en respuestas largas.

### Ahora

He cambiado a un enfoque de **TTS segmentado y encadenado**, que se comporta de forma pseudo realtime:

1. La respuesta del LLM se divide en **segmentos de texto** (frases o grupos de frases) mediante `SegmentedBufferedTTSClient`.
2. Para cada segmento:
   - Se solicita TTS al servicio interno (`ITTSService`, por ejemplo, un cliente PCM que llama a OpenAI).
   - Se obtiene un `AudioClip` por segmento.
3. El primer segmento se reproduce en el `AudioSource` del avatar en cuanto está listo, **sin esperar** a que se genere el audio de toda la respuesta.
4. Mientras suena el primer segmento, el sistema **pre‑carga en paralelo** el siguiente segmento para encadenarlo con una pausa mínima.
5. Entre segmentos se aplica un pequeño `transitionPaddingSeconds` (por defecto ~0.03 s) para suavizar el cambio de clip y evitar cortes perceptibles.

El controller detecta si el servicio TTS implementa `IStreamingTTSService` y, en ese caso, usa el camino streaming (`RequestSpeechStreamed`) que coordina toda esta reproducción por segmentos.

### Beneficios

- **Menor latencia percibida**: el avatar empieza a hablar antes, porque no espera a tener toda la respuesta sintetizada.
- **Respuestas largas más robustas**: al trabajar por segmentos, reducimos el riesgo de recorte del audio al final.
- **Conversación más natural**: se mantienen micro‑pausas entre frases, pero se ha ajustado la transición para que no parezca que el avatar “termina de hablar” y luego reanuda de forma brusca.

El `OpenAIConversationController` mide métricas como:

- `TTS` (tiempo de síntesis),
- `Time to first audio`,
- `PlaybackDuration` y `Time to playback end`,

y las vuelca en logs y en la vista de debug (`ConversationDebugView`). En las pruebas recientes, estos valores han mejorado respecto al enfoque batch anterior, especialmente el tiempo hasta que comienza a sonar la respuesta.

---

## Arquitectura de servicios

Las interfaces principales viven en `ConversationContracts.cs`:

- `ITTSService`
  - TTS batch (clip completo por texto).
  - Implementaciones:
    - Clientes basados en WAV/PCM (por ejemplo, `BufferedOpenAITTSClientPcm`), utilizados como backend del TTS segmentado.

- `IStreamingTTSService`
  - TTS streamable: recibe texto y un `AudioSource`, reproduce el audio progresivamente e informa por callbacks de inicio, error y fin de reproducción.
  - Implementaciones:
    - `SegmentedBufferedTTSClient`, que hace pseudo realtime por segmentos sobre un `ITTSService` interno.

- `ISTTService`
  - Servicio de STT para convertir audio del usuario a texto.

- `IChatService`
  - Servicio de chat/LLM, con métodos para respuestas simples y ricas (`RequestChat`, `RequestChatRich`).

`OpenAIConversationController` orquesta estos servicios, detecta en runtime si el TTS soporta streaming y selecciona el camino adecuado (batch o segmentado).

---

## Métricas de conversación

`ConversationResult` y `ConversationTiming` almacenan todos los tiempos de cada turno de conversación:

- STT: `SttSeconds`
- Chat: `ChatSeconds`
- TTS: `TtsSeconds`
- Time to first audio
- Playback duration
- Time to playback end
- Turn complete

`OpenAIConversationController.LogTiming` imprime estas métricas en consola y opcionalmente en `ConversationDebugView`, junto con el texto de la respuesta del asistente si la opción está activada.

Esto permite medir de forma objetiva la mejora de latencia al pasar del enfoque TTS batch al enfoque segmentado pseudo realtime.

---

## Estado actual

- El enfoque TTS segmentado pseudo realtime está integrado en `main` y se considera suficientemente estable para seguir iterando.
- La entrada de texto vía UI está habilitada para trabajar en entornos silenciosos, reutilizando el mismo pipeline de chat + TTS sin depender del micrófono.
- La arquitectura de servicios (`ISTTService`, `IChatService`, `ITTSService`, `IStreamingTTSService`) permite seguir cambiando proveedores de STT/TTS/LLM sin romper el `OpenAIConversationController`.
