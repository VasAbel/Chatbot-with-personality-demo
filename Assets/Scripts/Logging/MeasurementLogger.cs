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

        EnsureFile(
            "measurement_event_changes.csv",
            "date,time,day,hour,eventId,changeType,conversationId,previousStatus,status,previousParticipantCount,participantCount,previousKnownByCount,knownByCount,organizerCount,attendeeCount,isMultiPerson,eventDate,eventHour,placeId,organizers,attendees,knownBy,description"
        );

        EnsureEventSnapshotFile();
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

    private const string EventSnapshotFileName = "measurement_events_snapshot.csv";

    private void EnsureEventSnapshotFile()
    {
        string path = Path.Combine(basePath, EventSnapshotFileName);

        if (!File.Exists(path))
        {
            File.WriteAllText(
                path,
                "eventId,createdDate,createdGameTimestamp,lastUpdatedGameTimestamp,lastSourceConversationId,status,eventDate,eventHour,placeId,participantCount,organizerCount,attendeeCount,knownByCount,isMultiPerson,organizers,attendees,knownBy,description\n"
            );
        }
    }

    private static int ParticipantCount(SocialEvent socialEvent)
    {
        if (socialEvent == null)
            return 0;

        return (socialEvent.organizers ?? new List<string>())
            .Concat(socialEvent.attendees ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static int KnownByCount(SocialEvent socialEvent)
    {
        return socialEvent?.knownBy?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() ?? 0;
    }

    private static int OrganizerCount(SocialEvent socialEvent)
    {
        return socialEvent?.organizers?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() ?? 0;
    }

    private static int AttendeeCount(SocialEvent socialEvent)
    {
        return socialEvent?.attendees?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() ?? 0;
    }

    private static string JoinNames(IEnumerable<string> names)
    {
        if (names == null)
            return "";

        return string.Join("; ",
            names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
        );
    }

    private static string ExtractDate(string timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
            return "";

        var match = System.Text.RegularExpressions.Regex.Match(
            timestamp,
            @"\d{4}-\d{2}-\d{2}"
        );

        return match.Success ? match.Value : "";
    }

    public void LogEventChange(
        SocialEvent previousEvent,
        SocialEvent currentEvent,
        string changeType,
        string conversationId)
    {
        if (currentEvent == null)
            return;

        int previousParticipantCount = ParticipantCount(previousEvent);
        int participantCount = ParticipantCount(currentEvent);
        int previousKnownByCount = KnownByCount(previousEvent);
        int knownByCount = KnownByCount(currentEvent);

        Append(
            "measurement_event_changes.csv",
            string.Join(",",
                Csv(Date()),
                Csv(Time()),
                Day(),
                Hour(),
                Csv(currentEvent.eventId),
                Csv(changeType),
                Csv(conversationId),
                Csv(previousEvent?.status ?? ""),
                Csv(currentEvent.status ?? ""),
                previousParticipantCount,
                participantCount,
                previousKnownByCount,
                knownByCount,
                OrganizerCount(currentEvent),
                AttendeeCount(currentEvent),
                participantCount > 2 ? 1 : 0,
                Csv(currentEvent.date ?? ""),
                currentEvent.hour.HasValue ? currentEvent.hour.Value.ToString() : "",
                Csv(currentEvent.placeId ?? ""),
                Csv(JoinNames(currentEvent.organizers)),
                Csv(JoinNames(currentEvent.attendees)),
                Csv(JoinNames(currentEvent.knownBy)),
                Csv(currentEvent.description ?? "")
            )
        );
    }

    public void WriteEventSnapshot(IReadOnlyList<SocialEvent> events)
    {
        EnsureEventSnapshotFile();

        string path = Path.Combine(basePath, EventSnapshotFileName);

        var lines = new List<string>
        {
            "eventId,createdDate,createdGameTimestamp,lastUpdatedGameTimestamp,lastSourceConversationId,status,eventDate,eventHour,placeId,participantCount,organizerCount,attendeeCount,knownByCount,isMultiPerson,organizers,attendees,knownBy,description"
        };

        foreach (SocialEvent socialEvent in (events ?? Array.Empty<SocialEvent>())
            .Where(e => e != null)
            .OrderBy(e => e.eventId))
        {
            int participantCount = ParticipantCount(socialEvent);

            lines.Add(
                string.Join(",",
                    Csv(socialEvent.eventId),
                    Csv(ExtractDate(socialEvent.createdGameTimestamp)),
                    Csv(socialEvent.createdGameTimestamp ?? ""),
                    Csv(socialEvent.lastUpdatedGameTimestamp ?? ""),
                    Csv(socialEvent.sourceConversationId ?? ""),
                    Csv(socialEvent.status ?? ""),
                    Csv(socialEvent.date ?? ""),
                    socialEvent.hour.HasValue ? socialEvent.hour.Value.ToString() : "",
                    Csv(socialEvent.placeId ?? ""),
                    participantCount,
                    OrganizerCount(socialEvent),
                    AttendeeCount(socialEvent),
                    KnownByCount(socialEvent),
                    participantCount > 2 ? 1 : 0,
                    Csv(JoinNames(socialEvent.organizers)),
                    Csv(JoinNames(socialEvent.attendees)),
                    Csv(JoinNames(socialEvent.knownBy)),
                    Csv(socialEvent.description ?? "")
                )
            );
        }

        File.WriteAllLines(path, lines);
    }

}