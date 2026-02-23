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
    [SerializeField] private float neighborRadius = 3.0f;

    [Tooltip("Horizontal offset (world units) applied when avoiding overlap.")]
    [SerializeField] private float sideOffsetAmount = 3.0f;

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

        var go = Instantiate(bubblePrefab);
        go.transform.rotation = Quaternion.identity;

        // Base position: above the head
        Vector3 worldPos = transform.position + verticalOffset;

        // Try to avoid overlap with any OTHER anchor that currently has a bubble
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

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
            Vector3 otherScreen = cam.WorldToScreenPoint(nearest.transform.position + nearest.verticalOffset);

            float dir = (screenPos.x < otherScreen.x) ? -1 : 1;

            screenPos.x += sideOffsetAmount * dir * 50;
        }

        Vector3 finalWorld = cam.ScreenToWorldPoint(screenPos);
        finalWorld.z = worldPos.z;
        // Convert world target position to this transform's local space
        go.transform.position = finalWorld;

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
