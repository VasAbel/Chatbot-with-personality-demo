using UnityEngine;
using Assets.Game_Manager;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using Newtonsoft.Json;
using System.Threading.Tasks;

public class NPC : MonoBehaviour
{
    public int idx;

    private string npcName;
    public bool isInConversation = false;
    public bool isTalkingToUser = false;
    internal ConfigManager.Description desc;
    public List<string> dailySchedule = new List<string>();
    public NpcMemory memory = new NpcMemory();

    public void Awake()
    {
        desc = ConfigManager.Instance.GetFullCharacterDescription(idx);
        npcName = ConfigManager.Instance.GetCharacterName(idx);
        memory.corePersonality = desc.core ?? "";
        memory.currentThoughts.Clear();
        foreach (var t in ParseInitialThoughts(desc.thoughts))
            memory.currentThoughts.Add(t);
    }

    private IEnumerable<Thought> ParseInitialThoughts(string thoughtsBlock)
    {
        if (string.IsNullOrWhiteSpace(thoughtsBlock))
            yield break;

        var lines = thoughtsBlock
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var line in lines)
        {
            // allow either "- something" or "something"
            string text = line.StartsWith("-") ? line.TrimStart('-').Trim() : line;

            if (string.IsNullOrWhiteSpace(text))
                continue;

            yield return new Thought
            {
                text = text,
                // reasonable defaults for seeded “current situation”
                confidence = 0.8f,
                salience = 0.6f,
                createdUnix = now,
                gameTimestamp = GetMemoryTimestampTag()
            };
        }
    }
    public string getName() => npcName;
    public string getDesc() => desc.core;

    public void setName(string value)
    {
        npcName = value;
    }

    public void setDesc(string value)
    {
        // Future implementation if needed
    }

    [Serializable]
    private class ScheduleResponse
    {
        public List<string> schedule;
    }

    async void Start()
    {
        bool ok = await TryGenerateScheduleFromLLM();

        if (!ok || dailySchedule == null || dailySchedule.Count != 24)
        {
            Debug.LogWarning($"[{npcName}] Falling back to default hard-coded schedule.");
            ApplyDefaultSchedule();
        }

        NPCGlobalTimer timer = FindObjectOfType<NPCGlobalTimer>();
        if (timer != null)
        {
            timer.RegisterNPC(this);
        }

        LogMemoryToFile();
    }

    private async Task<bool> TryGenerateScheduleFromLLM()
    {
        var client = FindObjectOfType<GptClient>();
        var registry = PlaceRegistry.Instance;

        if (client == null || registry == null)
        {
            Debug.LogWarning($"[{npcName}] No GptClient or PlaceRegistry found, cannot generate schedule via LLM.");
            return false;
        }

        var placeNames = registry.GetAllPlaceNames();
        if (placeNames == null || placeNames.Count == 0)
        {
            Debug.LogWarning($"[{npcName}] PlaceRegistry has no places, cannot generate schedule via LLM.");
            return false;
        }

        string placesList = string.Join(", ", placeNames);

        var timer = FindObjectOfType<NPCGlobalTimer>();
        string todayBlock = timer != null
            ? $@"Current in-game date: {timer.GetCurrentDateString()}
        Current in-game day of week: {timer.GetCurrentDayOfWeekString()}
        Today full timestamp: {timer.GetFullTimestamp()}"
            : @"Current in-game date: unknown
        Current in-game day of week: unknown
        Today full timestamp: unknown";

        // ---- SYSTEM MESSAGE ----
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
- If memory clearly refers to something planned for the current day, prioritize that over default routines.

SELF-CHECK BEFORE ANSWERING:
1. Make sure ""hours"" has exactly 24 items.
2. If there are too many, remove extras from the end.
3. If there are too few, repeat the last valid location until there are 24.
4. Then output the final JSON only.
";

        // ---- USER MESSAGE (context about this NPC) ----
        string socialBlock = "";
        if (memory.socialByNpc != null && memory.socialByNpc.Count > 0)
        {
            socialBlock = string.Join("\n\n", memory.socialByNpc.Select(kv => $"{kv.Key}: {kv.Value}"));
        }

        string thoughtsBlock = "";
        if (memory.currentThoughts != null && memory.currentThoughts.Count > 0)
        {
            thoughtsBlock = string.Join("\n", memory.currentThoughts.Select(t => "- " + t.text));
        }

        string homeId = $"HouseOf{npcName}";

        string user = $@"
Generate the daily schedule for this NPC for the following in-game date.

NPC name: {npcName}
{todayBlock}

This NPC's home location ID: {homeId}

Core personality:
{memory.corePersonality}

Social memory:
{(string.IsNullOrWhiteSpace(socialBlock) ? "(none)" : socialBlock)}

Current thoughts and plans:
{(string.IsNullOrWhiteSpace(thoughtsBlock) ? "(none)" : thoughtsBlock)}

Available location IDs:
{placesList}

Important interpretation note:
Some memory items may refer to relative dates such as ""tomorrow"", ""next Friday"", or ""this weekend"".
Use the current in-game date and the memory timestamps to infer whether such plans are relevant for today.

Generate the schedule for the 24 hours of this date, from 00:00 to 24:00.
Return only the JSON object.";

        string rawJson;
        try
        {
            //using your existing JSON-enforcing helper
            rawJson = await client.RequestGenericJsonAsync(system, user, maxTokens: 400);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{npcName}] LLM schedule request failed: {ex.Message}");
            return false;
        }
        Debug.Log($"[{npcName}] raw schedule JSON:\n{rawJson}");
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            Debug.LogWarning($"[{npcName}] LLM schedule was empty.");
            return false;
        }

        NpcScheduleResponse resp = null;
        try
        {
            resp = JsonConvert.DeserializeObject<NpcScheduleResponse>(rawJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{npcName}] Failed to parse schedule JSON: {ex.Message}\nRaw: {rawJson}");
            return false;
        }

        if (resp == null || resp.hours == null || resp.hours.Count == 0)
        {
            Debug.LogWarning($"[{npcName}] Schedule JSON missing or empty.");
            return false;
        }

        var validSet = new HashSet<string>(placeNames);
        string safeDefault = validSet.Contains("House") ? "House" : placeNames[0];

        //Work on a local list so we can trim/pad
        var hours = resp.hours.ToList();

        //If too many → trim from the end
        if (hours.Count > 24)
        {
            Debug.LogWarning($"[{npcName}] Schedule has {hours.Count} entries, trimming to 24.");
            hours = hours.Take(24).ToList();
        }

        //If too few → repeat last entry until 24
        while (hours.Count < 24)
        {
            var last = hours.Count > 0 ? hours[hours.Count - 1] : safeDefault;
            hours.Add(last);
        }

        // Validate all places
        for (int i = 0; i < hours.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(hours[i]) || !validSet.Contains(hours[i]))
            {
                hours[i] = safeDefault;
            }
        }

        dailySchedule = hours;
        Debug.Log($"[{npcName}] LLM schedule normalized to exactly 24 valid entries.");
        return true;

    }


    private void ApplyDefaultSchedule()
    {
        switch (npcName)
        {
            case "Amy":
                dailySchedule = new List<string>
            {
                "HouseOfAmy", "HouseOfAmy", "HouseOfAmy", "HouseOfAmy",
                "HouseOfAmy", "HouseOfAmy", "HouseOfAmy", "HouseOfAmy",
                "School", "School", "School", "School",
                "HouseOfAmy", "HouseOfAmy", "HouseOfAmy", "HouseOfAmy",
                "HouseOfAmy", "Well", "HouseOfAmy", "HouseOfAmy",
                "HouseOfAmy", "HouseOfAmy", "HouseOfAmy", "HouseOfAmy"
             };
                break;

            case "Tim":
                dailySchedule = new List<string>
            {
                "HouseOfTim", "HouseOfTim", "HouseOfTim", "HouseOfTim",
                "HouseOfTim", "HouseOfTim", "HouseOfTim", "WoodworkingShop",
                "WoodworkingShop", "WoodworkingShop", "WoodworkingShop", "WoodworkingShop",
                "Well", "Well", "WoodworkingShop", "WoodworkingShop",
                "HouseOfTim", "HouseOfTim", "HouseOfTim", "HouseOfTim",
                "HouseOfTim", "HouseOfTim", "HouseOfTim", "HouseOfTim"
            };
                break;
                
            case "Gabriel":
                dailySchedule = new List<string>
            {
                "HouseOfGabriel", "HouseOfGabriel", "HouseOfGabriel", "HouseOfGabriel",
                "HouseOfGabriel", "HouseOfGabriel", "HouseOfGabriel", "School",
                "School", "School", "School", "School",
                "Well", "Well", "HouseOfGabriel", "HouseOfGabriel",
                "HouseOfGabriel", "HouseOfGabriel", "HouseOfGabriel", "HouseOfGabriel",
                "HouseOfGabriel", "HouseOfGabriel", "HouseOfGabriel", "HouseOfGabriel"
            };
                break;
            default:
                Debug.LogWarning($"No schedule defined for NPC: {npcName}, using 'House' all day.");
                dailySchedule = Enumerable.Repeat("House", 24).ToList();
                break;
        }
    }

    public string GetCurrentPlace(int hour)
    {
        if (hour >= 0 && hour < dailySchedule.Count)
            return dailySchedule[hour];
        return "Well"; // Fallback
    }

    public async void OnHourPassed(int hour)
    {
        if (isInConversation)
        {
            Debug.Log($"{getName()} is talking to the player. Skipping move this hour.");
            return;
        }

        // Only regenerate schedule at midnight (hour 0) to reduce LLM calls
        if(hour == 0 && dailySchedule.Count > 0)
        {
            Debug.Log($"[{npcName}] Using cached schedule from yesterday.");
        }
        else if (dailySchedule.Count == 0) // First time initialization
        {
            bool ok = await TryGenerateScheduleFromLLM();
            if (!ok || dailySchedule == null || dailySchedule.Count != 24)
            {
                Debug.LogWarning($"[{npcName}] Falling back to default hard-coded schedule.");
                ApplyDefaultSchedule();
            }
        }

        string placeToGo = dailySchedule[hour % dailySchedule.Count];
        Debug.Log($"[Chronology] {getName()} | now: {GetCurrentGameTimestamp()} | heading to: {placeToGo}");

        gameObject.GetComponent<NpcMovement>().MoveTo(placeToGo);
    }

    public void UpdateMemory(string npcName, string newSummary)
    {
        memory.socialByNpc[npcName] = newSummary;
    }

    public string GetFormattedMemory()
    {
        List<string> lines = new List<string>();

        if (memory.socialByNpc != null)
        {
            foreach (var kv in memory.socialByNpc)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    lines.Add($"MEMORY OF {kv.Key}:\n- {kv.Value.Trim()}");
            }
        }

        if (memory.currentThoughts != null && memory.currentThoughts.Count > 0)
        {
            lines.Add("CURRENT THOUGHTS:");
            int shown = 0;
            foreach (var t in memory.currentThoughts)
            {
                lines.Add($"- [{(string.IsNullOrWhiteSpace(t.gameTimestamp) ? "unknown time" : t.gameTimestamp)}] {t.text} (salience {(int)(t.salience * 100)}%)");
                if (++shown >= 5) break; // only the top few for token budget
            }
        }

        return lines.Count == 0 ? "Nothing learnt yet." : string.Join("\n\n", lines);
    }

    public void LogMemoryToFile()
    {
        string logPath = Path.Combine(Application.persistentDataPath, $"{getName()}_memory.txt");

        using (StreamWriter writer = new StreamWriter(logPath, false))
        {
            writer.WriteLine($"=== Memory log for {getName()} ===");
            writer.WriteLine($"Timestamp: {System.DateTime.Now}");
            writer.WriteLine();

            // CORE PERSONALITY
            writer.WriteLine("=== CORE PERSONALITY ===");
            writer.WriteLine(memory.corePersonality?.Trim() ?? "(none)");
            writer.WriteLine();

            // SOCIAL MEMORY (what this NPC remembers about others)
            writer.WriteLine("=== SOCIAL MEMORY ===");
            if (memory.socialByNpc != null && memory.socialByNpc.Count > 0)
            {
                foreach (var kv in memory.socialByNpc)
                {
                    writer.WriteLine($"[{kv.Key}]");
                    writer.WriteLine(kv.Value?.Trim() ?? "(no data)");
                    writer.WriteLine();
                }
            }
            else
            {
                writer.WriteLine("(no known NPCs yet)");
                writer.WriteLine();
            }

            // CURRENT THOUGHTS
            writer.WriteLine("=== CURRENT THOUGHTS ===");
            if (memory.currentThoughts != null && memory.currentThoughts.Count > 0)
            {
                foreach (var t in memory.currentThoughts.OrderByDescending(t => t.salience))
                {
                    DateTime created = DateTimeOffset.FromUnixTimeSeconds(t.createdUnix).DateTime;
                    writer.WriteLine($"- [{(string.IsNullOrWhiteSpace(t.gameTimestamp) ? "unknown time" : t.gameTimestamp)}] {t.text}");
                    writer.WriteLine($"  → confidence: {t.confidence:F2}, salience: {t.salience:F2}, created(real): {created}");
                    writer.WriteLine();
                }
            }
            else
            {
                writer.WriteLine("(no active thoughts)");
                writer.WriteLine();
            }
        }

        Debug.Log($"🧠 Memory log written for {getName()} at {logPath}");
    }

    public void DecayThoughts(float dt)
    {
        if (memory.currentThoughts == null || memory.currentThoughts.Count == 0)
            return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Tune these constants as you like:
        float baseSalienceDecayPerUnit   = 0.08f; // how fast thoughts become less "top of mind"
        float baseConfidenceDecayPerUnit = 0.02f; // how fast certainty fades

        foreach (var t in memory.currentThoughts)
        {
            // Age in days, just to slightly accelerate decay for very old stuff
            float ageDays = Mathf.Max(0f, (now - t.createdUnix) / 86400f);
            float ageFactor = 1f + ageDays * 0.5f; // after a few days, decay a bit stronger

            t.salience = Mathf.Clamp01(
                t.salience - baseSalienceDecayPerUnit * dt * ageFactor
            );

            // confidence decays more gently
            t.confidence = Mathf.Clamp01(
                t.confidence - baseConfidenceDecayPerUnit * dt
            );
        }

        // Drop thoughts that are essentially forgotten
        memory.currentThoughts.RemoveAll(t => t.salience < 0.05f);
    }

    public string GetCurrentAreaName()
    {
        if (PlaceRegistry.Instance == null)
            return PlaceRegistry.DefaultAreaName;

        return PlaceRegistry.Instance.DescribePosition(transform.position);
    }

    public string GetHeadingPlaceId()
    {
        var mover = GetComponent<NpcMovement>();
        if (mover != null && !string.IsNullOrWhiteSpace(mover.CurrentTargetPlaceId))
            return mover.CurrentTargetPlaceId;

        NPCGlobalTimer timer = FindObjectOfType<NPCGlobalTimer>();
        int hour = timer != null ? timer.GetCurrentHour() : 8;

        return GetCurrentPlace(hour);
    }

    public string GetHeadingDisplayName()
    {
        string placeId = GetHeadingPlaceId();

        if (PlaceRegistry.Instance == null || string.IsNullOrWhiteSpace(placeId))
            return "unknown place";

        return PlaceRegistry.Instance.GetPlaceDisplayName(placeId);
    }

    private NPCGlobalTimer GetGameTimer()
    {
        return FindObjectOfType<NPCGlobalTimer>();
    }

    public string GetCurrentGameTimestamp()
    {
        var timer = GetGameTimer();
        return timer != null ? timer.GetFullTimestamp() : "Unknown time";
    }

    public string GetCurrentGameDateOnly()
    {
        var timer = GetGameTimer();
        return timer != null ? timer.GetCurrentDateString() : "Unknown date";
    }

    public string GetCurrentGameDayOfWeek()
    {
        var timer = GetGameTimer();
        return timer != null ? timer.GetCurrentDayOfWeekString() : "Unknown day";
    }

    public string GetCurrentGameHourString()
    {
        var timer = GetGameTimer();
        return timer != null ? timer.GetCurrentHourString() : "??:??";
    }

    public string GetMemoryTimestampTag()
    {
        var timer = GetGameTimer();
        return timer != null ? timer.FormatMemoryTimestamp() : "Unknown time";
    }
}

[System.Serializable]
public class NpcScheduleResponse
{
    public List<string> hours;   // index 0..23
}
