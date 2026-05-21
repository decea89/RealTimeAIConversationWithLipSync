using System.Collections.Generic;
using UnityEngine;

namespace MVP.Conversation
{
    public class AvatarEmotionController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Avatar Animator. If empty, searches in children.")]
        private Animator animator;

        // Animator parameter names
        [Header("Animator Parameters")]
        [SerializeField]
        [Tooltip("Emotion parameter name in the Animator (e.g. 'Emotion'). Must match the Animator.")]
        private string emotionParam = "Emotion";
        
        [SerializeField]
        [Tooltip("Intent parameter name in the Animator (e.g. 'Intent'). Must match the Animator.")]
        private string intentParam = "Intent";

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        public void ApplyEmotion(CharacterEmotion emotion, List<IntentTag> intents)
        {
            if (animator == null)
                return;

            int emotionInt = EmotionToInt(emotion);
            animator.SetInteger(emotionParam, emotionInt);

            // Main intent: the first item in the list, if present
            var mainIntent = intents != null && intents.Count > 0 ? intents[0] : IntentTag.Unknown;
            int intentInt = IntentToInt(mainIntent);
            animator.SetInteger(intentParam, intentInt);

            // You can also trigger Animator events here if needed.
            // For example: animator.SetTrigger("React");
        }

        private int EmotionToInt(CharacterEmotion emotion)
        {
            switch (emotion)
            {
                case CharacterEmotion.Happy: return 1;
                case CharacterEmotion.Thinking: return 2;
                case CharacterEmotion.Concerned: return 3;
                case CharacterEmotion.Angry: return 4;
                case CharacterEmotion.Sad: return 5;
                default: return 0; // Neutral
            }
        }

        private int IntentToInt(IntentTag intent)
        {
            switch (intent)
            {
                case IntentTag.Greeting: return 1;
                case IntentTag.KnowledgeAnswer: return 2;
                case IntentTag.Fallback: return 3;
                case IntentTag.OutOfScope: return 4;
                default: return 0;
            }
        }
    }
}