using System.Collections.Generic;
using UnityEngine;

namespace MVP.Conversation
{
    public class AvatarEmotionController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Animator del avatar. Si vacío, busca en hijos.")]
        private Animator animator;

        // Nombres de parámetros en el Animator
        [Header("Animator Parameters")]
        [SerializeField]
        [Tooltip("Nombre del parámetro de emoción en Animator (p.ej. 'Emotion'). Sync con Animator.")]
        private string emotionParam = "Emotion";
        
        [SerializeField]
        [Tooltip("Nombre del parámetro de intención en Animator (p.ej. 'Intent'). Sync con Animator.")]
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

            // intent principal: el primero de la lista, si existe
            var mainIntent = intents != null && intents.Count > 0 ? intents[0] : IntentTag.Unknown;
            int intentInt = IntentToInt(mainIntent);
            animator.SetInteger(intentParam, intentInt);

            // aquí puedes también disparar triggers si quieres
            // por ejemplo: animator.SetTrigger("React");
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