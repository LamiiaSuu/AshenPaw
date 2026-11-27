using System.Collections;
using UnityEngine;

namespace AshenPaw
{
[RequireComponent(typeof(PlayerCombat), typeof(AudioSource))]
 public sealed class PlayerAudio : MonoBehaviour
    {   
        
        [Header("Attacks")]
        [SerializeField, Range(0f, 1f)] private float attackVolume = 0.5f;
        [SerializeField] private AudioClip[] chargingAttackSounds;
        [SerializeField] private AudioClip[] lightBiteAttackSounds;
        [SerializeField] private AudioClip[] heavyBiteAttackSounds;
        [SerializeField] private AudioClip crossBiteHitSound;
        [SerializeField] private AudioClip[] lightSlashAttackSounds;
        [SerializeField] private AudioClip[] heavySlashAttackSounds;
        [SerializeField] private AudioClip counterBiteSound;
        [SerializeField] private AudioClip counterBiteBuildup;
        [SerializeField, Range(0f, 1f)] private float counterBiteVolume = 0.62f;

        [Header("Parry")]
        [SerializeField] private AudioClip parryWhoosh;
        [SerializeField] private AudioClip parryDrop;
        [SerializeField, Range(0f, 1f)] private float parryVolume = 0.70f;
        
        [Header("Health")]
        [SerializeField, Range(0f, 1f)] private float healthVolume = 0.5f;
        [SerializeField] private AudioClip[] bluntDamageSounds;
        [SerializeField] private AudioClip[] slashDamageSounds;
        [SerializeField] private AudioClip[] deathSounds;

        [Header("Movement")]
        [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.5f;
        [SerializeField] private AudioClip[] footstepSounds;
        [SerializeField, Range(0f, 1f)] private float movementVolume = 0.56f;
        [SerializeField] private AudioClip jumpWhoosh;
        [SerializeField, Range(0f, 1f)] private float jumpWhooshVolume = 0.02f;
        [SerializeField] private AudioClip[] landSounds;
        [SerializeField] private AudioClip[] dodgeSounds;



        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.2f;
        [SerializeField, Range(0f, 0.2f)] private float pitchVariation = 0.08f;


        private PlayerCombat combat;
        private PlayerMovement movement;
        private PlayerHealth health;
        private AudioSource source;
        private AudioSource roarSource;
        private AudioSource counterSource;
        private AudioSource parrySource;
        private Coroutine roarFade;
        private Coroutine parryFade;
        private const float RoarFadeDuration = 0.04f;
        private const float ParryFadeDuration = 0.075f;
        private const float FootstepPitch = 1.08f;

        private void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            source = GetComponent<AudioSource>();
            movement = GetComponent<PlayerMovement>();
            health = GetComponent<PlayerHealth>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            if (roarSource == null) roarSource = CreateEffectSource();
            if (counterSource == null) counterSource = CreateEffectSource();
            if (parrySource == null) parrySource = CreateEffectSource();

            combat.StrikeStarted += OnStrikeStarted;
            combat.CrossBiteHit += OnCrossBiteHit;
            combat.StrikeCharged += OnStrikeCharged;
            combat.HeavyReleased += OnHeavyReleased;
            combat.CriticalBiteStarted += OnCriticalBiteStarted;
            combat.CriticalBiteHit += OnCriticalBiteHit;
            combat.CriticalBiteEnded += OnCriticalBiteEnded;
            combat.ParryStarted += OnParryStarted;
            combat.ParrySucceeded += OnParrySucceeded;
            combat.ParryMissed += OnParryMissed;
            movement.Dodged += OnDodge;
            movement.Jumped += OnJump;
            movement.Landed += OnLand;
            health.EnemyHitLanded += OnEnemyHitLanded;
            health.Death += OnDeath;
        }

        private void OnDisable()
        {
            combat.StrikeStarted -= OnStrikeStarted;
            combat.CrossBiteHit -= OnCrossBiteHit;
            combat.StrikeCharged -= OnStrikeCharged;
            combat.HeavyReleased -= OnHeavyReleased;
            combat.CriticalBiteStarted -= OnCriticalBiteStarted;
            combat.CriticalBiteHit -= OnCriticalBiteHit;
            combat.CriticalBiteEnded -= OnCriticalBiteEnded;
            combat.ParryStarted -= OnParryStarted;
            combat.ParrySucceeded -= OnParrySucceeded;
            combat.ParryMissed -= OnParryMissed;
            movement.Dodged -= OnDodge;
            movement.Jumped -= OnJump;
            movement.Landed -= OnLand;
            health.EnemyHitLanded -= OnEnemyHitLanded;
            health.Death -= OnDeath;
            if (roarFade != null)
            {
                StopCoroutine(roarFade);
                roarFade = null;
            }
            if (roarSource != null) roarSource.Stop();
            if (counterSource != null) counterSource.Stop();
            StopParryFade();
            if (parrySource != null) parrySource.Stop();
        }

        private AudioSource CreateEffectSource()
        {
            var effectSource = gameObject.AddComponent<AudioSource>();
            effectSource.playOnAwake = false;
            effectSource.outputAudioMixerGroup = source.outputAudioMixerGroup;
            return effectSource;
        }

        private void Play(AudioClip[] clips, float volumeScale = 1f, float pitch = 1f)
        {
            Play(PickClip(clips), volumeScale, pitch);
        }

        private void Play(AudioClip clip, float volumeScale, float pitch = 1f)
        {
            if (clip == null) return;

            source.pitch = pitch + Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip, volume*volumeScale);
        }

        private static AudioClip PickClip(AudioClip[] clips)
        {
            return clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
        }

        private void OnStrikeCharged() => PlayRoar(PickClip(chargingAttackSounds));

        private void OnHeavyReleased() => PlayRoar(null);

        private void OnParryStarted() => PlayParrySound(parryWhoosh);

        private void OnParrySucceeded() => PlayParrySound(parryDrop);

        private void OnParryMissed()
        {
            if (parryFade == null && parrySource.clip == parryWhoosh && parrySource.isPlaying)
                parryFade = StartCoroutine(FadeParryWhoosh());
        }

        private IEnumerator FadeParryWhoosh()
        {
            float startVolume = parrySource.volume;
            float elapsed = 0f;
            while (elapsed < ParryFadeDuration && parrySource.isPlaying)
            {
                yield return null;
                elapsed += Time.deltaTime;
                parrySource.volume = Mathf.Lerp(startVolume, 0f, elapsed / ParryFadeDuration);
            }
            parrySource.Stop();
            parryFade = null;
        }

        private void StopParryFade()
        {
            if (parryFade == null) return;
            StopCoroutine(parryFade);
            parryFade = null;
        }

        private void PlayParrySound(AudioClip clip)
        {
            StopParryFade();
            parrySource.Stop();
            parrySource.clip = clip;
            if (clip == null) return;

            parrySource.mute = source.mute;
            parrySource.pitch = 1f;
            parrySource.volume = source.volume * volume * parryVolume;
            parrySource.Play();
        }

        private void OnCriticalBiteStarted(float windup)
        {
            if (counterBiteBuildup == null) return;
            float buildupTime = Mathf.Max(0.05f, windup - 0.075f);
            PlayCounterSound(counterBiteBuildup, counterBiteBuildup.length / buildupTime, 1.1f);
        }

        private void OnCriticalBiteHit() => PlayCounterSound(counterBiteSound, 1f);

        private void OnCriticalBiteEnded()
        {
            if (counterSource.clip == counterBiteBuildup) counterSource.Stop();
        }

        private void PlayCounterSound(AudioClip clip, float pitch, float gain = 1f)
        {
            counterSource.Stop();
            counterSource.clip = clip;
            if (clip == null) return;

            counterSource.mute = source.mute;
            counterSource.volume = Mathf.Clamp01(source.volume * volume * counterBiteVolume * gain);
            counterSource.pitch = pitch;
            counterSource.Play();
        }

        private void PlayRoar(AudioClip clip, bool bite = false)
        {
            if (roarFade != null)
            {
                StopCoroutine(roarFade);
                roarFade = null;
            }

            if (roarSource.isPlaying)
                roarFade = StartCoroutine(FadeOutRoar(clip, bite));
            else
                StartRoar(clip, bite);
        }

        private IEnumerator FadeOutRoar(AudioClip nextClip, bool bite)
        {
            float startVolume = roarSource.volume;
            float elapsed = 0f;
            while (elapsed < RoarFadeDuration && roarSource.isPlaying)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                roarSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / RoarFadeDuration);
            }

            roarFade = null;
            StartRoar(nextClip, bite);
        }

        private void StartRoar(AudioClip clip, bool bite)
        {
            roarSource.Stop();
            roarSource.clip = clip;
            if (clip == null) return;

            roarSource.mute = source.mute;
            roarSource.volume = Mathf.Clamp01(source.volume * volume * attackVolume * (bite ? 1.25f : 1f));
            roarSource.pitch = (bite ? 1.03f : 0.97f) + Random.Range(-pitchVariation, pitchVariation);
            roarSource.Play();
        }

        private void OnStrikeStarted()
        {
            if (combat.IsLightComboBite)
                Play(lightBiteAttackSounds, attackVolume * 0.73f, 1.10f);
            else if (combat.IsCrossBite && crossBiteHitSound != null)
                Play(dodgeSounds, attackVolume * 0.22f);
            else if (combat.IsHeavyAttack && !combat.IsDashAttack &&
                !combat.IsSnapBite && !combat.IsAirLunge && !combat.IsAirDive)
                PlayRoar(PickClip(heavyBiteAttackSounds), true);
            else
                Play(lightBiteAttackSounds, attackVolume * (!combat.IsHeavyAttack || combat.IsDashAttack ? 0.80f : 0.25f));
        }

        private void OnCrossBiteHit() => Play(crossBiteHitSound, attackVolume * 0.68f);

        private void OnDodge() => Play(dodgeSounds, movementVolume);

        private void OnJump()
        {
            Play(footstepSounds, footstepVolume * 2.5f, FootstepPitch);
            if (jumpWhoosh != null)
                source.PlayOneShot(jumpWhoosh, volume * jumpWhooshVolume);
        }

        private void OnLand() => Play(landSounds, 0.05f);

        public void PlayFootstep()
        {
            if(!movement.IsGrounded) return;
            Play(footstepSounds, footstepVolume, FootstepPitch);
        }
        
        private void OnEnemyHitLanded() => Play(bluntDamageSounds, healthVolume);
        private void OnDeath() => Play(deathSounds, healthVolume);
    }   
}
