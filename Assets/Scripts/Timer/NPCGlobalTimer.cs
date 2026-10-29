using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class NPCGlobalTimer : MonoBehaviour
{
    public float secondsPerHour = 60f;

    [Header("Starting in-game date/time")]
    [SerializeField] private int startYear = 2026;
    [SerializeField] private int startMonth = 1;
    [SerializeField] private int startDay = 1;
    [SerializeField] private int startHour = 8;

    [Header("Automatic experiment stop")]
    [SerializeField] private bool stopAutomatically = true;
    [SerializeField] private int stopYear = 2026;
    [SerializeField] private int stopMonth = 1;
    [SerializeField] private int stopDay = 15;
    [SerializeField] private int stopHour = 0;

    private float timer;
    private DateTime currentDateTime;

    private readonly List<NpcScheduleController> registeredSchedules =
        new List<NpcScheduleController>();

    private void Awake()
    {
        EnsureInitialized();
        Debug.Log($"[GameTime] Start: {GetFullTimestamp()}");
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer < secondsPerHour)
            return;

        timer = 0f;
        currentDateTime = currentDateTime.AddHours(1);

        if (HasReachedAutomaticStop())
        {
            StopSimulation();
            return;
        }

        NotifySchedules();
    }

    public void RegisterSchedule(NpcScheduleController schedule)
    {
        if (schedule != null && !registeredSchedules.Contains(schedule))
            registeredSchedules.Add(schedule);
    }

    public void UnregisterSchedule(NpcScheduleController schedule)
    {
        registeredSchedules.Remove(schedule);
    }

    private bool HasReachedAutomaticStop()
    {
        if (!stopAutomatically)
            return false;

        DateTime stopDateTime = new DateTime(
            stopYear,
            stopMonth,
            stopDay,
            stopHour,
            0,
            0
        );

        return currentDateTime >= stopDateTime;
    }

    private void StopSimulation()
    {
        Debug.Log(
            $"[Simulation Complete] Reached automatic stop time: {GetFullTimestamp()}. " +
            "Stopping the simulation before this hour's schedule notifications."
        );

        try
        {
            string markerPath = Path.Combine(
                SimulationRunContext.RunDirectory,
                "run_completed.txt"
            );

            File.WriteAllText(
                markerPath,
                $"Completed automatically at in-game time: {GetFullTimestamp()}\n" +
                $"Real time: {DateTime.Now:O}\n"
            );
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[Simulation Complete] Could not write completion marker: {ex.Message}"
            );
        }

        ConversationManager conversationManager =
            FindObjectOfType<ConversationManager>();

        conversationManager?.StopAllConversations();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void NotifySchedules()
    {
        NpcScheduleController[] schedules = registeredSchedules.ToArray();

        foreach (NpcScheduleController schedule in schedules)
        {
            if (schedule != null)
                schedule.OnHourPassed(currentDateTime.Hour);
        }

        Debug.Log(
            $"[GameTime] Advanced to {GetFullTimestamp()} | NPC schedules notified."
        );
    }

    private void EnsureInitialized()
    {
        if (currentDateTime == default)
        {
            currentDateTime = new DateTime(
                startYear, startMonth, startDay, startHour, 0, 0
            );
        }
    }

    public int GetCurrentHour() { EnsureInitialized(); return currentDateTime.Hour; }
    public int GetCurrentDay() { EnsureInitialized(); return currentDateTime.Day; }
    public int GetCurrentMonth() { EnsureInitialized(); return currentDateTime.Month; }
    public int GetCurrentYear() { EnsureInitialized(); return currentDateTime.Year; }
    public DayOfWeek GetCurrentDayOfWeek() { EnsureInitialized(); return currentDateTime.DayOfWeek; }
    public DateTime GetCurrentDateTime() { EnsureInitialized(); return currentDateTime; }
    public string GetCurrentDateString() { EnsureInitialized(); return currentDateTime.ToString("yyyy-MM-dd"); }
    public string GetCurrentDayOfWeekString() { EnsureInitialized(); return currentDateTime.DayOfWeek.ToString(); }
    public string GetCurrentHourString() { EnsureInitialized(); return currentDateTime.ToString("HH:mm"); }
    public string GetFullTimestamp() { EnsureInitialized(); return currentDateTime.ToString("dddd, yyyy-MM-dd HH:mm"); }
    public string FormatMemoryTimestamp() { EnsureInitialized(); return currentDateTime.ToString("yyyy-MM-dd HH:mm dddd"); }
}
