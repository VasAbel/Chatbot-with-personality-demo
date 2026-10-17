using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Owns one NPC's daily schedule lifecycle: generation, validation,
/// midnight regeneration, timer registration, and moving the NPC to the
/// location assigned to the current hour.
/// </summary>
[RequireComponent(typeof(NPC))]
[RequireComponent(typeof(NpcMovement))]
public class NpcScheduleController : MonoBehaviour
{
    private NPC npc;
    private NpcMovement movement;

    private GptClient client;
    private NPCGlobalTimer timer;
    private ConversationManager conversationManager;

    [SerializeField] private List<string> dailySchedule = new List<string>();

    public IReadOnlyList<string> DailySchedule => dailySchedule;

    [Serializable]
    private sealed class ScheduleResponse
    {
        public List<string> hours;
    }

    private void Awake()
    {
        npc = GetComponent<NPC>();
        movement = GetComponent<NpcMovement>();
    }

    private async void Start()
    {
        client = FindObjectOfType<GptClient>();
        timer = FindObjectOfType<NPCGlobalTimer>();
        conversationManager = FindObjectOfType<ConversationManager>();

        if (client == null || timer == null || PlaceRegistry.Instance == null)
        {
            FailSchedule(
                $"[{npc.getName()}] Schedule controller is missing GptClient, NPCGlobalTimer, or PlaceRegistry."
            );
            return;
        }

        bool generated = await GenerateScheduleAsync();
        if (!generated)
        {
            FailSchedule($"[{npc.getName()}] Initial daily schedule generation failed.");
            return;
        }

        timer.RegisterSchedule(this);
    }

    public async void OnHourPassed(int hour)
    {
        if (!enabled)
            return;

        if (npc.isConversationBlocked && npc.isTalkingToUser)
        {
            Debug.Log($"{npc.getName()} is talking to the player. Skipping move this hour.");
            return;
        }

        if (hour == 0)
        {
            if (conversationManager == null)
                conversationManager = FindObjectOfType<ConversationManager>();

            if (conversationManager == null)
            {
                FailSchedule(
                    $"[{npc.getName()}] ConversationManager is unavailable, so memory cannot be cleaned before schedule regeneration."
                );
                return;
            }

            await conversationManager.MemoryManager.CleanNpcMemoryAsync(npc);

            bool generated = await GenerateScheduleAsync();
            if (!generated)
            {
                FailSchedule($"[{npc.getName()}] Daily schedule regeneration failed.");
                return;
            }
        }

        string placeToGo = GetPlaceForHour(hour);
        if (string.IsNullOrWhiteSpace(placeToGo))
        {
            FailSchedule(
                $"[{npc.getName()}] No valid schedule entry exists for hour {hour}."
            );
            return;
        }

        Debug.Log(
            $"[Chronology] {npc.getName()} | now: {timer.GetFullTimestamp()} | heading to: {placeToGo}"
        );

        movement.MoveTo(placeToGo);
    }

    public string GetPlaceForHour(int hour)
    {
        return hour >= 0 && hour < dailySchedule.Count
            ? dailySchedule[hour]
            : null;
    }

    public string GetHeadingPlaceId()
    {
        if (!string.IsNullOrWhiteSpace(movement.CurrentTargetPlaceId))
            return movement.CurrentTargetPlaceId;

        if (timer == null)
            timer = FindObjectOfType<NPCGlobalTimer>();

        return timer != null
            ? GetPlaceForHour(timer.GetCurrentHour())
            : null;
    }

    public string GetHeadingDisplayName()
    {
        string placeId = GetHeadingPlaceId();

        if (PlaceRegistry.Instance == null || string.IsNullOrWhiteSpace(placeId))
            return "unknown place";

        return PlaceRegistry.Instance.GetPlaceDisplayName(placeId);
    }

    private async Task<bool> GenerateScheduleAsync()
    {
        if (client == null || timer == null || PlaceRegistry.Instance == null)
            return false;

        List<string> placeNames = PlaceRegistry.Instance.GetAllPlaceNames();
        if (placeNames == null || placeNames.Count == 0)
        {
            Debug.LogError(
                $"[{npc.getName()}] PlaceRegistry has no places; schedule generation cannot continue."
            );
            return false;
        }

        string placesList = string.Join(", ", placeNames);

        string todayBlock =
$@"Current in-game date: {timer.GetCurrentDateString()}
        Current in-game day of week: {timer.GetCurrentDayOfWeekString()}
        Today full timestamp: {timer.GetFullTimestamp()}";

        // Prompt text intentionally preserved during structural refactoring.
        string system = @"
You are a daily routine planner for one NPC in a small village simulation game.

Your task is to generate a believable schedule for exactly one full day.

OUTPUT FORMAT:
- Output strict JSON only.
- The JSON must have exactly one property:
  {
    ""hours"": [ ""PLACE_0"", ""PLACE_1"", ..., ""PLACE_23"" ]
  }

ARRAY RULES:
- ""hours"" must contain exactly 24 strings.
- Use only the provided location IDs exactly as given.
- hours[0] = 00:00-01:00, hours[1] = 01:00-02:00, ..., hours[23] = 23:00-24:00.
- Do not output any other keys.
- Do not output explanations, comments, or trailing commas.

REALISM RULES:
- Assume a normal human day-night rhythm unless the memory clearly suggests otherwise.
- Sleep/rest usually happens at night, mostly at the NPC's own home.
- Regular work/school usually happens during daytime, commonly starting in the morning.
- Workplaces are usually visited on workdays more than on weekends, unless the memory clearly suggests otherwise.
- Social visits and errands are more common in the afternoon or evening than deep at night.
- Concrete social-event logistics come from the authoritative event list supplied below, not from memory.
- If an event is listed for this NPC today, the NPC must be at that event's place during the listed hour.

SELF-CHECK BEFORE ANSWERING:
1. Make sure ""hours"" has exactly 24 items.
2. If there are too many, remove extras from the end.
3. If there are too few, repeat the last valid location until there are 24.
4. Then output the final JSON only.
";

        string socialBlock = "";
        if (npc.memory.socialByNpc != null && npc.memory.socialByNpc.Count > 0)
        {
            socialBlock = string.Join(
                "\n\n",
                npc.memory.socialByNpc.Select(kv => $"{kv.Key}: {kv.Value}")
            );
        }

        string thoughtsBlock = "";
        if (npc.memory.currentThoughts != null && npc.memory.currentThoughts.Count > 0)
        {
            thoughtsBlock = string.Join(
                "\n",
                npc.memory.currentThoughts.Select(t => "- " + t.text)
            );
        }

        string homeId = $"HouseOf{npc.getName()}";

        string eventScheduleBlock = SocialEventManager.Instance != null
            ? SocialEventManager.Instance.BuildScheduleContext(
                npc.getName(),
                timer.GetCurrentDateTime()
            )
            : "(none)";

        string user = $@"
Generate the daily schedule for this NPC for the following in-game date.

NPC name: {npc.getName()}
{todayBlock}

This NPC's home location ID: {homeId}

Core personality:
{npc.memory.corePersonality}

Social memory:
{(string.IsNullOrWhiteSpace(socialBlock) ? "(none)" : socialBlock)}

Current thoughts and plans:
{(string.IsNullOrWhiteSpace(thoughtsBlock) ? "(none)" : thoughtsBlock)}

Authoritative planned social events for this NPC today:
{eventScheduleBlock}

Available location IDs:
{placesList}

Social-event scheduling rule:
- The authoritative event list above is the single source of truth for concrete social appointments.
- For each listed event, set the schedule entry for that exact hour to the listed place ID.
- If no event is listed, plan the NPC's normal believable daily routine from personality and general context.

Generate the schedule for the 24 hours of this date, from 00:00 to 24:00.
Return only the JSON object.";

        string rawJson;
        try
        {
            rawJson = await client.RequestGenericJsonAsync(
                system,
                user,
                maxTokens: 400
            );
        }
        catch (Exception ex)
        {
            Debug.LogError(
                $"[{npc.getName()}] LLM schedule request failed: {ex.Message}"
            );
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            Debug.LogError($"[{npc.getName()}] LLM schedule response was empty.");
            return false;
        }

        ScheduleResponse response;
        try
        {
            response = JsonConvert.DeserializeObject<ScheduleResponse>(rawJson);
        }
        catch (Exception ex)
        {
            Debug.LogError(
                $"[{npc.getName()}] Failed to parse schedule JSON: {ex.Message}\nRaw: {rawJson}"
            );
            return false;
        }

        if (response?.hours == null || response.hours.Count == 0)
        {
            Debug.LogError(
                $"[{npc.getName()}] Schedule response contained no usable entries."
            );
            return false;
        }

        var validPlaces = new HashSet<string>(
            placeNames,
            StringComparer.Ordinal
        );

        if (response.hours.Any(place =>
            string.IsNullOrWhiteSpace(place) || !validPlaces.Contains(place)))
        {
            Debug.LogError(
                $"[{npc.getName()}] Schedule contains an empty or unknown place ID."
            );
            return false;
        }

        List<string> cleanedSchedule = response.hours.ToList();

        if (cleanedSchedule.Count > 24)
        {
            Debug.LogWarning(
                $"[{npc.getName()}] Schedule contained {cleanedSchedule.Count} entries; truncating to the first 24."
            );
            cleanedSchedule = cleanedSchedule.Take(24).ToList();
        }
        else if (cleanedSchedule.Count < 24)
        {
            string lastPlace = cleanedSchedule[cleanedSchedule.Count - 1];

            Debug.LogWarning(
                $"[{npc.getName()}] Schedule contained only {cleanedSchedule.Count} entries; repeating the final valid place until 24 entries are reached."
            );

            while (cleanedSchedule.Count < 24)
                cleanedSchedule.Add(lastPlace);
        }

        if (cleanedSchedule.Count != 24)
        {
            Debug.LogError(
                $"[{npc.getName()}] Schedule could not be normalized to exactly 24 entries."
            );
            return false;
        }

        dailySchedule = cleanedSchedule;

        SocialEventManager.Instance?.ApplyEventsToSchedule(
            npc.getName(),
            timer.GetCurrentDateTime(),
            dailySchedule
        );

        MeasurementLogger.Instance?.LogDailySchedule(npc, dailySchedule);
        return true;
    }

    private void FailSchedule(string message)
    {
        Debug.LogError(message);

        if (timer != null)
            timer.UnregisterSchedule(this);

        enabled = false;
    }
}
