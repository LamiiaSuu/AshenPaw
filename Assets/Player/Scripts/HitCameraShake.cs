using UnityEngine;
using UnityEngine.Rendering;

namespace AshenPaw
{
    [RequireComponent(typeof(Camera))]
    public sealed class HitCameraShake : MonoBehaviour
    {
        [SerializeField] private float duration = 0.13f;
        [SerializeField] private float strength = 0.075f;
        private Camera view;
        private float shakeUntil;
        private Vector3 appliedOffset;
        private bool applied;

        private float intensity = 1f;
        public void Trigger() => Trigger(1f);
        public void Trigger(float amount)
        {
            amount = Mathf.Max(0f, amount);
            if (amount <= 0f) return;
            if (Time.unscaledTime < shakeUntil && amount < intensity) return;
            intensity = amount;
            shakeUntil = Time.unscaledTime + duration;
        }

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera) { if (camera == view) ApplyShake(); }
        private void EndCamera(ScriptableRenderContext context, Camera camera) { if (camera == view) RestoreCamera(); }
        private void OnPreCull() => ApplyShake();
        private void OnPostRender() => RestoreCamera();

        private void ApplyShake()
        {
            if (applied || Time.unscaledTime >= shakeUntil) return;
            float fade = Mathf.Clamp01((shakeUntil - Time.unscaledTime) / Mathf.Max(0.001f, duration));
            appliedOffset = (Vector3)Random.insideUnitCircle * (strength * intensity * fade);
            transform.position += appliedOffset;
            applied = true;
        }

        private void RestoreCamera()
        {
            if (!applied) return;
            transform.position -= appliedOffset;
            appliedOffset = Vector3.zero;
            applied = false;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RestoreCamera();
            shakeUntil = 0f;
        }
    }
}
