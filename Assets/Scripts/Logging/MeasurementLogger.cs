using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class MeasurementLogger : MonoBehaviour
{
    public static MeasurementLogger Instance { get; private set; }

    private string basePath;
    private NPCGlobalTimer gameTimer;

    // Tracks new acquaintances already logged:
    // key = "Amy|Tim"
    private readonly HashSet<string> knownAcquaintanceEdges = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        basePath = Application.persistentDataPath;
        gameTimer = FindObjectOfType<NPCGlobalTimer>();

        EnsureFile(
            "measurement_conversations.csv",
            "date,time,day,hour,conversationId,npc1,npc2,place,npc1Heading,npc2Heading,spokenLineCount,wasSkippedMemoryUpdate"
        );

        EnsureFile(
            "measurement_acquaintances.csv",
            "date,time,day,hour,npc,newAcquaintance,conversationId"
        );

        EnsureFile(
            "measurement_mentions.csv",
            "date,time,day,hour,conversationId,speaker,mentionedNpc,listener"
        );

        EnsureFile(
            "measurement_memory_operations.csv",
            "date,time,day,hour,npc,conversationId,partner,section,socialTarget,addCount,updateCount,removeCount,jsonParseOk"
        );

        EnsureFile(
            "measurement_daily_schedules.csv",
            "date,dayOfWeek,npc,hour0,hour1,hour2,hour3,hour4,hour5,hour6,hour7,hour8,hour9,hour10,hour11,hour12,hour13,hour14,hour15,hour16,hour17,hour18,hour19,hour20,hour21,hour22,hour23"
        );
    }

    private void EnsureFile(string fileName, string header)
    {
        string path = Path.Combine(basePath, fileName);

        if (!File.Exists(path))
        {
            File.WriteAllText(path, header + "\n");
        }
    }

    private void Append(string fileName, string row)
    {
        string path = Path.Combine(basePath, fileName);
        File.AppendAllText(path, row + "\n");
    }

    private string Csv(string value)
    {
        if (value == null) return "";
        value = value.Replace("\"", "\"\"");
        return $"\"{value}\"";
    }

    private NPCGlobalTimer GetGameTimer()
    {
        if (gameTimer == null)
            gameTimer = FindObjectOfType<NPCGlobalTimer>();

        return gameTimer;
    }

    private string Date()
    {
        NPCGlobalTimer timer = GetGameTimer();
        return timer != null ? timer.GetCurrentDateString() : "";
    }

    private string Time()
    {
        NPCGlobalTimer timer = GetGameTimer();
        return timer != null ? timer.GetCurrentHourString() : "";
    }

    private int Hour()
    {
        NPCGlobalTimer timer = GetGameTimer();
        return timer != null ? timer.GetCurrentHour() : -1;
    }

    private int Day()
    {
        NPCGlobalTimer timer = GetGameTimer();
        return timer != null ? timer.GetCurrentDay() : -1;
    }

    private string DayOfWeek()
    {
        NPCGlobalTimer timer = GetGameTimer();
        return timer != null ? timer.GetCurrentDayOfWeekString() : "";
    }

    private static string Heading(NPC npc)
    {
        if (npc == null)
            return "unknown place";

        NpcScheduleController schedule = npc.GetComponent<NpcScheduleController>();
        return schedule != null ? schedule.GetHeadingDisplayName() : "unknown place";
    }

    public void LogConversationFinished(
        NPC npc1,
        NPC npc2,
        string conversationId,
        int spokenLineCount,
        bool wasSkippedMemoryUpdate)
    {
        if (npc1 == null || npc2 == null) return;

        string date = Date();
        string time = Time();
        int day = Day();
        int hour = Hour();

        string place = npc1.GetCurrentAreaName();
        string npc1Heading = Heading(npc1);
        string npc2Heading = Heading(npc2);

        Append(
            "measurement_conversations.csv",
            string.Join(",",
                Csv(date),
                Csv(time),
                day,
                hour,
                Csv(conversationId),
                Csv(npc1.getName()),
                Csv(npc2.getName()),
                Csv(place),
                Csv(npc1Heading),
                Csv(npc2Heading),
                spokenLineCount,
                wasSkippedMemoryUpdate ? 1 : 0
            )
        );
    }

    public void LogNewAcquaintance(NPC self, NPC other, string conversationId)
    {
        if (self == null || other == null) return;

        string edgeKey = $"{self.getName()}|{other.getName()}";

        if (knownAcquaintanceEdges.Contains(edgeKey))
            return;

        knownAcquaintanceEdges.Add(edgeKey);

        Append(
            "measurement_acquaintances.csv",
            string.Join(",",
                Csv(Date()),
                Csv(Time()),
                Day(),
                Hour(),
                Csv(self.getName()),
                Csv(other.getName()),
                Csv(conversationId)
            )
        );
    }

    public void LogNpcMention(
        NPC speaker,
        NPC listener,
        string mentionedNpcName,
        string conversationId)
    {
        if (speaker == null || listener == null) return;

        Append(
            "measurement_mentions.csv",
            string.Join(",",
                Csv(Date()),
                Csv(Time()),
                Day(),
                Hour(),
                Csv(conversationId),
                Csv(speaker.getName()),
                Csv(mentionedNpcName),
                Csv(listener.getName())
            )
        );
    }

    public void LogMemoryOperationCount(
    NPC npc,
    string conversationId,
    string partnerName,
    string section,
    string socialTarget,
    int addCount,
    int updateCount,
    int removeCount,
    bool jsonParseOk)
    {
        if (npc == null) return;

        Append(
            "measurement_memory_operations.csv",
            string.Join(",",
                Csv(Date()),
                Csv(Time()),
                Day(),
                Hour(),
                Csv(npc.getName()),
                Csv(conversationId),
                Csv(partnerName),
                Csv(section),
                Csv(socialTarget ?? ""),
                addCount,
                updateCount,
                removeCount,
                jsonParseOk ? 1 : 0
            )
        );
    }

    public void LogDailySchedule(NPC npc, List<string> schedule)
    {
        if (npc == null || schedule == null)
            return;

        var normalized = schedule
            .Take(24)
            .ToList();

        while (normalized.Count < 24)
            normalized.Add("");

        Append(
            "measurement_daily_schedules.csv",
            string.Join(",",
                new[]
                {
                    Csv(Date()),
                    Csv(DayOfWeek()),
                    Csv(npc.getName())
                }
                .Concat(normalized.Select(Csv))
            )
        );
    }
}