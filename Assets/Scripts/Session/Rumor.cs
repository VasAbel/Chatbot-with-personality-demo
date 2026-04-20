using System;
using System.Collections.Generic;

[Serializable]
public class Rumor
{
    public string rumorId;           
    public string originalText;      
    public string currentText;       // current (possibly distorted) version
    public string heardFrom;         
    public List<string> spreadChain; // ordered list: player -> NPC1 -> NPC2 etc.
    public long createdUnix;

    public Rumor() { }

    public Rumor(string originalText, string heardFrom)
    {
        rumorId = Guid.NewGuid().ToString("N").Substring(0, 8);
        this.originalText = originalText;
        currentText = originalText;
        this.heardFrom = heardFrom;
        spreadChain = new List<string> { heardFrom };
        createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    // Create a copy to pass to the next NPC, with a possibly distorted text
    public Rumor PassTo(string nextNpc, string distortedText = null)
    {
        var copy = new Rumor
        {
            rumorId = rumorId,
            originalText = originalText,
            currentText = distortedText ?? currentText,
            heardFrom = spreadChain[spreadChain.Count - 1],
            spreadChain = new List<string>(spreadChain) { nextNpc },
            createdUnix = createdUnix
        };
        return copy;
    }

    public bool HasReached(string npcName)
    {
        return spreadChain.Contains(npcName);
    }
}