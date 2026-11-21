using System.Collections.Generic;
using UnityEngine;

namespace AshenPaw
{
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class StageParallax : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private Vector2 cameraAnchor;
        [SerializeField] private Vector2 depth = new Vector2(0.25f, 0.15f);
        [SerializeField] private bool coverViewport;
        [SerializeField] private bool repeatHorizontally;

        private readonly List<SpriteRenderer> tiles = new List<SpriteRenderer>();
        private SpriteRenderer original;

        private Vector3 origin;
        private Vector3 scale;
        private Vector2 spriteSize;

        private void Awake()
        {
            origin = transform.position;
            scale = transform.localScale;
            original = GetComponent<SpriteRenderer>();
            var sprite = original.sprite;
            spriteSize = sprite != null ? (Vector2)sprite.bounds.size : Vector2.zero;
        }

        private void LateUpdate()
        {
            if (view == null) view = Camera.main;
            if (view == null) return;

            Vector2 cameraPosition = view.transform.position;
            Vector2 offset = Vector2.Scale(cameraPosition - cameraAnchor, Vector2.one - depth);
            Vector3 position = origin + (Vector3)offset;
            transform.position = position;

            if (spriteSize.x <= 0f || spriteSize.y <= 0f || !view.orthographic) return;

            if (repeatHorizontally)
            {
                UpdateTiles(cameraPosition.x, view.orthographicSize * view.aspect);
                return;
            }
            if (!coverViewport) return;

            // Fit to the viewport size, then limit parallax travel before it exposes an edge.
            float halfWidth = view.orthographicSize * view.aspect + 0.05f;
            float halfHeight = view.orthographicSize + 0.05f;
            float spriteHalfWidth = spriteSize.x * Mathf.Abs(scale.x) * 0.5f;
            float spriteHalfHeight = spriteSize.y * Mathf.Abs(scale.y) * 0.5f;
            float fit = Mathf.Max(1f, Mathf.Max(halfWidth / spriteHalfWidth, halfHeight / spriteHalfHeight));
            transform.localScale = new Vector3(scale.x * fit, scale.y * fit, scale.z);

            float travelX = Mathf.Max(0f, spriteHalfWidth * fit - halfWidth);
            float travelY = Mathf.Max(0f, spriteHalfHeight * fit - halfHeight);
            position.x = Mathf.Clamp(position.x, cameraPosition.x - travelX, cameraPosition.x + travelX);
            position.y = Mathf.Clamp(position.y, cameraPosition.y - travelY, cameraPosition.y + travelY);
            transform.position = position;
        }

        private void UpdateTiles(float cameraX, float halfWidth)
        {
            float tileWidth = spriteSize.x * Mathf.Abs(transform.lossyScale.x);
            if (tileWidth <= 0f) return;

            float left = cameraX - halfWidth - transform.position.x - 1f;
            float right = cameraX + halfWidth - transform.position.x + 1f;
            int first = Mathf.FloorToInt(left / tileWidth + 0.5f);
            int last = Mathf.FloorToInt(right / tileWidth + 0.5f);
            int used = 0;

            for (int index = first; index <= last; index++)
            {
                if (index == 0) continue;
                if (used == tiles.Count) tiles.Add(CreateTile());

                SpriteRenderer tile = tiles[used++];
                tile.transform.localPosition = Vector3.right * (spriteSize.x * index);
                tile.flipX = original.flipX ^ ((index & 1) != 0);
                tile.enabled = true;
            }

            for (int i = used; i < tiles.Count; i++) tiles[i].enabled = false;
        }

        private SpriteRenderer CreateTile()
        {
            var tile = new GameObject("Background repeat").AddComponent<SpriteRenderer>();
            tile.transform.SetParent(transform, false);
            tile.sprite = original.sprite;
            tile.sharedMaterial = original.sharedMaterial;
            tile.color = original.color;
            tile.flipY = original.flipY;
            tile.sortingLayerID = original.sortingLayerID;
            tile.sortingOrder = original.sortingOrder;
            return tile;
        }
    }
}
