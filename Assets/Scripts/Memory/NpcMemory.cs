using System;
using System.Collections.Generic;

[Serializable]
public class Thought
{
    public string text;
    public float confidence;   // 0..1
    public float salience;     // 0..1 (how much it matters to the NPC)
    public long createdUnix;   // DateTimeOffset.UtcNow.ToUnixTimeSeconds()
}

[Serializable]
public class NpcMemory // per NPC
{
    public string corePersonality;                        // concise, stable; starts from desc.description
    public Dictionary<string,string> socialByNpc = new(); // otherName -> summary you know about them
    public List<Thought> currentThoughts = new();         // ephemeral plans/ideas
}
