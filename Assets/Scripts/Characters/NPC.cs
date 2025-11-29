using UnityEngine;
using Assets.Game_Manager;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

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

    void Start()
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
                Debug.LogWarning($"No schedule defined for NPC: {npcName}");
                break;
        }

        NPCGlobalTimer timer = FindObjectOfType<NPCGlobalTimer>();
        if (timer != null)
        {
            timer.RegisterNPC(this);
        }

        LogMemoryToFile();
    }

    public string GetCurrentPlace(int hour)
    {
        if (hour >= 0 && hour < dailySchedule.Count)
            return dailySchedule[hour];
        return "House"; // Fallback
    }

    public void OnHourPassed(int hour)
    {
        if (isInConversation && isTalkingToUser)
        {
            Debug.Log($"{getName()} is talking to the player. Skipping move this hour.");
            return;
        }

        string placeToGo = dailySchedule[hour % dailySchedule.Count];
        Debug.Log($"{getName()} now heading to: {placeToGo}");

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
