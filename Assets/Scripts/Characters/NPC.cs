using UnityEngine;
using Assets.Game_Manager;
using System.Collections.Generic;

public class NPC : MonoBehaviour
{
    public int idx;
    
    private string npcName;
    public bool isInConversation = false;
    internal ConfigManager.Description desc;
     public List<string> dailySchedule = new List<string>();

    public void Awake()
    {
        desc = ConfigManager.Instance.GetFullCharacterDescription(idx);
        npcName = ConfigManager.Instance.GetCharacterName(idx);
    }

    public string getName() => npcName;
    public string getDesc() => desc.description;

    public void setName(string value)
    {
        npcName = value;
    }

    public void setDesc(string value)
    {
        // Future implementation if needed
    }

    void Start()
    {
        switch (npcName)
        {
            case "Amy":
                dailySchedule = new List<string>
                {
                    "House", "House", "Townhall", "Townhall", "Townhall", "Well", "Market", "Market",
                    "House", "House", "Market", "Market", "Well", "House", "House", "Market",
                    "Market", "House", "House", "House", "House", "House", "House", "House"
                };
                break;
            case "Tim":
                dailySchedule = new List<string>
                {
                    "House", "House", "Market", "Market", "Market", "Market", "Market", "House",
                    "Market", "Market", "Market", "Market", "Well", "House", "House", "Market",
                    "Townhall", "Townhall", "House", "House", "House", "House", "House", "House"
                };
                break;
            case "Gabriel":
                dailySchedule = new List<string>
                {
                    "House", "House", "Townhall", "Townhall", "Townhall", "Townhall", "Well", "Market",
                    "Townhall", "Townhall", "Townhall", "House", "House", "House", "Market", "Well",
                    "House", "House", "House", "House", "House", "House", "House", "House"
                };
                break;
            default:
                Debug.LogWarning($"No schedule defined for NPC: {npcName}");
                break;
        }

        NPCGlobalTimer timer = FindObjectOfType<NPCGlobalTimer>();
        if (timer != null)
        {
            timer.RegisterNPC(this);
        }
    }

    public string GetCurrentPlace(int hour)
    {
        if (hour >= 0 && hour < dailySchedule.Count)
            return dailySchedule[hour];
        return "House"; // Fallback
    }

    public void OnHourPassed(int hour)
    {
        string placeToGo = dailySchedule[hour % dailySchedule.Count];
        Debug.Log($"{getName()} now heading to: {placeToGo}");
        
        gameObject.GetComponent<NpcMovement>().MoveTo(placeToGo);
    }
}
