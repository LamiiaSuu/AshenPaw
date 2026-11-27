using Unity;
using UnityEngine;

namespace AshenPaw
{
    public sealed class AnimationEventRelay : MonoBehaviour
    {
        private PlayerAudio playerAudio;

        private void Awake()=> playerAudio = GetComponentInParent<PlayerAudio>();

        public void Footstep()
        {
            if (playerAudio != null) playerAudio.PlayFootstep();
        }
    }
}