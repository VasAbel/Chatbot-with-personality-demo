using System;
using UnityEngine;
using Assets.Game_Manager;

public class NPC : MonoBehaviour
{
    public int idx;
    
    private string npcName;
    internal ConfigManager.Description desc;

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
}
