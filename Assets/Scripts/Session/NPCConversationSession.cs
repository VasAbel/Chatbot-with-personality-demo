using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class NPCConversationSession : ConversationSession
{
    private NPC npc1, npc2;
    private int currentSpeakerIndex = 0;

    private readonly string npc1CurrentArea;
    private readonly string npc2CurrentArea;
    private readonly string npc1Heading;
    private readonly string npc2Heading;
    private readonly string conversationTimestamp;

    public NPCConversationSession(NPC npc1, NPC npc2)
    {
        this.npc1 = npc1;
        this.npc2 = npc2;
        conversationID = $"{this.npc1.getName()}-{this.npc2.getName()}";

        npc1CurrentArea = npc1.GetCurrentAreaName();
        npc2CurrentArea = npc2.GetCurrentAreaName();
        npc1Heading = npc1.GetHeadingDisplayName();
        npc2Heading = npc2.GetHeadingDisplayName();
        conversationTimestamp = npc1.GetCurrentGameTimestamp();

        Debug.Log(
            $"[Conversation Start] {npc1.getName()} ↔ {npc2.getName()}\n" +
            $"Time: {conversationTimestamp}\n" +
            $"{npc1.getName()} | at: {npc1CurrentArea} | heading to: {npc1Heading}\n" +
            $"{npc2.getName()} | at: {npc2CurrentArea} | heading to: {npc2Heading}"
        );
    }

    public override NPC GetCurrentSpeaker()
    {
        return (currentSpeakerIndex == 0) ? npc1 : npc2;
    }

    // In NPCConversationSession.cs
    public override void UpdateMessageHistory(string message)
    {
        NPC currentSpeaker = GetCurrentSpeaker();
        string speakerName = currentSpeaker != null ? currentSpeaker.getName() : null;

        currentSpeakerIndex = 1 - currentSpeakerIndex; // toggle AFTER reading

        if (message.StartsWith("You are now speaking to"))
            messageHistory.Add(message);
        else
            messageHistory.Add($"{speakerName}: {message}");
    }

    // In NPCConversationSession.cs
    private string BuildSituationFor(NPC speaker)
    {
        bool isNpc1 = speaker == npc1;
        string currentArea = isNpc1 ? npc1CurrentArea : npc2CurrentArea;
        string heading = isNpc1 ? npc1Heading : npc2Heading;

        string rumorBlock = "";
        if (RumorManager.Instance != null)
        {
            var rumors = RumorManager.Instance.GetRumorsKnownBy(speaker.getName());
            if (rumors != null && rumors.Count > 0)
            {
                var lines = rumors.Select(r => $"- \"{r.currentText}\"");
                rumorBlock = $"\n\nIMPORTANT: You have exciting news to share. Your FIRST message MUST mention this — work it naturally into your greeting:\n{string.Join("\n", lines)}";
            }
        }
        else
        {
            Debug.LogWarning("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        }

        return
    $@"- Current in-game time: {conversationTimestamp}
- You are currently at: {currentArea}
- Before meeting your conversation partner, you were heading to: {heading}{rumorBlock}";
    }

    public override void PrepareForNextSpeaker(GptClient client)
    {
        NPC newSpeaker = GetCurrentSpeaker();
        string situation = BuildSituationFor(newSpeaker);
        Debug.Log(
            $"[LLM Context] Speaker: {newSpeaker.getName()}\n{situation}"
        );

        client.SetSystemMessage(messageHistory, newSpeaker, npc1, situation);
    }

    public override bool IsUserConversation() => false;

    public NPC GetNPC(int index)
    {
        return index == 0 ? npc1 : npc2;
    }
}
