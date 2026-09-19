using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

[Serializable]
public class SocialEvent
{
    public string eventId;

    // Split date and hour so an event can exist while one of these details is still unresolved.
    // date: yyyy-MM-dd, hour: 0..23. Null/empty means not agreed yet.
    public string date;
    public int? hour;

    public bool isPublic;
    public List<string> attendees = new List<string>();
    public string placeId;                 // null/empty while unresolved
    public string description;
    public string status = "pending";     // pending | planned | cancelled

    // Legacy migration support for the first event-registry version.
    public string dateTime;

    // Useful for debugging / later thesis analysis.
    public string createdGameTimestamp;
    public string lastUpdatedGameTimestamp;
    public string sourceConversationId;
}

public class SocialEventManager : MonoBehaviour
{
    public static SocialEventManager Instance { get; private set; }

    private const string EventFileName = "social_events.json";
    private const string EventDateFormat = "yyyy-MM-dd";
    private const string LegacyDateTimeFormat = "yyyy-MM-dd HH:mm";

    private readonly object eventDataLock = new object();
    private readonly SemaphoreSlim eventUpdateSemaphore = new SemaphoreSlim(1, 1);
    private List<SocialEvent> events = new List<SocialEvent>();
    private string eventFilePath;
    private int nextEventNumber = 1;

    [Serializable]
    private class EventStore
    {
        public List<SocialEvent> events = new List<SocialEvent>();
    }

    [Serializable]
    private class EventProposal
    {
        public string date;
        public int? hour;
        public bool isPublic;
        public List<string> attendees;
        public string placeId;
        public string description;
    }

    [Serializable]
    private class EventUpdateProposal : EventProposal
    {
        public string eventId;
    }

    [Serializable]
    private class EventDelta
    {
        public List<EventProposal> add;
        public List<EventUpdateProposal> update;
        public List<string> cancel;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        eventFilePath = Path.Combine(Application.persistentDataPath, EventFileName);
        LoadEvents();
    }

    private void LoadEvents()
    {
        if (!File.Exists(eventFilePath))
        {
            events = new List<SocialEvent>();
            SaveEventsSnapshot(events);
            return;
        }

        try
        {
            string json = File.ReadAllText(eventFilePath);
            var store = JsonConvert.DeserializeObject<EventStore>(json);
            events = store?.events ?? new List<SocialEvent>();

            foreach (var e in events)
                NormalizeLoadedEvent(e);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EVENTS] Failed to load {EventFileName}: {ex.Message}. Starting with an empty event registry.");
            events = new List<SocialEvent>();
        }

        nextEventNumber = events
            .Select(e => ParseEventNumber(e?.eventId))
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static void NormalizeLoadedEvent(SocialEvent e)
    {
        if (e == null)
            return;

        // Migrate entries written by the first version, which used one dateTime string.
        if ((string.IsNullOrWhiteSpace(e.date) || !e.hour.HasValue) &&
            !string.IsNullOrWhiteSpace(e.dateTime) &&
            DateTime.TryParseExact(
                e.dateTime.Trim(),
                LegacyDateTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime legacy))
        {
            e.date = legacy.ToString(EventDateFormat);
            e.hour = legacy.Hour;
        }

        e.dateTime = null;
        e.attendees ??= new List<string>();
        e.attendees = e.attendees
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.Equals(e.status, "cancelled", StringComparison.OrdinalIgnoreCase))
            e.status = IsComplete(e) ? "planned" : "pending";
    }

    private static int ParseEventNumber(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return 0;

        const string prefix = "EVT-";
        if (!eventId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return 0;

        return int.TryParse(eventId.Substring(prefix.Length), out int n) ? n : 0;
    }

    private string NextEventId() => $"EVT-{nextEventNumber++:000000}";

    private static SocialEvent CloneEvent(SocialEvent e)
    {
        if (e == null) return null;

        return new SocialEvent
        {
            eventId = e.eventId,
            date = e.date,
            hour = e.hour,
            isPublic = e.isPublic,
            attendees = e.attendees != null ? new List<string>(e.attendees) : new List<string>(),
            placeId = e.placeId,
            description = e.description,
            status = e.status,
            createdGameTimestamp = e.createdGameTimestamp,
            lastUpdatedGameTimestamp = e.lastUpdatedGameTimestamp,
            sourceConversationId = e.sourceConversationId
        };
    }

    private List<SocialEvent> SnapshotEvents()
    {
        lock (eventDataLock)
        {
            return events.Select(CloneEvent).Where(e => e != null).ToList();
        }
    }

    private void SaveEventsSnapshot(List<SocialEvent> snapshot)
    {
        try
        {
            var store = new EventStore
            {
                events = snapshot
                    .OrderBy(EventSortTime)
                    .ThenBy(e => e.eventId)
                    .ToList()
            };

            string json = JsonConvert.SerializeObject(
                store,
                Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );
            File.WriteAllText(eventFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EVENTS] Failed to save {EventFileName}: {ex.Message}");
        }
    }

    private static DateTime? ParseDate(string value)
    {
        if (DateTime.TryParseExact(
            value?.Trim(),
            EventDateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime parsed))
        {
            return parsed.Date;
        }

        return null;
    }

    private static DateTime EventSortTime(SocialEvent e)
    {
        var d = ParseDate(e?.date);
        if (!d.HasValue)
            return DateTime.MaxValue;

        return d.Value.AddHours(e?.hour ?? 23);
    }

    private static string SanitizeJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "{}";

        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
            raw = raw.Substring(start, end - start + 1);

        return raw.Replace('“', '"').Replace('”', '"').Trim();
    }

    private DateTime GetCurrentGameTime()
    {
        var timer = FindObjectOfType<NPCGlobalTimer>();
        return timer != null ? timer.GetCurrentDateTime() : DateTime.Now;
    }

    private string GetCurrentGameTimestamp()
    {
        var timer = FindObjectOfType<NPCGlobalTimer>();
        return timer != null ? timer.GetFullTimestamp() : DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    private static bool IsCancelled(SocialEvent e) =>
        e != null && string.Equals(e.status, "cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlanned(SocialEvent e) =>
        e != null && string.Equals(e.status, "planned", StringComparison.OrdinalIgnoreCase);

    private static bool IsPending(SocialEvent e) =>
        e != null && string.Equals(e.status, "pending", StringComparison.OrdinalIgnoreCase);

    private static bool IsComplete(SocialEvent e)
    {
        if (e == null)
            return false;

        bool hasDate = ParseDate(e.date).HasValue;
        bool hasHour = e.hour.HasValue && e.hour.Value >= 0 && e.hour.Value <= 23;
        bool hasPlace = !string.IsNullOrWhiteSpace(e.placeId);
        bool hasPeople = e.isPublic || (e.attendees != null && e.attendees.Count >= 2);

        return hasDate && hasHour && hasPlace && hasPeople && !string.IsNullOrWhiteSpace(e.description);
    }

    private static bool IsRelevantToNpc(SocialEvent e, string npcName)
    {
        if (e == null || string.IsNullOrWhiteSpace(npcName))
            return false;

        if (e.isPublic)
            return true;

        return e.attendees != null &&
               e.attendees.Any(a => string.Equals(a, npcName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IncludesNpc(SocialEvent e, string npcName)
    {
        return e?.attendees != null &&
               !string.IsNullOrWhiteSpace(npcName) &&
               e.attendees.Any(a => string.Equals(a, npcName, StringComparison.OrdinalIgnoreCase));
    }

    // Past entries stay in the file for analysis, but are hidden from LLM/runtime context.
    // An unresolved plan without a date cannot be proven overdue, so it remains relevant until resolved/cancelled.
    private static bool IsPresentOrFuture(SocialEvent e, DateTime now)
    {
        if (e == null || IsCancelled(e))
            return false;

        var date = ParseDate(e.date);
        if (!date.HasValue)
            return true;

        if (date.Value.Date > now.Date)
            return true;

        if (date.Value.Date < now.Date)
            return false;

        // Same day. If the exact hour is unresolved, it can still be clarified today.
        return !e.hour.HasValue || e.hour.Value >= now.Hour;
    }

    public string BuildScheduleContext(string npcName, DateTime now)
    {
        var relevant = SnapshotEvents()
            .Where(IsPlanned)
            .Where(e => IsRelevantToNpc(e, npcName))
            .Where(IsComplete)
            .Where(e => ParseDate(e.date)?.Date == now.Date)
            .Where(e => e.hour.HasValue && e.hour.Value >= now.Hour)
            .OrderBy(e => e.hour.Value)
            .ToList();

        if (relevant.Count == 0)
            return "(none)";

        return string.Join("\n", relevant.Select(e =>
            $"- {e.date} {e.hour.Value:00}:00 -> {e.placeId}"));
    }

    public void ApplyEventsToSchedule(string npcName, DateTime now, List<string> schedule)
    {
        if (schedule == null || schedule.Count < 24)
            return;

        var relevant = SnapshotEvents()
            .Where(IsPlanned)
            .Where(e => IsRelevantToNpc(e, npcName))
            .Where(IsComplete)
            .Where(e => ParseDate(e.date)?.Date == now.Date)
            .Where(e => e.hour.HasValue && e.hour.Value >= now.Hour)
            .OrderBy(e => e.hour.Value)
            .ThenBy(e => e.eventId)
            .ToList();

        foreach (var hourGroup in relevant.GroupBy(e => e.hour.Value))
        {
            var first = hourGroup.First();

            if (hourGroup.Select(e => e.placeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                Debug.LogWarning(
                    $"[EVENTS] {npcName} has multiple planned events in hour {hourGroup.Key}:00 at different places. " +
                    $"Using {first.eventId} -> {first.placeId} for the schedule."
                );
            }

            if (PlaceRegistry.Instance != null &&
                PlaceRegistry.Instance.GetPlaceReferenceByName(first.placeId) == null)
            {
                Debug.LogWarning($"[EVENTS] Event {first.eventId} uses unknown place '{first.placeId}', so it was not applied to {npcName}'s schedule.");
                continue;
            }

            schedule[hourGroup.Key] = first.placeId;
        }
    }

    public string BuildConversationContext(NPC npc, NPC partner)
    {
        if (npc == null)
            return "(none)";

        DateTime now = GetCurrentGameTime();
        string npcName = npc.getName();
        string partnerName = partner != null ? partner.getName() : null;
        string currentArea = npc.GetCurrentAreaName();

        var relevant = SnapshotEvents()
            .Where(e => !IsCancelled(e))
            .Where(e => IsRelevantToNpc(e, npcName))
            .Where(e => IsPresentOrFuture(e, now))
            .OrderBy(EventSortTime)
            .ThenBy(e => e.eventId)
            .ToList();

        if (relevant.Count == 0)
            return "- No current, upcoming, or unresolved registered social events are relevant to you.";

        var lines = new List<string>();

        // First inspect every fully planned event that is scheduled for THIS exact game hour.
        var scheduledNow = relevant
            .Where(IsPlanned)
            .Where(IsComplete)
            .Where(e => ParseDate(e.date)?.Date == now.Date)
            .Where(e => e.hour == now.Hour)
            .ToList();

        if (scheduledNow.Count > 0)
        {
            lines.Add("- Planned-event situation at the current time:");

            foreach (var e in scheduledNow)
            {
                bool placeMatches = PlaceRegistry.Instance != null &&
                                    PlaceRegistry.Instance.IsInsidePlace(e.placeId, npc.transform.position);
                bool partnerMatches = e.isPublic || IncludesNpc(e, partnerName);

                lines.Add("  " + FormatEventForConversation(e, npcName));
                lines.Add($"    Time matches: YES ({e.hour.Value:00}:00). Current place: {currentArea}. Planned place matches: {(placeMatches ? "YES" : "NO")}.");

                if (e.isPublic)
                {
                    lines.Add("    This is public, so the current conversation partner does not need to be a specific attendee.");
                }
                else
                {
                    string currentPartner = string.IsNullOrWhiteSpace(partnerName) ? "the player" : partnerName;
                    lines.Add($"    Current conversation partner: {currentPartner}. Planned attendee matches: {(partnerMatches ? "YES" : "NO")}.");
                }

                if (placeMatches && partnerMatches)
                {
                    lines.Add("    Interpretation: this conversation is happening as the planned event intended.");
                }
                else if (!placeMatches && partnerMatches)
                {
                    lines.Add("    Interpretation: you met the planned attendee at the planned time, but not at the planned place. You may naturally notice or mention that.");
                }
                else if (placeMatches && !partnerMatches)
                {
                    lines.Add("    Interpretation: you are at the planned place and time, but you are currently talking to someone other than the planned attendee. You may mention whom you were expecting.");
                }
                else
                {
                    lines.Add("    Interpretation: you have a planned event right now, but you are currently elsewhere and talking to someone else. You may mention where/who you were supposed to meet.");
                }
            }
        }

        var pending = relevant.Where(IsPending).Take(6).ToList();
        if (pending.Count > 0)
        {
            lines.Add("- Unresolved social plans relevant to you:");
            foreach (var e in pending)
            {
                lines.Add("  " + FormatEventForConversation(e, npcName));
                lines.Add($"    Missing details: {string.Join(", ", GetMissingFields(e))}. If natural, you may bring these missing details up so the plan can be completed.");
            }
        }

        var otherUpcoming = relevant
            .Where(IsPlanned)
            .Where(e => !scheduledNow.Any(nowEvent => nowEvent.eventId == e.eventId))
            .Take(6)
            .ToList();

        if (otherUpcoming.Count > 0)
        {
            lines.Add("- Other upcoming planned events relevant to you:");
            foreach (var e in otherUpcoming)
                lines.Add("  " + FormatEventForConversation(e, npcName));
        }

        return string.Join("\n", lines);
    }

    private static List<string> GetMissingFields(SocialEvent e)
    {
        var missing = new List<string>();
        if (!ParseDate(e?.date).HasValue) missing.Add("date");
        if (e == null || !e.hour.HasValue) missing.Add("hour");
        if (e == null || string.IsNullOrWhiteSpace(e.placeId)) missing.Add("place");
        return missing;
    }

    private static string FormatEventForConversation(SocialEvent e, string selfName)
    {
        string people;
        if (e.isPublic)
        {
            people = "PUBLIC (all villagers invited)";
        }
        else
        {
            var others = (e.attendees ?? new List<string>())
                .Where(a => !string.Equals(a, selfName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            people = others.Count > 0 ? "with " + string.Join(", ", others) : "private event";
        }

        string when = $"{(string.IsNullOrWhiteSpace(e.date) ? "DATE NOT AGREED" : e.date)} " +
                      $"{(e.hour.HasValue ? e.hour.Value.ToString("00") + ":00" : "TIME NOT AGREED")}";

        string place;
        if (string.IsNullOrWhiteSpace(e.placeId))
        {
            place = "PLACE NOT AGREED";
        }
        else
        {
            string displayPlace = PlaceRegistry.Instance != null
                ? PlaceRegistry.Instance.GetPlaceDisplayName(e.placeId)
                : e.placeId;
            place = $"{displayPlace} ({e.placeId})";
        }

        return $"[{e.status.ToUpperInvariant()} {e.eventId}] {when} | {place} | {people} | {e.description}";
    }

    public async Task UpdateEventsFromConversationAsync(NPCConversationSession session, GptClient client)
    {
        if (session == null || client == null)
            return;

        var transcript = session.GetSpokenTranscript();
        if (transcript == null || transcript.Count < 2)
            return;

        await eventUpdateSemaphore.WaitAsync();
        try
        {
            DateTime now = GetCurrentGameTime();
            DateTime conversationTime = session.GetConversationDateTime();

            // Keep past events in storage, but do not burden the logger with them.
            // Pending events with no date remain visible because they still need clarification.
            var relevantSnapshot = SnapshotEvents()
                .Where(e => IsCancelled(e) || IsPresentOrFuture(e, now))
                .Where(e =>
                {
                    var d = ParseDate(e.date);
                    return !d.HasValue || d.Value.Date >= now.Date;
                })
                .OrderBy(EventSortTime)
                .ThenBy(e => e.eventId)
                .ToList();

            string existingEvents = relevantSnapshot.Count == 0
                ? "(none)"
                : string.Join("\n", relevantSnapshot.Select(FormatEventForLogger));

            var registry = PlaceRegistry.Instance;
            string places = registry == null
                ? "(none)"
                : string.Join("\n", registry.GetAllPlaceNames()
                    .OrderBy(id => id)
                    .Select(id => $"- {id} = {registry.GetPlaceDisplayName(id)}"));

            var validNpcNames = FindObjectsOfType<NPC>()
                .Select(n => n.getName())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            string npcNames = string.Join(", ", validNpcNames);

            string system = @"
You are the social-event registry updater for a village simulation.
Your only task is to detect social plans that were created, clarified, changed, reconfirmed, or cancelled in the CURRENT conversation.
Reply with VALID JSON ONLY. No markdown and no commentary.

There are two kinds of stored events:
1. PLANNED: date, whole-hour time, place, and participants/public scope are all known.
2. PENDING: the NPCs clearly agreed that they intend to arrange/attend a real future social meeting, but one or more of date, hour, or place is still unresolved.

IMPORTANT:
- Store a PENDING event when there is clear mutual intent to make a real future plan, even if some logistics are missing.
- Do NOT create an event from a one-sided suggestion, casual wish, joke, vague non-committal comment, or merely because the current topic could be continued later.

EVENT CREATION THRESHOLD — VERY IMPORTANT:
Create a NEW event only when the conversation contains an explicit intention for people to meet, gather, visit each other, do an activity together, or attend something together in the future.
The conversation must contain evidence that the shared future interaction itself was proposed and accepted or mutually intended.

Do NOT infer an event merely because:
- the NPCs are discussing an interesting topic,
- they mention a place they like or plan to visit individually,
- one NPC says they enjoy talking with the other,
- they mention a future activity without inviting the other person,
- the topic could naturally be continued later,
- it would simply make sense for them to meet again.

Never invent a catch-up, continuation discussion, or future visit unless the NPCs themselves expressed an intention to do something together.

A future meeting can still count even when date, hour, or place are missing. The important requirement is explicit shared future intent, not complete logistics.
Mutual intent does not require formal words like 'meet' or 'appointment'. A proposed shared activity plus clear acceptance is enough.

Examples that ARE events:
- 'We should meet again sometime.' / 'I'd like that.'
- 'Maybe I could photograph your woodworking projects someday.' / 'I'd love that.'
- 'Want to come by my shop this weekend?' / 'Sure.'
- 'Let's talk about this more over coffee sometime.' / 'Absolutely.'

Examples that are NOT events:
- 'I like Maria's cafe.' / 'Me too.'
- 'I'm going to Maria's cafe later.' / 'Their pastries are great.'
- 'I'd love to hear more about your work.' / 'Thanks!'
- A normal conversation about books, coffee, work, hobbies, or another topic, even if continuing it later would make sense.

When uncertain whether an actual future shared interaction was intended, prefer NOT creating a new event.

- Missing fields must be JSON null. Never invent them.
- If a relative date is actually specified (for example 'tomorrow' or 'next Friday'), resolve it to an exact yyyy-MM-dd date using the supplied conversation timestamp.
- hour is an integer from 0 to 23 and represents HH:00. If no whole-hour time was agreed, use null.
- placeId must be one of the provided place IDs exactly. If no valid place was agreed, use null.
- For a PRIVATE event, isPublic=false and attendees must contain every named NPC expected to attend.
- For a PUBLIC event, isPublic=true and attendees must be an empty array; public means every NPC is invited.
- Use the known NPC names and the two conversation participants; identifying the participants of their own agreed plan is not invention.
- The description must summarize the FUTURE activity or meeting that was actually proposed. Do not simply summarize the CURRENT conversation topic.
- If an existing pending event gets missing details clarified, UPDATE that event rather than adding another one.
- If an existing event is rescheduled or otherwise changed, UPDATE it instead of adding a duplicate.
- If an existing event is explicitly cancelled, put its eventId in cancel.
- Do not modify events unrelated to the current conversation.

For add/update, return the FULL current state of the event. Known fields must be preserved; unresolved fields must be null.
The program itself decides whether the resulting event is pending or planned.

Return exactly this JSON shape:
{
  ""add"": [
    {
      ""date"": ""yyyy-MM-dd or null"",
      ""hour"": 17,
      ""isPublic"": false,
      ""attendees"": [""NPC1"", ""NPC2""],
      ""placeId"": ""PLACE_ID or null"",
      ""description"": ""one short sentence""
    }
  ],
  ""update"": [
    {
      ""eventId"": ""EVT-000001"",
      ""date"": ""yyyy-MM-dd or null"",
      ""hour"": null,
      ""isPublic"": false,
      ""attendees"": [""NPC1"", ""NPC2""],
      ""placeId"": null,
      ""description"": ""one short sentence""
    }
  ],
  ""cancel"": [""EVT-000001""]
}

Always include add, update, and cancel arrays, even when empty.";

            string user = $@"
Conversation started at: {conversationTime:yyyy-MM-dd HH:mm dddd}
Current game time while registering: {now:yyyy-MM-dd HH:mm dddd}
Conversation ID: {session.conversationID}
NPCs in this conversation: {session.GetNPC(0).getName()}, {session.GetNPC(1).getName()}

Valid NPC names:
{npcNames}

Valid places:
{places}

Existing PRESENT/FUTURE/PENDING events only:
{existingEvents}

CURRENT conversation transcript:
{string.Join("\n", transcript)}

Return only the event-operation JSON.";

            string raw = await client.RequestGenericJsonAsync(
                system,
                user,
                fallbackJson: @"{""add"":[],""update"":[],""cancel"":[]}",
                maxTokens: 850
            );

            raw = SanitizeJson(raw);

            EventDelta delta;
            try
            {
                delta = JsonConvert.DeserializeObject<EventDelta>(raw);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EVENTS] Could not parse event logger JSON for {session.conversationID}: {ex.Message}\nRaw: {raw}");
                return;
            }

            if (delta == null)
                return;

            int added = 0;
            int updated = 0;
            int cancelled = 0;
            string gameTimestamp = GetCurrentGameTimestamp();
            List<SocialEvent> snapshotToSave;

            lock (eventDataLock)
            {
                foreach (var proposal in delta.add ?? new List<EventProposal>())
                {
                    if (!TryNormalizeProposal(proposal, now, validNpcNames, out var normalized, out string reason))
                    {
                        Debug.LogWarning($"[EVENTS] Ignoring invalid new event: {reason}");
                        continue;
                    }

                    if (LooksLikeDuplicate(events, normalized))
                    {
                        Debug.LogWarning($"[EVENTS] Ignoring duplicate event proposed by logger: {FormatEventForLogger(normalized)}");
                        continue;
                    }

                    normalized.eventId = NextEventId();
                    normalized.status = IsComplete(normalized) ? "planned" : "pending";
                    normalized.createdGameTimestamp = gameTimestamp;
                    normalized.lastUpdatedGameTimestamp = gameTimestamp;
                    normalized.sourceConversationId = session.conversationID;
                    events.Add(normalized);
                    added++;
                }

                foreach (var proposal in delta.update ?? new List<EventUpdateProposal>())
                {
                    if (proposal == null || string.IsNullOrWhiteSpace(proposal.eventId))
                        continue;

                    var existing = events.FirstOrDefault(e =>
                        string.Equals(e.eventId, proposal.eventId.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                        continue;

                    if (!TryNormalizeProposal(proposal, now, validNpcNames, out var normalized, out string reason))
                    {
                        Debug.LogWarning($"[EVENTS] Ignoring invalid update for {proposal.eventId}: {reason}");
                        continue;
                    }

                    existing.date = normalized.date;
                    existing.hour = normalized.hour;
                    existing.isPublic = normalized.isPublic;
                    existing.attendees = normalized.attendees;
                    existing.placeId = normalized.placeId;
                    existing.description = normalized.description;
                    existing.status = IsComplete(existing) ? "planned" : "pending";
                    existing.lastUpdatedGameTimestamp = gameTimestamp;
                    existing.sourceConversationId = session.conversationID;
                    existing.dateTime = null;
                    updated++;
                }

                foreach (string eventId in delta.cancel ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(eventId))
                        continue;

                    var existing = events.FirstOrDefault(e =>
                        string.Equals(e.eventId, eventId.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (existing == null || IsCancelled(existing))
                        continue;

                    existing.status = "cancelled";
                    existing.lastUpdatedGameTimestamp = gameTimestamp;
                    existing.sourceConversationId = session.conversationID;
                    cancelled++;
                }

                snapshotToSave = events.Select(CloneEvent).ToList();
            }

            if (added > 0 || updated > 0 || cancelled > 0)
            {
                SaveEventsSnapshot(snapshotToSave);
                Debug.Log($"[EVENTS] {session.conversationID}: added={added}, updated={updated}, cancelled={cancelled}. Registry: {eventFilePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EVENTS] Event update failed: {ex.Message}");
        }
        finally
        {
            eventUpdateSemaphore.Release();
        }
    }

    private static string FormatEventForLogger(SocialEvent e)
    {
        string date = string.IsNullOrWhiteSpace(e?.date) ? "null" : e.date;
        string hour = e?.hour.HasValue == true ? e.hour.Value.ToString("00") + ":00" : "null";
        string people = e?.isPublic == true
            ? "PUBLIC"
            : string.Join(", ", e?.attendees ?? new List<string>());
        string place = string.IsNullOrWhiteSpace(e?.placeId) ? "null" : e.placeId;

        return $"{e?.eventId ?? "(new)"} | status={e?.status ?? "pending"} | date={date} | hour={hour} | people={people} | place={place} | {e?.description}";
    }

    private static bool TryNormalizeProposal(
        EventProposal p,
        DateTime now,
        List<string> validNpcNames,
        out SocialEvent normalized,
        out string reason)
    {
        normalized = null;
        reason = null;

        if (p == null)
        {
            reason = "proposal is null";
            return false;
        }

        string normalizedDate = null;
        if (!string.IsNullOrWhiteSpace(p.date))
        {
            var parsedDate = ParseDate(p.date);
            if (!parsedDate.HasValue)
            {
                reason = $"invalid date '{p.date}'";
                return false;
            }

            if (parsedDate.Value.Date < now.Date)
            {
                reason = "date is already in the past";
                return false;
            }

            normalizedDate = parsedDate.Value.ToString(EventDateFormat);
        }

        if (p.hour.HasValue && (p.hour.Value < 0 || p.hour.Value > 23))
        {
            reason = $"hour {p.hour.Value} is outside 0..23";
            return false;
        }

        if (normalizedDate != null && p.hour.HasValue)
        {
            DateTime exact = ParseDate(normalizedDate).Value.AddHours(p.hour.Value);
            if (exact < now)
            {
                reason = "exact event time is already in the past";
                return false;
            }
        }

        string normalizedPlace = string.IsNullOrWhiteSpace(p.placeId) ? null : p.placeId.Trim();
        if (normalizedPlace != null && PlaceRegistry.Instance != null &&
            PlaceRegistry.Instance.GetPlaceReferenceByName(normalizedPlace) == null)
        {
            reason = $"unknown place '{normalizedPlace}'";
            return false;
        }

        var attendees = (p.attendees ?? new List<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (p.isPublic)
        {
            attendees.Clear();
        }
        else
        {
            if (attendees.Count < 2)
            {
                reason = "private event needs at least two attendees";
                return false;
            }

            foreach (string attendee in attendees)
            {
                if (!validNpcNames.Any(v => string.Equals(v, attendee, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = $"unknown attendee '{attendee}'";
                    return false;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(p.description))
        {
            reason = "description is missing";
            return false;
        }

        normalized = new SocialEvent
        {
            date = normalizedDate,
            hour = p.hour,
            isPublic = p.isPublic,
            attendees = attendees,
            placeId = normalizedPlace,
            description = p.description.Trim(),
            status = "pending"
        };
        normalized.status = IsComplete(normalized) ? "planned" : "pending";
        return true;
    }

    private static bool LooksLikeDuplicate(List<SocialEvent> all, SocialEvent candidate)
    {
        if (candidate == null)
            return false;

        return all.Any(e =>
        {
            if (e == null || IsCancelled(e))
                return false;

            if (e.isPublic != candidate.isPublic)
                return false;

            bool samePeople = e.isPublic || SameAttendeeSet(e.attendees, candidate.attendees);
            if (!samePeople)
                return false;

            bool sameDate = string.Equals(e.date ?? "", candidate.date ?? "", StringComparison.OrdinalIgnoreCase);
            bool sameHour = e.hour == candidate.hour;
            bool samePlace = string.Equals(e.placeId ?? "", candidate.placeId ?? "", StringComparison.OrdinalIgnoreCase);

            if (sameDate && sameHour && samePlace)
                return true;

            // If no logistics have been agreed yet, also prevent duplicate copies of the same intent.
            bool bothVeryIncomplete = string.IsNullOrWhiteSpace(e.date) && !e.hour.HasValue && string.IsNullOrWhiteSpace(e.placeId) &&
                                      string.IsNullOrWhiteSpace(candidate.date) && !candidate.hour.HasValue && string.IsNullOrWhiteSpace(candidate.placeId);
            return bothVeryIncomplete && string.Equals(e.description?.Trim(), candidate.description?.Trim(), StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool SameAttendeeSet(List<string> a, List<string> b)
    {
        a ??= new List<string>();
        b ??= new List<string>();

        return a.Count == b.Count &&
               a.All(x => b.Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)));
    }
}
