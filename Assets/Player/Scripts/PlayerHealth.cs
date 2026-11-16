using UnityEngine;

namespace AshenPaw
{
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class PlayerHealth : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 5;
        public int Health { get; private set; }
        private PlayerMovement movement;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            Health = maxHealth;
        }

        public bool TakeDamage(int damage)
        {
            if (damage <= 0 || movement.IsInvulnerable) return false;
            Health = Mathf.Max(0, Health - damage);
            if (Health == 0)
            {
                movement.Respawn();
                Health = maxHealth;
            }
            return true;
        }
    }
}
