using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
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
        [SerializeField] private AudioClip[] lightSlashAttackSounds;
        [SerializeField] private AudioClip[] heavySlashAttackSounds;
        
        [Header("Health")]
        [SerializeField, Range(0f, 1f)] private float healthVolume = 0.5f;
        [SerializeField] private AudioClip[] bluntDamageSounds;
        [SerializeField] private AudioClip[] slashDamageSounds;
        [SerializeField] private AudioClip[] deathSounds;

        [Header("Movement")]
        [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.5f;
        [SerializeField] private AudioClip[] footstepSounds;
        [SerializeField, Range(0f, 1f)] private float movementVolume = 0.5f;
        [SerializeField] private AudioClip[] jumpSounds;
        [SerializeField] private AudioClip[] landSounds;
        [SerializeField] private AudioClip[] dodgeSounds;



        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.2f;
        [SerializeField, Range(0f, 0.2f)] private float pitchVariation = 0.08f;


        private PlayerCombat combat;
        private PlayerMovement movement;
        private PlayerHealth health;
        private AudioSource source;

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
            combat.StrikeStarted += OnStrikeStarted;
            combat.StrikeCharged += OnStrikeStarted;
            movement.Dodged += OnDodge;
            movement.Jumped += OnJump;
            movement.Landed += OnLand;
            health.EnemyHitLanded += OnEnemyHitLanded;
            health.Death += OnDeath;
        }

        private void OnDisable()
        {
            combat.StrikeStarted -= OnStrikeStarted;
            combat.StrikeCharged -= OnStrikeStarted;
            movement.Dodged -= OnDodge;
            movement.Jumped -= OnJump;
            movement.Landed -= OnLand;
            health.EnemyHitLanded -= OnEnemyHitLanded;
            health.Death -= OnDeath;
        }

        private void Play(AudioClip[] clips, float volumeScale = 1f)
        {
            if (clips == null || clips.Length == 0) return;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;

            source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip, volume*volumeScale);
        }

        private void OnStrikeStarted()
        {
            if (combat.IsCharging) Play(chargingAttackSounds, attackVolume);
            else if (combat.IsHeavyAttack) Play(heavyBiteAttackSounds, attackVolume);
            else Play(lightBiteAttackSounds, attackVolume);
        }
        private void OnDodge() => Play(dodgeSounds, movementVolume);

        private void OnJump() => Play(jumpSounds, movementVolume);

        private void OnLand() => Play(landSounds, 0.05f);

        public void PlayFootstep()
        {
            if(!movement.IsGrounded) return;
            Play(footstepSounds, footstepVolume);
        }
        
        private void OnEnemyHitLanded() => Play(bluntDamageSounds, healthVolume);
        private void OnDeath() => Play(deathSounds, healthVolume);
    }   
}
