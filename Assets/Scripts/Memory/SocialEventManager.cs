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
    private class EventUpdateProposal : EventProposal
    {
        public string eventId;
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

    public string BuildConversationContext(NPC npc, NPC partner)
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

        string partnerLabel = string.IsNullOrWhiteSpace(partnerName) ? "the player" : partnerName;
        lines.Add(
            $"- Existing-event conversation guidance: the events below are upcoming events you know about. " +
            $"'Known by' lists who has learned about an event; 'attendees' lists people currently confirmed/expected to participate; 'organizers' lists people responsible for organizing it. " +
            $"These meanings apply to YOUR OWN name too: if you are listed only under 'known by', you know the event exists but you are NOT currently planning or expected to attend. " +
            $"In that case, you may naturally mention or discuss the event, but do not speak as if you will attend, describe what you will do there, or normally invite others on the event's behalf. " +
            $"Your status can change naturally during this conversation: for example, if someone invites you and you clearly accept, you may then talk as someone who plans to attend. " +
            $"Your current conversation partner is {partnerLabel}. " +
            $"For each event, if {partnerLabel} is NOT in 'known by', do not speak as if they already know what event you mean: introduce/explain it first if you choose to bring it up. " +
            $"If {partnerLabel} is already in 'known by', you may discuss it as shared knowledge. " +
            $"If you are an organizer or attendee and {partnerLabel} is not, you may invite them when that feels natural and socially appropriate. " +
            $"If you invite them to an already planned event, naturally tell them the known date, time, and place so they know when and where it is. " +
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
You are the social-event registry updater for a village simulation.
Your only task is to maintain the shared event registry from the CURRENT conversation.
Reply with VALID JSON ONLY. No markdown and no commentary.

MAIN DECISION PROCESS — FOLLOW IN THIS ORDER:

STEP 1 — DID THE CONVERSATION ACTUALLY INVOLVE A REAL SOCIAL EVENT OR FUTURE SHARED ACTIVITY?
First decide whether the CURRENT conversation actually:
- created a new future meeting/activity,
- referred to an existing event,
- clarified or changed an existing event,
- told somebody about an existing event,
- invited somebody to an existing event,
- or established that somebody will or will not attend an existing event.

If none of those happened, return empty add/update arrays.

Do NOT create an event merely because:
- the speakers discussed an interesting topic,
- they mentioned a place they like,
- one person said they may go somewhere individually,
- they enjoyed the conversation,
- or continuing the topic later would make sense.

A NEW event requires explicit shared future intent. There must be evidence that the people actually intend to meet, gather, visit, attend, or do something together.
The event may still be created as PENDING because date, hour, or place is unresolved.

Examples that ARE enough for a new event:
- 'We should meet again sometime.' / 'I'd like that.'
- 'Maybe I could photograph your woodworking projects someday.' / 'I'd love that.'
- 'Want to come by my shop this weekend?' / 'Sure.'
- 'Let's talk about this more over coffee sometime.' / 'Absolutely.'

Examples that are NOT events:
- 'I like Maria's cafe.' / 'Me too.'
- 'I'm going to Maria's cafe later.' / 'Their pastries are great.'
- 'I'd love to hear more about your work.' / 'Thanks!'
- Normal discussion of books, work, food, hobbies, or places.

When uncertain whether a future shared interaction was actually intended, prefer NO new event.

STEP 2 — IS THIS A NEW EVENT OR AN EXISTING ONE?
Before adding anything, compare the conversation with the existing events supplied below.

If the speakers are talking about, explaining, inviting someone to, joining, declining, clarifying, or changing an EXISTING event, UPDATE that event using its existing eventId.
Do not create a second event just because a new person learned about or joined an existing one.

Event identity is based on the PURPOSE of the future occurrence: what is actually supposed to happen.
Different wording does NOT create a different event. ""the winter gathering"", ""the village event"", and ""the potluck"" may refer to the same event if the conversation shows they are the same future occurrence.

Strong evidence that the conversation refers to an existing event includes explicit backward references such as:
- ""the event we talked about before""
- ""that village gathering""
- ""our plan from the other day""
- ""the sports day""
When such wording appears and there is a compatible existing event, strongly prefer UPDATE over ADD.

A separate planning meeting about another event is a DIFFERENT event only if the NPCs actually arrange a separate future meeting/activity whose purpose is planning that other event.
Example:
- Existing event: ""Daniel hosts a winter village gathering.""
- Later conversation: ""About that village event we discussed, January 20 at Town Hall would work."" -> UPDATE the existing gathering.
- Later conversation: ""Let's meet Friday at the cafe to plan the winter gathering."" -> ADD a separate planning meeting whose purpose is planning the gathering.

Do not create a separate event merely because the current conversation is discussing or refining an existing event.
Use purpose and conversational reference, not exact wording, to decide whether two mentions refer to the same occurrence.

STEP 3 — ASSIGN PEOPLE PRECISELY.

- organizers:
  NPCs clearly hosting, creating, coordinating, or taking responsibility for the event.
  Do not make every participant an organizer.

- attendees:
  NPCs who are clearly confirmed/expected to PARTICIPATE in the event.
  An invitation alone is NOT enough.
  Add a non-organizer only when they clearly accept or confirm participation, for example:
  'I'll be there', 'I'd love to come', 'That works for me', 'See you then', or another clear acceptance in context.
  Merely hearing about the event, saying 'sounds fun', 'great idea', or showing enthusiasm is NOT attendance.
  If someone explicitly refuses or says they will not attend, keep them out of attendees.
  Every organizer must also appear in attendees.

- knownBy:
  NPCs who clearly know that the event exists.
  If an event is actually explained or mentioned to a previously uninformed conversation partner, add that person to knownBy even if they are not invited, do not accept, or explicitly refuse.
  Do not add NPCs who have not actually been told about the event.
  Every attendee must also appear in knownBy.

The intended invariant is:
organizers ⊆ attendees ⊆ knownBy

STEP 4 — ASSIGN LOGISTICS VERY CONSERVATIVELY.
Do NOT guess or complete missing logistics. A field may remain null.

For a NEW event, a date, hour, or place is confirmed only if a concrete value was PROPOSED and then ACCEPTED/CONFIRMED by the other party in context.
The confirmation does not need to repeat the value word-for-word; phrases such as 'that works for me', 'sounds good', or 'see you there' can confirm the immediately preceding proposal.

For an EXISTING event:
- Preserve already-confirmed date/hour/place values unless the conversation clearly changes them.
- A NEW replacement value should only overwrite an existing value if the replacement was proposed and mutually confirmed.
- Merely suggesting an alternative does not change the stored value.

- date:
  Set only when the conversation mutually confirms ONE EXACT DAY.
  Exact-day references may be explicit dates or resolvable relative references such as 'tomorrow', 'next Tuesday', 'this Saturday', or 'Monday' when context makes the intended day unambiguous.
  Resolve an accepted relative day to yyyy-MM-dd using the supplied conversation timestamp.
  Broad ranges such as 'next week', 'sometime this weekend', or 'one day soon' are NOT exact dates and must remain null unless an exact day is later confirmed.
  A day proposed by one speaker but not accepted by the other is NOT confirmed and must remain null.

- hour:
  Set only when the conversation mutually confirms ONE CONCRETE CLOCK HOUR.
  Valid examples include 14:00, 2 PM, 9 in the morning, noon (=12), or midnight (=0).
  Broad dayparts such as 'morning', 'afternoon', 'evening', 'after work', or 'later' are NOT concrete hours and must remain null.
  Never convert 'afternoon' into 14, 'morning' into 9, etc.
  A concrete hour proposed by one speaker but not accepted by the other is NOT confirmed.

- placeId:
  Set only when a place you can clearly match to a concrete place from the provided valid-place list was proposed AND accepted/confirmed in context.
  The confirmation may be indirect, such as 'the cafe works for me' or 'sounds good' immediately after the cafe was proposed.
  You may need to find the valid match for a place ID, for example ""HouseOfAmy"" can be equal to Amy saying ""Meet me at my home"".
  A place merely mentioned during ordinary conversation is not an event location.
  Never invent a place and never use a place outside the supplied valid IDs.

Example:
Speaker A: 'Would you be able to meet at the cafe next week?'
Speaker B: 'The cafe next week works for me. How about Tuesday afternoon?'
Conversation ends.
Result:
- placeId = Cafe, because the cafe was proposed and confirmed.
- date = null, because Tuesday was only proposed by Speaker B and never confirmed.
- hour = null, because 'afternoon' is not a concrete clock hour and was not confirmed anyway.

STEP 5 — DESCRIPTION AND STATUS.

- description:
  One short sentence describing the concrete PURPOSE of the FUTURE occurrence: what people will actually do.
  Write the event itself, not a person's wish, thought, uncertainty, or the fact that they are considering it.
  Good:
  - ""Daniel hosts a winter village gathering.""
  - ""Amy and Tim go hiking together.""
  - ""Daniel and Tim meet to plan the village history celebration.""
  Avoid:
  - ""Daniel is thinking about organizing a gathering.""
  - ""Amy would be happy to meet Tim sometime.""
  - ""Daniel is considering ideas for the winter event.""
  PENDING means logistics are unresolved; the description should still state the event's purpose as clearly as the conversation allows.
  For an existing event, preserve the same core purpose unless the conversation clearly changes what the event actually is.

- status:
  The program determines this automatically, you don't need to modify it.
  PLANNED means date, concrete hour, place, and at least one real participant/organizer are known.
  PENDING means a real event exists but one or more logistics are unresolved.

UPDATE RULE — CRITICAL:
For every UPDATE, return the FULL CURRENT STATE of the event after this conversation.
Preserve all unchanged organizers, attendees, knownBy names, date, hour, place, and description from the existing event.
Do not erase a confirmed value merely because it was not repeated in this conversation.

Return exactly this JSON shape:
{
  ""add"": [
    {
      ""date"": ""yyyy-MM-dd or null"",
      ""hour"": 17 or null,
      ""organizers"": [""Maria""],
      ""attendees"": [""Maria"", ""Amy""],
      ""knownBy"": [""Maria"", ""Amy""],
      ""placeId"": ""Cafe"",
      ""description"": ""one short sentence""
    }
  ],
  ""update"": [
    {
      ""eventId"": ""EVT-000001"",
      ""date"": ""yyyy-MM-dd or null"",
      ""hour"": 13 or null,
      ""organizers"": [""Maria""],
      ""attendees"": [""Maria"", ""Amy""],
      ""knownBy"": [""Maria"", ""Amy"", ""Tim""],
      ""placeId"": ""Cafe"",
      ""description"": ""Maria is planning a village gathering at her cafe.""
    }
  ]
}

Always include add and update arrays, even when empty.";

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
                fallbackJson: @"{""add"":[],""update"":[]}",
                maxTokens: 1350
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
                        continue;

                    if (!TryNormalizeProposal(proposal, now, validNpcNames, out var normalized, out string reason))
                    {
                        Debug.LogWarning($"[EVENTS] Ignoring invalid update for {proposal.eventId}: {reason}");
                        continue;
                    }

                    existing.date = normalized.date;
                    existing.hour = normalized.hour;
                    existing.organizers = normalized.organizers;
                    existing.attendees = normalized.attendees;
                    existing.knownBy = normalized.knownBy;
                    existing.placeId = normalized.placeId;
                    existing.description = normalized.description;
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
