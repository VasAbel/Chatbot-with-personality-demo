using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class NPCGlobalTimer : MonoBehaviour
{
    public float secondsPerHour = 60f; // 1 minute = 1 game hour

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
        EnsureInitialized();
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

    private void EnsureInitialized()
    {
        if (currentDateTime == default)
        {
            currentDateTime = new DateTime(startYear, startMonth, startDay, startHour, 0, 0);
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

    public int GetCurrentHour()
    {
        EnsureInitialized();
        return currentDateTime.Hour;
    }

    public int GetCurrentDay()
    {
        EnsureInitialized();
        return currentDateTime.Day;
    }

    public int GetCurrentMonth()
    {
        EnsureInitialized();
        return currentDateTime.Month;
    }

    public int GetCurrentYear()
    {
        EnsureInitialized();
        return currentDateTime.Year;
    }

    public DayOfWeek GetCurrentDayOfWeek()
    {
        EnsureInitialized();
        return currentDateTime.DayOfWeek;
    }

    public DateTime GetCurrentDateTime()
    {
        EnsureInitialized();
        return currentDateTime;
    }

    public string GetCurrentDateString()
    {
        EnsureInitialized();
        return currentDateTime.ToString("yyyy-MM-dd");
    }

    public string GetCurrentDayOfWeekString()
    {
        EnsureInitialized();
        return currentDateTime.DayOfWeek.ToString();
    }

    public string GetCurrentHourString()
    {
        EnsureInitialized();
        return currentDateTime.ToString("HH:mm");
    }

    public string GetFullTimestamp()
    {
        EnsureInitialized();
        return currentDateTime.ToString("dddd, yyyy-MM-dd HH:mm");
    }

    public string FormatMemoryTimestamp()
    {
        EnsureInitialized();
        return currentDateTime.ToString("yyyy-MM-dd HH:mm dddd");
    }
}
