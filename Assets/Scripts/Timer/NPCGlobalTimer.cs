using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NPCGlobalTimer : MonoBehaviour
{
    public float secondsPerHour = 60f; // 1 minute = 1 game hour
    private float timer = 0f;
    private int currentHour = 8;

    private List<NPC> registeredNPCs = new List<NPC>();

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= secondsPerHour)
        {
            timer = 0f;
            currentHour = (currentHour + 1) % 24;
            NotifyNPCs();
        }
    }

    private void NotifyNPCs()
    {
        foreach (var npc in registeredNPCs)
        {
            npc.OnHourPassed(currentHour);
        }

        Debug.Log($"Hour {currentHour}: NPCs notified.");
    }

    public void RegisterNPC(NPC npc)
    {
        if (!registeredNPCs.Contains(npc))
        {
            registeredNPCs.Add(npc);
        }
    }

    public int GetCurrentHour()
    {
        return currentHour;
    }
}
