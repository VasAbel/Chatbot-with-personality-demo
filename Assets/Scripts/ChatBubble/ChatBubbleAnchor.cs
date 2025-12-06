using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class ChatBubbleAnchor : MonoBehaviour
{
    [Header("Prefab & Positioning")]
    [SerializeField] private ChatBubble bubblePrefab;
    [SerializeField] private Vector3 verticalOffset = new Vector3(0f, 0.2f, 0f);
    [SerializeField] private float defaultLifetime = 3.0f;

    [Header("Anti-Overlap Settings")]
    [Tooltip("Max distance (world units) to consider another NPC as a 'conversation partner'.")]
    [SerializeField] private float neighborRadius = 1.5f;

    [Tooltip("Horizontal offset (world units) applied when avoiding overlap.")]
    [SerializeField] private float sideOffsetAmount = 2f;

    private ChatBubble current;

    // Track all anchors so we can find neighbors
    private static readonly List<ChatBubbleAnchor> ActiveAnchors = new List<ChatBubbleAnchor>();

    private void OnEnable()
    {
        if (!ActiveAnchors.Contains(this))
            ActiveAnchors.Add(this);
    }

    private void OnDisable()
    {
        ActiveAnchors.Remove(this);
    }

    public void Show(string text, float? lifetime = null)
    {
        if (!bubblePrefab) return;

        // Kill old bubble (only one at a time per NPC)
        if (current != null)
        {
            Destroy(current.gameObject);
            current = null;
        }

        var go = Instantiate(bubblePrefab, transform);
        go.transform.localRotation = Quaternion.identity;

        // Base position: above the head
        Vector3 worldPos = transform.position + verticalOffset;

        // Try to avoid overlap with any OTHER anchor that currently has a bubble
        Camera cam = Camera.main;
        if (cam != null)
        {
            ChatBubbleAnchor nearest = null;
            float bestDist = float.MaxValue;

            foreach (var anchor in ActiveAnchors)
            {
                if (anchor == this) continue;
                if (!anchor.current) continue;   // only care about anchors that already have a visible bubble

                float d = Vector3.Distance(transform.position, anchor.transform.position);
                if (d < neighborRadius && d < bestDist)
                {
                    bestDist = d;
                    nearest = anchor;
                }
            }

            if (nearest != null)
            {
                // Decide side relative to the camera's right vector
                Vector3 right = cam.transform.right;
                Vector3 toNeighbor = nearest.transform.position - transform.position;

                // If the neighbor is to the right on screen, we shift this bubble to the left, and vice versa
                float dot = Vector3.Dot(toNeighbor, right); // >0 => neighbor is to the right
                float dir = (dot > 0f) ? -1f : 1f;

                worldPos += right * sideOffsetAmount * dir;
            }
        }

        // Convert world target position to this transform's local space
        go.transform.position = worldPos;

        current = go.GetComponent<ChatBubble>();
        current.Initialize(text, lifetime ?? defaultLifetime);
    }

    public void Hide()
    {
        if (current)
        {
            Destroy(current.gameObject);
            current = null;
        }
    }
}
