using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MVP.Conversation
{
    public class VoiceConversationController : MonoBehaviour
    {
        [SerializeField] private AvatarEmotionController emotionController;


        [Header("Services")]
        [SerializeField] private MonoBehaviour sttServiceBehaviour;
        [SerializeField] private MonoBehaviour chatServiceBehaviour;
        [SerializeField] private MonoBehaviour ttsServiceBehaviour;

        [Header("Recording")]
        [SerializeField] private MicrophoneRecorder microphoneRecorder;
        [SerializeField] private Button holdToTalkButton;
        [SerializeField] private bool useKeyboardDebugShortcut = true;
        [SerializeField] private KeyCode keyboardDebugKey = KeyCode.Space;

        [Header("Playback")]
        [SerializeField] private AudioSource avatarAudioSource;
        [SerializeField] private ConversationDebugView debugView;

        private ISTTService sttService;
        private IChatService chatService;
        private ITTSService ttsService;
        private bool isBusy;
        private bool isHoldingRecord;

        private void Awake()
        {
            sttService = sttServiceBehaviour as ISTTService;
            chatService = chatServiceBehaviour as IChatService;
            ttsService = ttsServiceBehaviour as ITTSService;
        }

        private void Update()
        {
            if (!useKeyboardDebugShortcut)
                return;

            if (Input.GetKeyDown(keyboardDebugKey))
                BeginRecording();

            if (Input.GetKeyUp(keyboardDebugKey))
                EndRecordingAndSend();
        }

        public void BeginRecording()
        {
            if (isBusy || microphoneRecorder == null)
                return;

            isHoldingRecord = true;
            microphoneRecorder.StartRecording();
            debugView?.SetState("State: Recording");
            debugView?.SetDebug("Grabando desde micrófono...");
        }

        public void EndRecordingAndSend()
        {
            if (!isHoldingRecord || isBusy || microphoneRecorder == null)
                return;

            isHoldingRecord = false;
            AudioClip clip = microphoneRecorder.StopRecording();
            if (clip == null)
            {
                debugView?.SetState("State: Error");
                debugView?.SetDebug("No se pudo obtener audio del micrófono.");
                return;
            }

            byte[] wavBytes = WavUtility.FromAudioClip(clip);
            StartCoroutine(RunVoiceConversation(wavBytes));
        }

        private IEnumerator RunVoiceConversation(byte[] wavBytes)
        {
            isBusy = true;
            debugView?.SetState("State: Processing");
            debugView?.SetDebug("Transcribiendo audio...");

            string userText = null;
            string sttError = null;
            yield return sttService.Transcribe(wavBytes, (text, error) =>
            {
                userText = text;
                sttError = error;
            });

            if (!string.IsNullOrEmpty(sttError) || string.IsNullOrWhiteSpace(userText))
            {
                debugView?.SetState("State: Error");
                debugView?.SetDebug("ERROR STT:\n" + sttError);
                isBusy = false;
                yield break;
            }

            debugView?.SetDebug("USER: " + userText + "\n\nConsultando chat...");

            string assistantText = null;
            string chatError = null;
            yield return chatService.RequestChat(userText, (text, error) =>
            {
                assistantText = text;
                chatError = error;
            });

            if (!string.IsNullOrEmpty(chatError) || string.IsNullOrWhiteSpace(assistantText))
            {
                debugView?.SetState("State: Error");
                debugView?.SetDebug("ERROR CHAT:\n" + chatError);
                isBusy = false;
                yield break;
            }

            debugView?.SetDebug("USER: " + userText + "\n\nAI: " + assistantText + "\n\nGenerando TTS...");

            AudioClip ttsClip = null;
            string ttsError = null;
            yield return ttsService.RequestSpeech(assistantText, (clip, error) =>
            {
                ttsClip = clip;
                ttsError = error;
            });

            if (!string.IsNullOrEmpty(ttsError) || ttsClip == null)
            {
                debugView?.SetState("State: Error");
                debugView?.SetDebug("ERROR TTS:\n" + ttsError);
                isBusy = false;
                yield break;
            }

            debugView?.SetState("State: Speaking");
            debugView?.SetDebug("USER: " + userText + "\n\nAI: " + assistantText);

            avatarAudioSource.Stop();
            avatarAudioSource.clip = ttsClip;
            avatarAudioSource.Play();

            while (avatarAudioSource.isPlaying)
                yield return null;

            debugView?.SetState("State: Idle");
            isBusy = false;
        }
    }
}