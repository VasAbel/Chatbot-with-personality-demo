using System.Collections.Generic;
using UnityEngine;

public class PlaceRegistry : MonoBehaviour
{
    public static PlaceRegistry Instance { get; private set; }

    private Dictionary<string, Transform> places = new Dictionary<string, Transform>();

    void Awake()
    {
        Instance = this;

        PlaceReference[] allPlaces = FindObjectsOfType<PlaceReference>();
        foreach (var place in allPlaces)
        {
            if (!places.ContainsKey(place.placeID))
            {
                places[place.placeID] = place.transform;
            }
        }
    }

    public Transform GetPlaceByName(string name)
    {
        if (places.ContainsKey(name))
        {
            return places[name];
        }

        Debug.LogWarning($"Place '{name}' not found.");
        return null;
    }
}
