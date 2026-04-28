# VR Conversational Avatar – Runtime Pipeline

Este repositorio contiene el pipeline de conversación completa para el avatar VR: entrada de voz o texto, STT, backend de chat del cliente y TTS con reproducción en tiempo **pseudo realtime**, incluyendo control de emociones, lipsync en Unity y gestión de sesiones anónimas por usuario.

## Visión general

El flujo completo por turno es:

1. El usuario habla (push‑to‑talk) o escribe en el input de texto.
2. Si es voz, se recorta el audio y se envía a STT.
3. El texto resultante se envía al backend de chat del cliente (`POST /chat`).
4. La respuesta del backend se sintetiza a voz mediante TTS segmentado.
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

- El mismo controller soporta entrada por `TMP_InputField` y botón de enviar (`debugInputField` + `debugSendButton`) para trabajar en entornos silenciosos.
- Llamando a `StartTextConversation(text)` se salta toda la parte de micrófono/STT y usa directamente el flujo de chat + TTS.

---

## Capa de chat y contrato con el backend del cliente

La capa de chat se abstrae con `IChatService`.  
En producción se utiliza `CharacterBackendChatClient`, que implementa el contrato que definió el cliente.

### Request `POST /chat`

Por cada turno de conversación se envía:

```json
{
  "session_id": "uuid",
  "user_text": "string",
  "character_id": "francisco-de-vitoria",
  "metadata": {
    "locale": "es-ES",
    "user_id": null
  }
}
```

- `session_id` lo genera la app (ver sección de sesiones) y el backend lo usa para mantener el historial server‑side.
- `user_text` es la salida del STT (o el texto introducido por debug).
- `character_id` es fijo en este MVP: `francisco-de-vitoria`.
- `metadata.locale` fija el idioma de la conversación.
- `metadata.user_id` se deja nulo porque no hay login.

Todas las llamadas se autenticán con un Bearer token estático configurado en el inspector:

```http
Authorization: Bearer <TOKEN_DEL_CLIENTE>
Content-Type: application/json
```

### Response `POST /chat`

El backend devuelve:

```json
{
  "session_id": "uuid",
  "response_text": "string",
  "emotion": "neutral|happy|thinking|concerned|...",
  "intent_tags": ["greeting", "knowledge_answer", "fallback", "out_of_scope"],
  "sources": [
    { "title": "string", "score": 0.87 }
  ],
  "metadata": {
    "latency_ms": 1240,
    "model": "string",
    "rag_hits": 3
  }
}
```

`CharacterBackendChatClient` mapea esta respuesta a `ChatServiceResult`:

- `response_text` → texto del asistente (`responseText`), que se pasa a TTS.
- `emotion` → `CharacterEmotion` para el `AvatarEmotionController`.
- `intent_tags` → lista de `IntentTag` (greeting, knowledge_answer, fallback, out_of_scope, etc.).
- `sources` → títulos de las fuentes usadas por el RAG, visibles en la vista de debug.
- `metadata.latency_ms`, `metadata.model`, `metadata.rag_hits` → se guardan en el resultado y se muestran en `LogTiming` / `ConversationDebugView` para análisis de calidad del backend.

---

## Gestión de sesiones anónimas y cambio de usuario

El cliente gestiona el estado conversacional **server‑side** por `session_id`.  
En la app no hay login: simplemente se quiere separar el contexto cuando se pasan las gafas de una persona a otra.

### `SessionManager`

`SessionManager` se encarga de:

- Generar y almacenar el `session_id` actual.
- Llevar un contador local de:
  - `CurrentUserIndex` (sesión 1, 2, 3… en este dispositivo).
  - `CurrentTurnIndex` (turno dentro de la sesión).

API principal:

- `StartNewSession()`  
  Genera un nuevo `session_id` (uuid), incrementa `CurrentUserIndex` y resetea `CurrentTurnIndex`.
- `RegisterTurn()`  
  Se llama después de cada turno de conversación para incrementar el contador.
- `HasActiveSession`  
  Indica si ya hay una sesión inicializada (en el arranque se crea una por defecto).

`CharacterBackendChatClient` consulta `SessionManager` para enviar siempre el `session_id` correcto en `POST /chat`.

### Cambio de usuario (botón físico en el headset/mandos)

No hay botón visual de “cambiar usuario” en la UI.  
Cuando se pasan las gafas a otra persona, el operador pulsa un botón físico del visor o de los mandos (configurable en el Input System).

- `NewUserSessionInputHandler` escucha una acción de entrada (`NewUser`) configurada en el asset de Input Actions XR.
- Al dispararse esa acción:
  - `OpenAIConversationController.StartNewAnonymousUserSession()`:
    - Detiene cualquier conversación en curso.
    - Para el `AudioSource` del avatar.
    - Pide al cliente de chat que resetee su contexto local si lo soporta (`IConversationResettable`).
    - Llama a `SessionManager.StartNewSession()` para generar un nuevo `session_id`.
- A partir de ese momento, la siguiente petición `POST /chat` se envía con un `session_id` nuevo, de modo que el backend empieza un historial distinto para el siguiente usuario, como pedía el cliente.

---

## Capa de chat (LLM/backend)

Además del cliente del backend del cliente (`CharacterBackendChatClient`), la app puede usar `OpenAIChatClient` u otras implementaciones de `IChatService` para debug o pruebas internas.

- `IChatService` define:
  - `RequestChat(userText, ...)` → respuesta simple en texto.
  - `RequestChatRich(userText, ...)` → devuelve `ChatServiceResult` con:
    - `responseText`
    - `emotion`
    - `intentTags`
    - `latencyMs`, `model`, `ragHits`
    - `sourceTitles` (títulos de `sources`)
- `OpenAIConversationController` llama a `RequestChatRich`, aplica emoción al avatar y pasa el texto a la capa de TTS.

---

## Nuevo enfoque TTS (pseudo realtime)

### Antes

- El TTS funcionaba de forma **batch**: se pedía un único audio para toda la respuesta y se esperaba a tener el clip completo antes de reproducir.
- Esto provocaba:
  - Tiempos mayores de “time to first audio”.
  - Riesgo de que el audio se recortase al final en respuestas largas.

### Ahora

Se ha cambiado a un enfoque de **TTS segmentado y encadenado**, que se comporta de forma pseudo realtime:

1. La respuesta del LLM se divide en **segmentos de texto** (frases o grupos de frases).
2. Para cada segmento:
   - Se solicita TTS al servicio interno (`ITTSService` o `IStreamingTTSService`).
   - Se obtiene un `AudioClip` (WAV) o se escribe en un buffer PCM.
3. El primer segmento se reproduce en el `AudioSource` del avatar en cuanto está listo, **sin esperar** al audio completo.
4. Mientras suena el primer segmento, el sistema **pre‑carga en paralelo** el siguiente segmento para encadenarlo con una pausa mínima.
5. Entre segmentos se aplica un pequeño `transitionPaddingSeconds` (~0.03 s) para suavizar el cambio de clip y evitar cortes perceptibles.

El controller detecta si el servicio TTS implementa `IStreamingTTSService` y, en ese caso, usa el camino streaming (`RequestSpeechStreamed`) que coordina la reproducción por segmentos sobre un `AudioClip` circular (`StreamingAudioBuffer`).

### Beneficios

- **Menor latencia percibida**: el avatar empieza a hablar antes porque no se espera a tener el audio completo.
- **Respuestas largas más robustas**: al trabajar por segmentos, se reduce el riesgo de recorte al final.
- **Conversación más natural**: se mantienen micro‑pausas entre frases, pero se ha afinado la transición para que no parezca que el avatar corta y reanuda de forma brusca.

---

## Arquitectura de servicios

Las interfaces principales viven en `ConversationContracts.cs`:

- `ISTTService`
  - Servicio de STT (por ejemplo, `OpenAISTTClient`).
  - API: `Transcribe(byte[] audioBytes, ...)`.

- `IChatService`
  - Abstracción de chat/LLM (backend del cliente u OpenAI).
  - API: `RequestChat` y `RequestChatRich`.

- `ITTSService`
  - TTS batch (retorna `AudioClip` completo).
  - Implementaciones:
    - `BufferedOpenAITTSClientWav` (OpenAI TTS → WAV → `AudioClip`).

- `IStreamingTTSService`
  - TTS streaming PCM:
    - Recibe texto + `AudioSource`.
    - Escribe en un buffer de audio en tiempo real.
    - Notifica inicio, error y fin.

`OpenAIConversationController` orquesta:

1. Entrada (texto o voz).
2. STT si hace falta.
3. Llamada al `IChatService`.
4. Aplicación de emoción en `AvatarEmotionController`.
5. TTS (streaming o batch) y reproducción en el avatar.

---

## Métricas de conversación y debug

`ConversationResult` y `ConversationTiming` almacenan:

- STT: `SttSeconds`
- Chat: `ChatSeconds`
- TTS: `TtsSeconds`
- `TimeToFirstAudioSeconds`
- `PlaybackDurationSeconds`
- `TimeToPlaybackEndSeconds`
- `TurnCompleteSeconds`

Además, `ConversationResult` incluye metadatos del backend:

- `backendLatencyMs`
- `backendModel`
- `backendRagHits`
- `backendSourceTitles` (lista de títulos de `sources`)

`OpenAIConversationController.LogTiming`:

- Construye un texto con todos los tiempos.
- Añade:
  - `Backend latency`, `Backend model`, `RAG hits`.
  - `Sources: título1, título2, ...`.
  - (Opcionalmente) el texto de la respuesta del asistente.
- Lo envía a:
  - `Debug.Log` (en forma compacta, con saltos de línea reemplazados).
  - `ConversationDebugView` en runtime.

Esto permite medir de forma objetiva la mejora de latencia del TTS segmentado y analizar el comportamiento del RAG del cliente (latencias, modelo usado y fuentes activadas) mientras se prueba el avatar en VR.

---

## Estado actual

- El enfoque TTS segmentado pseudo realtime está integrado en `main` y se considera suficientemente estable para seguir iterando.
- La entrada de texto vía UI está habilitada, reutilizando el mismo pipeline de chat + TTS sin depender del micrófono.
- La integración con el backend del cliente usa `POST /chat` con `session_id` anónimo, `character_id = "francisco-de-vitoria"` y el contrato de request/response acordado.
- El cambio de usuario se hace mediante un botón físico del headset/mandos que reinicia contexto y genera un nuevo `session_id`.
- La arquitectura de servicios (`ISTTService`, `IChatService`, `ITTSService`, `IStreamingTTSService`) permite seguir cambiando proveedores de STT/TTS/LLM sin romper el `OpenAIConversationController`.
