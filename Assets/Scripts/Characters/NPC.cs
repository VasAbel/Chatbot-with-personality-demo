using System;
using System.Collections.Generic;
using UnityEngine;
using Assets.Game_Manager;

/// <summary>
/// Stores the persistent/runtime state that belongs to one NPC.
/// Schedule generation/following, conversation lifecycle, and memory persistence
/// are handled by dedicated components/services.
/// </summary>
public class NPC : MonoBehaviour
{
    public int idx;

    private string npcName;

    public bool isConversationBlocked = false;
    public bool isTalkingToUser = false;
    public NpcMemory memory = new NpcMemory();

    private void Awake()
    {
        ConfigManager.Description initialDescription =
            ConfigManager.Instance.GetFullCharacterDescription(idx);

        npcName = ConfigManager.Instance.GetCharacterName(idx);

        memory.corePersonality = initialDescription.core ?? "";
        memory.currentThoughts.Clear();

        foreach (Thought thought in ParseInitialThoughts(initialDescription.thoughts))
            memory.currentThoughts.Add(thought);
    }

    public string getName() => npcName;

    public string GetCurrentAreaName()
    {
        if (PlaceRegistry.Instance == null)
            return PlaceRegistry.DefaultAreaName;

        return PlaceRegistry.Instance.DescribePosition(transform.position);
    }

    private static IEnumerable<Thought> ParseInitialThoughts(string thoughtsBlock)
    {
        if (string.IsNullOrWhiteSpace(thoughtsBlock))
            yield break;

        long createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string[] lines = thoughtsBlock.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );

        NPCGlobalTimer timer = UnityEngine.Object.FindObjectOfType<NPCGlobalTimer>();
        string gameTimestamp = timer != null
            ? timer.FormatMemoryTimestamp()
            : "Unknown time";

        foreach (string rawLine in lines)
        {
            string text = rawLine.Trim().TrimStart('-').Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            yield return new Thought
            {
                text = text,
                confidence = 0.8f,
                salience = 0.6f,
                createdUnix = createdUnix,
                gameTimestamp = gameTimestamp
            };
        }
    }
}
