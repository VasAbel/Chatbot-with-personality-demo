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
        memory.corePersonality = desc.description;
    }

    public string getName() => npcName;
    public string getDesc() => desc.description;

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

        // ---- SYSTEM MESSAGE ----
        string system = @"
You are a daily routine planner for NPCs in a small village simulation game.
Your job is to plan a believable 24-hour schedule for exactly one NPC.

The world is discrete: 24 game hours from 0 to 23.
For each hour, you must choose **one** location where the NPC spends that hour.

You MUST follow these rules:

- You may only use the provided location IDs exactly as given.
- You MUST output strict JSON with a single property:
  {
      ""hours"": [ ""PLACE_0"", ""PLACE_1"", ..., ""PLACE_23"" ]
  }

CRITICAL CONSTRAINTS ABOUT THE ARRAY:

- ""hours"" MUST be an array of **exactly 24 strings**.
- No more than 24. No fewer than 24.
- hours[0] is hour 0–1, hours[1] is hour 1–2, ..., hours[23] is 23–24.
- Do NOT include any other keys besides ""hours"".
- Do NOT include comments, explanations, or trailing commas.

SELF-CHECK BEFORE ANSWERING (VERY IMPORTANT):

1. Count how many elements are in the ""hours"" array in your draft.
2. If the count is **greater than 24**, remove elements from the **end** until exactly 24 remain.
3. If the count is **less than 24**, repeat the **last valid place** until there are exactly 24 items.
4. Only then output the final JSON.

OTHER RULES:
- Repeating the same location across many hours means the NPC stays there.
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

        string user = $@"
NPC name: {npcName}

Core personality (long-term traits, job, habits):
{memory.corePersonality}

Social memory (what {npcName} knows about others):
{(string.IsNullOrWhiteSpace(socialBlock) ? "(none)" : socialBlock)}

Current plans & thoughts (may influence where they go today):
{(string.IsNullOrWhiteSpace(thoughtsBlock) ? "(none)" : thoughtsBlock)}

Available locations (use ONLY these exact IDs):
{placesList}

Design a realistic daily routine for {npcName} that fits their personality and current plans.
For example:
- Work-related hours in places that match their job.
- Social / collaboration plans that might bring them to others' usual locations.
- Rest / sleep mostly at ""HouseOf'name of npc'"".
The order of the elements should represent the order of hours starting from 00:00 AM for the first element, 01:00 AM for the second etc.

Return ONLY the JSON object with the 24-element ""hours"" array.";

        string rawJson;
        try
        {
            // using your existing JSON-enforcing helper
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

        // Work on a local list so we can trim/pad
        var hours = resp.hours.ToList();

        // 👇 If too many → trim from the end
        if (hours.Count > 24)
        {
            Debug.LogWarning($"[{npcName}] Schedule has {hours.Count} entries, trimming to 24.");
            hours = hours.Take(24).ToList();
        }

        // 👇 If too few → repeat last entry until 24
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
                    "Well", "Well", "Market", "Townhall", "Townhall", "Well", "Market", "Market",
                    "House", "House", "Market", "Market", "Well", "House", "House", "Market",
                    "Market", "House", "House", "House", "House", "House", "House", "House"
                };
                break;
            case "Tim":
                dailySchedule = new List<string>
                {
                    "Townhall", "House", "Market", "Market", "Market", "Market", "Market", "House",
                    "Market", "Market", "Market", "Market", "Well", "House", "House", "Market",
                    "Townhall", "Townhall", "House", "House", "House", "House", "House", "House"
                };
                break;
            case "Gabriel":
                dailySchedule = new List<string>
                {
                    "Well", "Well", "Well", "Well", "Townhall", "Townhall", "Well", "Market",
                    "Townhall", "Townhall", "Townhall", "House", "House", "House", "Market", "Well",
                    "House", "House", "House", "House", "House", "House", "House", "House"
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
        if (isInConversation && isTalkingToUser)
        {
            Debug.Log($"{getName()} is talking to the player. Skipping move this hour.");
            return;
        }

        string placeToGo = dailySchedule[hour % dailySchedule.Count];
        Debug.Log($"{getName()} now heading to: {placeToGo}");

        gameObject.GetComponent<NpcMovement>().MoveTo(placeToGo);

        if(hour == 0)
        {
            bool ok = await TryGenerateScheduleFromLLM();

            if (!ok || dailySchedule == null || dailySchedule.Count != 24)
            {
                Debug.LogWarning($"[{npcName}] Falling back to default hard-coded schedule.");
                ApplyDefaultSchedule();
            }
        }
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
                lines.Add($"- {t.text} (salience {(int)(t.salience * 100)}%)");
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
                    writer.WriteLine($"- {t.text}");
                    writer.WriteLine($"  → confidence: {t.confidence:F2}, salience: {t.salience:F2}, created: {created}");
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
}

[System.Serializable]
public class NpcScheduleResponse
{
    public List<string> hours;   // index 0..23
}
