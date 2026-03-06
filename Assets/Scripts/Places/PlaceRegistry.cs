using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PlaceRegistry : MonoBehaviour
{
    public static PlaceRegistry Instance { get; private set; }

    private Dictionary<string, Transform> places = new Dictionary<string, Transform>();
    private readonly Dictionary<string, PlaceReference> placeRefs = new Dictionary<string, PlaceReference>();
    public const string DefaultAreaName = "road";

    void Awake()
    {
        Instance = this;

        places.Clear();
        placeRefs.Clear();

        PlaceReference[] allPlaces = FindObjectsOfType<PlaceReference>();
        foreach (var place in allPlaces)
        {
            if (place == null || string.IsNullOrWhiteSpace(place.placeID))
                continue;

            if (!places.ContainsKey(place.placeID))
            {
                places[place.placeID] = place.transform;
                placeRefs[place.placeID] = place;
            }
            else
            {
                Debug.LogWarning($"Duplicate PlaceReference id found: {place.placeID}");
            }
        }
    }

    public Transform GetPlaceByName(string name)
    {
        if (places.TryGetValue(name, out var t))
            return t;

        Debug.LogWarning($"Place '{name}' not found.");
        return null;
    }

    public PlaceReference GetPlaceReferenceByName(string name)
    {
        if (placeRefs.TryGetValue(name, out var p))
            return p;

        return null;
    }

    public string GetPlaceDisplayName(string placeId)
    {
        var p = GetPlaceReferenceByName(placeId);
        return p != null ? p.GetDisplayName() : placeId;
    }

    public List<string> GetAllPlaceNames()
    {
        return places.Keys.ToList();
    }

    public string DescribePosition(Vector3 worldPos, string fallback = DefaultAreaName)
    {
        PlaceReference bestMatch = null;
        float bestDistance = float.MaxValue;

        foreach (var place in placeRefs.Values)
        {
            if (place == null)
                continue;

            float d = place.Distance2D(worldPos);
            if (d <= place.areaRadius && d < bestDistance)
            {
                bestDistance = d;
                bestMatch = place;
            }
        }

        return bestMatch != null ? bestMatch.GetDisplayName() : fallback;
    }

    public bool IsInsidePlace(string placeId, Vector3 worldPos)
    {
        var place = GetPlaceReferenceByName(placeId);
        return place != null && place.Contains(worldPos);
    }

}
