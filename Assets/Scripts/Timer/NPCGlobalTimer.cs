using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class NPCGlobalTimer : MonoBehaviour
{
    public float secondsPerHour = 15f; // 15 seconds = 1 game hour (4x faster than original)

    [Header("Starting in-game date/time")]
    [SerializeField] private int startYear = 2026;
    [SerializeField] private int startMonth = 1;
    [SerializeField] private int startDay = 1;
    [SerializeField] private int startHour = 8;
    private float timer = 0f;
    private DateTime currentDateTime;

    private List<NPC> registeredNPCs = new List<NPC>();

    void Awake()
    {
        currentDateTime = new DateTime(startYear, startMonth, startDay, startHour, 0, 0);
        Debug.Log($"[GameTime] Start: {GetFullTimestamp()}");
    }
    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= secondsPerHour)
        {
            timer = 0f;
            currentDateTime = currentDateTime.AddHours(1);
            NotifyNPCs();
        }
    }

    private void NotifyNPCs()
    {
        foreach (var npc in registeredNPCs)
        {
            npc.OnHourPassed(currentDateTime.Hour);
        }

        Debug.Log($"[GameTime] Advanced to {GetFullTimestamp()} | NPCs notified.");
    }

    public void RegisterNPC(NPC npc)
    {
        if (!registeredNPCs.Contains(npc))
        {
            registeredNPCs.Add(npc);
        }
    }

    public int GetCurrentHour() => currentDateTime.Hour;
    public int GetCurrentDay() => currentDateTime.Day;
    public int GetCurrentMonth() => currentDateTime.Month;
    public int GetCurrentYear() => currentDateTime.Year;
    public DayOfWeek GetCurrentDayOfWeek() => currentDateTime.DayOfWeek;
    public DateTime GetCurrentDateTime() => currentDateTime;

    public string GetCurrentDateString()
    {
        return currentDateTime.ToString("yyyy-MM-dd");
    }

    public string GetCurrentDayOfWeekString()
    {
        return currentDateTime.DayOfWeek.ToString();
    }

    public string GetCurrentHourString()
    {
        return currentDateTime.ToString("HH:mm");
    }

    public string GetFullTimestamp()
    {
        return currentDateTime.ToString("dddd, yyyy-MM-dd HH:mm");
    }

    public string FormatMemoryTimestamp()
    {
        return currentDateTime.ToString("yyyy-MM-dd HH:mm dddd");
    }
}
