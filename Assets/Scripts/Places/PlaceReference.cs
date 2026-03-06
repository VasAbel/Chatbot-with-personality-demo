using UnityEngine;

public class PlaceReference : MonoBehaviour
{
    public string placeID;

    [Tooltip("Human-readable name used in prompts. Leave empty to use placeID.")]
    public string displayName;

    [Min(0.1f)]
    [Tooltip("How close someone has to be to count as being 'at' this place.")]
    public float areaRadius = 2f;

    public string GetDisplayName()
    {
        return string.IsNullOrWhiteSpace(displayName) ? placeID : displayName;
    }

    public float Distance2D(Vector3 worldPos)
    {
        Vector2 a = new Vector2(transform.position.x, transform.position.z);
        Vector2 b = new Vector2(worldPos.x, worldPos.z);
        return Vector2.Distance(a, b);
    }

    public bool Contains(Vector3 worldPos)
    {
        return Distance2D(worldPos) <= areaRadius;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, areaRadius);
    }
#endif
}