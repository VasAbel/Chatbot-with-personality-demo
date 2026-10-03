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
    // organizers = people hosting/creating the event.
    // attendees = people who are confirmed/expected to participate.
    // knownBy = people who have actually learned about the event.
    public List<string> organizers = new List<string>();
    public List<string> attendees = new List<string>();
    public List<string> knownBy = new List<string>();
    public string placeId;                 // null/empty while unresolved
    public string description;
    public string status = "pending";     // pending | planned

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
        public List<string> organizers;
        public List<string> attendees;
        public List<string> knownBy;
        public string placeId;
        public string description;
    }

    [Serializable]
    private class EventUpdateProposal
    {
        public string eventId;

        // Patch semantics: null means "leave unchanged" for scalar fields.
        public string date;
        public int? hour;
        public string placeId;

        public List<string> addOrganizers;
        public List<string> removeOrganizers;
        public List<string> addAttendees;
        public List<string> removeAttendees;
        public List<string> addKnownBy;

        // Description is preserved unless purposeChanged is explicitly true.
        public bool purposeChanged;
        public string description;
    }

    [Serializable]
    private class EventDelta
    {
        public List<EventProposal> add;
        public List<EventUpdateProposal> update;
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

        e.organizers = NormalizeNameList(e.organizers);
        e.attendees = NormalizeNameList(e.attendees);
        e.knownBy = NormalizeNameList(e.knownBy);

        // Current-schema invariant:
        // organizers are participants, and every participant necessarily knows about the event.
        e.attendees = UnionNames(e.attendees, e.organizers);
        e.knownBy = UnionNames(e.knownBy, e.attendees);

        e.status = IsComplete(e) ? "planned" : "pending";
    }

    private static List<string> NormalizeNameList(IEnumerable<string> names)
    {
        return (names ?? Enumerable.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> UnionNames(params IEnumerable<string>[] groups)
    {
        return NormalizeNameList(groups.Where(g => g != null).SelectMany(g => g));
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
            organizers = e.organizers != null ? new List<string>(e.organizers) : new List<string>(),
            attendees = e.attendees != null ? new List<string>(e.attendees) : new List<string>(),
            knownBy = e.knownBy != null ? new List<string>(e.knownBy) : new List<string>(),
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
        bool hasParticipants = (e.organizers != null && e.organizers.Count > 0) ||
                               (e.attendees != null && e.attendees.Count > 0);

        // planned/pending describes whether logistics are complete, not whether every possible
        // attendee has already heard about or accepted an open event.
        return hasDate && hasHour && hasPlace && hasParticipants && !string.IsNullOrWhiteSpace(e.description);
    }

    private static bool IsKnownToNpc(SocialEvent e, string npcName)
    {
        if (e == null || string.IsNullOrWhiteSpace(npcName))
            return false;

        return IncludesName(e.knownBy, npcName) || IncludesName(e.organizers, npcName) || IncludesName(e.attendees, npcName);
    }

    private static bool IsScheduledParticipant(SocialEvent e, string npcName)
    {
        if (e == null || string.IsNullOrWhiteSpace(npcName))
            return false;

        return IncludesName(e.organizers, npcName) || IncludesName(e.attendees, npcName);
    }

    private static bool IncludesName(List<string> names, string npcName)
    {
        return names != null &&
               !string.IsNullOrWhiteSpace(npcName) &&
               names.Any(a => string.Equals(a, npcName, StringComparison.OrdinalIgnoreCase));
    }

    // Past entries stay in the file for analysis, but are hidden from LLM/runtime context.
    // An unresolved plan without a date cannot be proven overdue, so it remains relevant until resolved.
    private static bool IsPresentOrFuture(SocialEvent e, DateTime now)
    {
        if (e == null)
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
            .Where(e => IsScheduledParticipant(e, npcName))
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
            .Where(e => IsScheduledParticipant(e, npcName))
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

    public string BuildConversationContext(NPC npc, NPC partner, bool partnerIsUser = false)
    {
        if (npc == null)
            return "(none)";

        DateTime now = GetCurrentGameTime();
        string npcName = npc.getName();
        string partnerName = partner != null ? partner.getName() : null;
        string currentArea = npc.GetCurrentAreaName();

        var relevant = SnapshotEvents()
            .Where(e => IsKnownToNpc(e, npcName))
            .Where(e => IsPresentOrFuture(e, now))
            .OrderBy(EventSortTime)
            .ThenBy(e => e.eventId)
            .ToList();

        if (relevant.Count == 0)
            return "- No current, upcoming, or unresolved registered social events are relevant to you.";

        var lines = new List<string>();

        string partnerLabel = partnerIsUser
            ? "the player"
            : (string.IsNullOrWhiteSpace(partnerName) ? "the current conversation partner" : partnerName);

        string partnerGuidance = partnerIsUser
            ? "The player is not represented in organizers/attendees/known-by lists. Do not infer what the player knows or whether they attend from those lists; the player may introduce or authoritatively change event information during the conversation. "
            : $"For each event, if {partnerLabel} is NOT in 'known by', do not speak as if they already know what event you mean: introduce/explain it first if you choose to bring it up. " +
              $"If {partnerLabel} is already in 'known by', you may discuss it as shared knowledge. " +
              $"If you are an organizer or attendee and {partnerLabel} is not, you may invite them when that feels natural and socially appropriate. " +
              $"If you invite them to an already planned event, naturally tell them the known date, time, and place so they know when and where it is. ";

        lines.Add(
            $"- Existing-event conversation guidance: the events below are upcoming events you know about. " +
            $"'Known by' lists who has learned about an event; 'attendees' lists people currently confirmed/expected to participate; 'organizers' lists people responsible for organizing it. " +
            $"These meanings apply to YOUR OWN name too: if you are listed only under 'known by', you know the event exists but you are NOT currently planning or expected to attend. " +
            $"In that case, you may naturally mention or discuss the event, but do not speak as if you will attend, describe what you will do there, or normally invite others on the event's behalf. " +
            $"Your status can change naturally during this conversation: for example, if someone invites you and you clearly accept, you may then talk as someone who plans to attend. " +
            $"Your current conversation partner is {partnerLabel}. " +
            partnerGuidance +
            $"This is only an available conversational possibility, not a task: do not force existing events into the conversation, and do not invite people when the event seems private or the invitation would feel unnatural or unrelated."
        );

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
                bool partnerMatches = IsScheduledParticipant(e, partnerName);

                lines.Add("  " + FormatEventForConversation(e, npcName));
                lines.Add($"    Time matches: YES ({e.hour.Value:00}:00). Current place: {currentArea}. Planned place matches: {(placeMatches ? "YES" : "NO")}.");

                string currentPartner = string.IsNullOrWhiteSpace(partnerName) ? "the player" : partnerName;
                lines.Add($"    Current conversation partner: {currentPartner}. Expected participant matches: {(partnerMatches ? "YES" : "NO")}.");

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

        string organizers = (e.organizers != null && e.organizers.Count > 0) ? string.Join(", ", e.organizers) : "none specified";
        string attendees = (e.attendees != null && e.attendees.Count > 0) ? string.Join(", ", e.attendees) : "none confirmed yet";
        string knownBy = (e.knownBy != null && e.knownBy.Count > 0) ? string.Join(", ", e.knownBy) : "none recorded";

        string selfStatus;
        if (IncludesName(e.organizers, selfName))
        {
            selfStatus = "ORGANIZER - you organize this event and are expected to attend";
        }
        else if (IncludesName(e.attendees, selfName))
        {
            selfStatus = "ATTENDEE - you are currently expected/planning to attend";
        }
        else
        {
            selfStatus = "KNOWS ABOUT ONLY - you know this event exists but are not currently expected/planning to attend";
        }

        return $"[{e.status.ToUpperInvariant()} {e.eventId}] {when} | {place} | {e.description} | " +
               $"YOUR STATUS: {selfStatus} | organizers: {organizers} | attendees: {attendees} | known by: {knownBy}";
    }

    public async Task UpdateEventsFromConversationAsync(ConversationSession session, GptClient client)
    {
        if (session == null || client == null)
            return;

        var transcript = session.GetSpokenTranscript();
        if (transcript == null || transcript.Count < 2)
            return;

        bool isUserConversation = session is UserConversationSession;
        DateTime conversationTime;
        string participantDescription;
        string currentNpcName = null;

        if (session is NPCConversationSession npcSession)
        {
            conversationTime = npcSession.GetConversationDateTime();
            participantDescription = $"{npcSession.GetNPC(0).getName()}, {npcSession.GetNPC(1).getName()}";
        }
        else if (session is UserConversationSession userSession)
        {
            conversationTime = userSession.GetConversationDateTime();
            currentNpcName = userSession.GetNPC().getName();
            participantDescription = $"User, {currentNpcName}";
        }
        else
        {
            Debug.LogWarning($"[EVENTS] Unsupported conversation-session type: {session.GetType().Name}");
            return;
        }

        await eventUpdateSemaphore.WaitAsync();
        try
        {
            DateTime now = GetCurrentGameTime();

            // Keep past events in storage, but do not burden the logger with them.
            // Pending events with no date remain visible because they still need clarification.
            var relevantSnapshot = SnapshotEvents()
                .Where(e => IsPresentOrFuture(e, now))
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
You update a village event registry from ONE conversation.
Return VALID JSON ONLY.

Think in this exact order:

STEP 0 — FIND THE EVENTS

First identify which actual social events or future shared activities are discussed in the CURRENT conversation.

An event means a specific future occurrence that one or more NPCs genuinely intend to happen, for example:
- meeting someone,
- hosting or attending a gathering,
- visiting a place together,
- going on an activity together,
- or carrying out another concrete planned occurrence.

Mere interest, wishes, hypotheticals, or general discussion are NOT events by themselves.
Examples that are NOT enough:
- 'I'd love to go hiking sometime.'
- 'We should do something fun one day.'
- 'Parties are always nice.'
- talking about how enjoyable an activity would be without actually agreeing that it will happen.

For a new event, look for genuine commitment or mutual agreement that the occurrence is intended to happen. Exact date/time/place may still be unresolved; that only makes the event PENDING.

It is completely normal for a conversation to contain NO events at all.
Do NOT force an event to exist just because this is an event-analysis task.
If no real event is discussed, return empty add and update arrays.

A conversation may also mention MULTIPLE distinct events. Identify each future occurrence separately, then handle each one independently in the MATCH / UPDATE / ADD steps below.

STEP 1 — MATCH
For every future occurrence mentioned, first decide whether it is an EXISTING event.
Match by meaning/purpose and conversational reference, not exact wording.
Strong same-event evidence:
- explicit backward reference: 'that gathering', 'the event we discussed', 'our plan from before'
- same underlying purpose
- compatible people/context
If a compatible existing event is being discussed or refined, UPDATE it. Do not ADD a duplicate.
A meeting TO PLAN another event is separate only when the conversation actually arranges a separate future planning meeting.

STEP 2 — UPDATE
For each matched event, output ONLY WHAT CHANGED.
Do not reconstruct the whole event.

People changes:
- accepted/confirmed participation -> addAttendees
- explicitly cannot/will not attend -> removeAttendees
- organizer/host responsibility begins -> addOrganizers
- organizer/host responsibility ends, OR the person cannot attend -> removeOrganizers
- newly learns about the event without joining -> addKnownBy
In this simulation every organizer is also an attendee. Therefore if someone can no longer attend, remove them from BOTH attendees and organizers if present.
Do not remove somebody from knownBy merely because they stop attending; knowledge remains.

Logistics changes:
- date/hour/placeId in an UPDATE mean REPLACE that field with this newly established value.
- Omit/null the field when it did not change.
- Preserve existing logistics automatically by not returning them.
- Do not guess.
- Exact relative references may use existing event data: e.g. 'at the same time as EVT-000015' means the referenced event's exact date/hour if those values exist.

Purpose/description changes:
- Existing description is PRESERVED BY DEFAULT.
- purposeChanged=false means leave description untouched.
- purposeChanged=true only if what the event itself is FOR / what will happen fundamentally changed.
- Attendance changes, missing someone, logistics changes, or extra activity details do NOT by themselves change the event purpose.
- Never rewrite a description as 'X cannot attend...' or other participant-status information.

STEP 3 — ADD
After matching/updating existing events, decide whether the conversation creates any genuinely SEPARATE future occurrence.
One conversation may UPDATE one event and ADD another.
A new event needs a concrete intended future occurrence, not ordinary topic discussion.
Pending events are allowed when logistics are incomplete.
For NPC-NPC conversations, a new shared event requires actual mutual future intent.

For NEW events:
- organizers = NPCs responsible for hosting/organizing it
- attendees = NPCs clearly confirmed/expected to participate
- knownBy = NPCs who actually know it exists
- organizer must also be attendee; attendee must also be knownBy
- description = one short sentence stating the event's concrete PURPOSE/activity, not a wish or uncertainty

Logistics for NEW events:
- date: one exact day in yyyy-MM-dd format OR NULL, explicitly established; broad ranges like 'next weekend' are not exact, but references to specific days like 'next Tuesday' or 'tomorrow' are. In these cases you need to calculate the date relative to the current date.
- hour: one concrete clock hour OR NULL; 'afternoon' is not an exact hour
- placeId: a concrete valid place OR NULL; characters don't necessarily refer to the place by exactly its placeId, for example, Maria saying 'meet me at my home' can mean 'HouseOfMaria'. Use context to understand the place references.
NEVER INVENT logistics that were not established in the conversation. Return null instead.
Add logistics only which are explicitly agreed on in the discussion and can be mapped to ONE EXACT value (date if specific - even if relative - day was confirmed; hour if specific hour was confirmed, place if it is clear what existing place ID they meant).
It is possible to create an event with incomplete logistics, the program will mark those with 'pending' state instead of 'planned'.
For ordinary NPC-NPC conversations, newly proposed logistics require acceptance/confirmation by the other person.
Do not turn 'afternoon' into 14, 'morning' into 9, etc.

STEP 4 — VERIFY
Before returning JSON:
- No duplicate ADD for an event that matched an existing event.
- No invented people or logistics.
- No participant-status sentence used as an event description.
- Event descriptions are purely about the PURPOSE of the meeting.  
- One conversation may contain both UPDATE and ADD operations.
- 'pending' state means logistics are incomplete and NPCs will discuss it further, 'planned' state means everything is set. It is NOT YOUR JOB to set this attribute, it is done by the program.

Return exactly this shape:
{
  ""update"": [
    {
      ""eventId"": ""EVT-000001"",
      ""date"": null,
      ""hour"": null,
      ""placeId"": null,
      ""addOrganizers"": [],
      ""removeOrganizers"": [],
      ""addAttendees"": [],
      ""removeAttendees"": [],
      ""addKnownBy"": [],
      ""purposeChanged"": false,
      ""description"": null
    }
  ],
  ""add"": [
    {
      ""date"": ""yyyy-MM-dd or null"",
      ""hour"": 17,
      ""organizers"": [""Maria""],
      ""attendees"": [""Maria""],
      ""knownBy"": [""Maria""],
      ""placeId"": null,
      ""description"": ""Maria hosts a village gathering.""
    }
  ]
}
Always include update and add arrays, even when empty.
";

            if (isUserConversation)
            {
                system += $@"

USER MODE:
- User is the authoritative simulation controller, not an NPC. Never put User/Player in any event list.
- Only {currentNpcName} directly hears this conversation. Do not add absent NPCs to knownBy/attendees/organizers just because User says to invite or involve them later.
- Explicit User instructions about {currentNpcName}'s own event participation and schedule are authoritative when the NPC accepts them in-character.
- User may create a scheduled obligation/activity involving only {currentNpcName}; it does not need to be a social meeting with another NPC.
- If User says {currentNpcName} cannot attend a matched event, remove {currentNpcName} from attendees AND organizers. Keep them in knownBy.
- If User assigns {currentNpcName} a separate activity at 'the same time' as a matched event, use that matched event's exact date/hour for the NEW activity when available.
- Example: User says Amy cannot attend EVT-X because at the same time she must go to Clinic. -> UPDATE EVT-X removing Amy from attendees/organizers, AND ADD a separate Amy Clinic event using EVT-X's date/hour.
- A request to 'invite everyone' is only an intention for {currentNpcName}; absent villagers are not yet knownBy or attendees.
";
            }

            string user = $@"
Conversation started at: {conversationTime:yyyy-MM-dd HH:mm dddd}
Current game time while registering: {now:yyyy-MM-dd HH:mm dddd}
Conversation ID: {session.conversationID}
Participants in this conversation: {participantDescription}

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
                fallbackJson: @"{""add"":[],""update"":[]}",
                maxTokens: 1000
            );

            Debug.Log($"[EVENTS] Raw logger JSON for {session.conversationID}:\n{raw}");

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
                    {
                        Debug.LogWarning($"[EVENTS] Ignoring update for unknown event {proposal.eventId}.");
                        continue;
                    }

                    if (!TryApplyUpdatePatch(existing, proposal, now, validNpcNames, out bool changed, out string reason))
                    {
                        Debug.LogWarning($"[EVENTS] Ignoring invalid update for {proposal.eventId}: {reason}");
                        continue;
                    }

                    if (!changed)
                        continue;

                    existing.status = IsComplete(existing) ? "planned" : "pending";
                    existing.lastUpdatedGameTimestamp = gameTimestamp;
                    existing.sourceConversationId = session.conversationID;
                    updated++;
                }

                snapshotToSave = events.Select(CloneEvent).ToList();
            }

            if (added > 0 || updated > 0)
            {
                SaveEventsSnapshot(snapshotToSave);
                Debug.Log($"[EVENTS] {session.conversationID}: added={added}, updated={updated}. Registry: {eventFilePath}");
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
        string place = string.IsNullOrWhiteSpace(e?.placeId) ? "null" : e.placeId;
        string organizers = string.Join(", ", e?.organizers ?? new List<string>());
        string attendees = string.Join(", ", e?.attendees ?? new List<string>());
        string knownBy = string.Join(", ", e?.knownBy ?? new List<string>());

        return $"{e?.eventId ?? "(new)"} | status={e?.status ?? "pending"} | date={date} | hour={hour} | place={place} | " +
               $"organizers=[{organizers}] | attendees=[{attendees}] | knownBy=[{knownBy}] | {e?.description}";
    }

    private static bool TryApplyUpdatePatch(
        SocialEvent existing,
        EventUpdateProposal patch,
        DateTime now,
        List<string> validNpcNames,
        out bool changed,
        out string reason)
    {
        changed = false;
        reason = null;

        if (existing == null || patch == null)
        {
            reason = "existing event or patch is null";
            return false;
        }

        // Apply to a clone first so an invalid patch cannot partially mutate the registry.
        var working = CloneEvent(existing);

        bool IsValidNpc(string name) =>
            !string.IsNullOrWhiteSpace(name) &&
            validNpcNames.Any(v => string.Equals(v, name.Trim(), StringComparison.OrdinalIgnoreCase));

        List<string> NormalizeAndValidate(IEnumerable<string> names, string field)
        {
            var list = NormalizeNameList(names);
            foreach (string name in list)
            {
                if (!IsValidNpc(name))
                    throw new InvalidOperationException($"unknown NPC '{name}' in {field}");
            }
            return list;
        }

        try
        {
            var addOrganizers = NormalizeAndValidate(patch.addOrganizers, "addOrganizers");
            var removeOrganizers = NormalizeAndValidate(patch.removeOrganizers, "removeOrganizers");
            var addAttendees = NormalizeAndValidate(patch.addAttendees, "addAttendees");
            var removeAttendees = NormalizeAndValidate(patch.removeAttendees, "removeAttendees");
            var addKnownBy = NormalizeAndValidate(patch.addKnownBy, "addKnownBy");

            // In this simulation organizers are scheduled participants.
            // Explicitly removing attendance therefore also ends organizer status.
            removeOrganizers = UnionNames(removeOrganizers, removeAttendees);

            var organizers = NormalizeNameList(working.organizers);
            var attendees = NormalizeNameList(working.attendees);
            var knownBy = NormalizeNameList(working.knownBy);

            bool RemoveNames(List<string> target, IEnumerable<string> remove)
            {
                int before = target.Count;
                var removeSet = new HashSet<string>(remove ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                target.RemoveAll(x => removeSet.Contains(x));
                return target.Count != before;
            }

            if (RemoveNames(organizers, removeOrganizers)) changed = true;
            if (RemoveNames(attendees, removeAttendees)) changed = true;

            int orgBefore = organizers.Count;
            organizers = UnionNames(organizers, addOrganizers);
            if (organizers.Count != orgBefore) changed = true;

            int attBefore = attendees.Count;
            attendees = UnionNames(attendees, addAttendees, organizers);
            if (attendees.Count != attBefore) changed = true;

            int knownBefore = knownBy.Count;
            knownBy = UnionNames(knownBy, addKnownBy, attendees);
            if (knownBy.Count != knownBefore) changed = true;

            working.organizers = organizers;
            working.attendees = attendees;
            working.knownBy = knownBy;

            if (!string.IsNullOrWhiteSpace(patch.date))
            {
                var parsed = ParseDate(patch.date);
                if (!parsed.HasValue)
                {
                    reason = $"invalid replacement date '{patch.date}'";
                    return false;
                }
                if (parsed.Value.Date < now.Date)
                {
                    reason = "replacement date is already in the past";
                    return false;
                }
                string normalizedDate = parsed.Value.ToString(EventDateFormat);
                if (!string.Equals(working.date, normalizedDate, StringComparison.OrdinalIgnoreCase))
                {
                    working.date = normalizedDate;
                    changed = true;
                }
            }

            if (patch.hour.HasValue)
            {
                if (patch.hour.Value < 0 || patch.hour.Value > 23)
                {
                    reason = $"replacement hour {patch.hour.Value} is outside 0..23";
                    return false;
                }
                if (working.hour != patch.hour)
                {
                    working.hour = patch.hour;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(patch.placeId))
            {
                string place = patch.placeId.Trim();
                if (PlaceRegistry.Instance != null && PlaceRegistry.Instance.GetPlaceReferenceByName(place) == null)
                {
                    reason = $"unknown replacement place '{place}'";
                    return false;
                }
                if (!string.Equals(working.placeId, place, StringComparison.OrdinalIgnoreCase))
                {
                    working.placeId = place;
                    changed = true;
                }
            }

            if (ParseDate(working.date).HasValue && working.hour.HasValue)
            {
                DateTime exact = ParseDate(working.date).Value.AddHours(working.hour.Value);
                if (exact < now)
                {
                    reason = "updated event time would be in the past";
                    return false;
                }
            }

            if (patch.purposeChanged)
            {
                if (string.IsNullOrWhiteSpace(patch.description))
                {
                    reason = "purposeChanged=true requires a non-empty description";
                    return false;
                }

                string description = patch.description.Trim();
                if (!string.Equals(working.description, description, StringComparison.Ordinal))
                {
                    working.description = description;
                    changed = true;
                }
            }

            if (changed)
            {
                existing.date = working.date;
                existing.hour = working.hour;
                existing.organizers = working.organizers;
                existing.attendees = working.attendees;
                existing.knownBy = working.knownBy;
                existing.placeId = working.placeId;
                existing.description = working.description;
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
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

        var organizers = NormalizeNameList(p.organizers);
        var attendees = NormalizeNameList(p.attendees);
        var knownBy = NormalizeNameList(p.knownBy);

        foreach (string name in organizers.Concat(attendees).Concat(knownBy).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!validNpcNames.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)))
            {
                reason = $"unknown NPC '{name}'";
                return false;
            }
        }

        if (organizers.Count == 0 && attendees.Count == 0)
        {
            reason = "event needs at least one organizer or attendee";
            return false;
        }

        // Current-schema invariant:
        // organizers are participants, and every participant necessarily knows about the event.
        attendees = UnionNames(attendees, organizers);
        knownBy = UnionNames(knownBy, attendees);

        if (string.IsNullOrWhiteSpace(p.description))
        {
            reason = "description is missing";
            return false;
        }

        normalized = new SocialEvent
        {
            date = normalizedDate,
            hour = p.hour,
            organizers = organizers,
            attendees = attendees,
            knownBy = knownBy,
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
            if (e == null)
                return false;

            bool sharesPeople = UnionNames(e.organizers, e.attendees, e.knownBy)
                .Any(x => UnionNames(candidate.organizers, candidate.attendees, candidate.knownBy)
                    .Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)));
            if (!sharesPeople)
                return false;

            bool sameDate = string.Equals(e.date ?? "", candidate.date ?? "", StringComparison.OrdinalIgnoreCase);
            bool sameHour = e.hour == candidate.hour;
            bool samePlace = string.Equals(e.placeId ?? "", candidate.placeId ?? "", StringComparison.OrdinalIgnoreCase);

            if (sameDate && sameHour && samePlace)
                return true;

            bool bothVeryIncomplete = string.IsNullOrWhiteSpace(e.date) && !e.hour.HasValue && string.IsNullOrWhiteSpace(e.placeId) &&
                                      string.IsNullOrWhiteSpace(candidate.date) && !candidate.hour.HasValue && string.IsNullOrWhiteSpace(candidate.placeId);
            return bothVeryIncomplete && string.Equals(e.description?.Trim(), candidate.description?.Trim(), StringComparison.OrdinalIgnoreCase);
        });
    }


}
