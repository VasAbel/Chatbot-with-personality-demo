using UnityEngine;

[DisallowMultipleComponent]
public class ChatBubbleAnchor : MonoBehaviour
{
    [Header("Prefab & Positioning")]
    [SerializeField] private ChatBubble bubblePrefab;
    [SerializeField] private Vector3 offset = new Vector3(0f, 2f, 0f);
    [SerializeField] private float defaultLifetime = 3.0f;

    private ChatBubble current;

    public void Show(string text, float? lifetime = null)
    {
        if (!bubblePrefab) return;

        if (current != null)
        {
            Destroy(current.gameObject);
            current = null;
        }

        // Instantiate as a child of the NPC so it follows movement
        var go = Instantiate(bubblePrefab, transform); // parent = this
        // place above head with local offset
        go.transform.localPosition = offset;
        go.transform.localRotation = Quaternion.identity;

        current = go.GetComponent<ChatBubble>();
        current.Initialize(text, lifetime ?? defaultLifetime);
    }

    /// <summary>
    /// Manually hide an active bubble, if any.
    /// </summary>
    public void Hide()
    {
        if (current)
        {
            Destroy(current.gameObject);
            current = null;
        }
    }
}
