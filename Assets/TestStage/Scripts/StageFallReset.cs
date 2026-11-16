using UnityEngine;

namespace AshenPaw.TestStage
{
    /// <summary>Returns a Player-tagged Rigidbody2D to the test-stage spawn after a fall.</summary>
    public sealed class StageFallReset : MonoBehaviour
    {
        [SerializeField] private Transform spawnPoint;

        public void SetSpawn(Transform spawn) => spawnPoint = spawn;

        private void OnTriggerEnter2D(Collider2D other)
        {
            Rigidbody2D body = other.attachedRigidbody;
            if (body == null || spawnPoint == null || !body.CompareTag("Player"))
                return;

            body.GetComponent<AshenPaw.PlayerMovement>()?.Respawn();
            body.position = spawnPoint.position;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }
}
