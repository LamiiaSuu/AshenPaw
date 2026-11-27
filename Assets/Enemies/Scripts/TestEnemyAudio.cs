using UnityEngine;

namespace AshenPaw
{
    [RequireComponent(typeof(TestEnemy), typeof(AudioSource))]
    public sealed class TestEnemyAudio : MonoBehaviour
    {
        [Header("Attacks")]
        [SerializeField] private AudioClip[] lightAttack;

        [Header("Damage Taken")]
        [SerializeField] private AudioClip[] biteDamage;
        [SerializeField] private AudioClip[] heavyBiteDamage;
        [SerializeField] private AudioClip[] slashDamage;

        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.2f;
        [SerializeField, Range(0f, 0.3f)] private float pitchVariation = 0.08f;

        private TestEnemy testEnemy;
        private AudioSource source;

        private void Awake()
        {
            testEnemy = GetComponent<TestEnemy>();
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            testEnemy.DamageTaken += OnDamageTaken;
        }

        private void OnDisable()
        {
            testEnemy.DamageTaken -= OnDamageTaken;
        }

        private void OnDamageTaken(bool heavyHitSound)
        {
            Play(heavyHitSound ? heavyBiteDamage : biteDamage, 0.05f);
        }

        private void Play(AudioClip[] clips, float volumeScale = 1f)
        {
            if (clips == null || clips.Length == 0) return;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;

            source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip, volume*volumeScale);
        }
    }
}
