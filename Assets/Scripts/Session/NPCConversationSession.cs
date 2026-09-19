using System;
using System.Collections.Generic;
using System.IO;
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
    private readonly DateTime conversationDateTime;
    private readonly string npc1EventContext;
    private readonly string npc2EventContext;

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
        var timer = UnityEngine.Object.FindObjectOfType<NPCGlobalTimer>();
        conversationDateTime = timer != null ? timer.GetCurrentDateTime() : DateTime.Now;

        npc1EventContext = SocialEventManager.Instance != null
            ? SocialEventManager.Instance.BuildConversationContext(npc1, npc2)
            : "- Global event registry unavailable.";
        npc2EventContext = SocialEventManager.Instance != null
            ? SocialEventManager.Instance.BuildConversationContext(npc2, npc1)
            : "- Global event registry unavailable.";

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

    public override void UpdateMessageHistory(string message)
    {  
        currentSpeakerIndex = 1 - currentSpeakerIndex;
        NPC currentSpeaker = GetCurrentSpeaker();
        string speakerName = currentSpeaker != null ? currentSpeaker.getName() : null;

        if (message.StartsWith("You are now speaking to"))
        {
            messageHistory.Add(message);
        }   
        else
        {
            messageHistory.Add($"{speakerName}: {message}");
        }
    }

    public void AddSpokenLine(NPC speaker, string message)
    {
        if (speaker == null || string.IsNullOrWhiteSpace(message))
            return;

        spokenTranscript.Add($"{speaker.getName()}: {message.Trim()}");
    }

    private string BuildSituationFor(NPC speaker)
    {
        bool isNpc1 = speaker == npc1;

        string currentArea = isNpc1 ? npc1CurrentArea : npc2CurrentArea;
        string heading = isNpc1 ? npc1Heading : npc2Heading;
        string eventContext = isNpc1 ? npc1EventContext : npc2EventContext;

        return
$@"- Current in-game time: {conversationTimestamp}
- You are currently at: {currentArea}
- Before meeting your conversation partner, you were heading to: {heading}
- Registered social-event context relevant to you:
{eventContext}";
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

    public DateTime GetConversationDateTime() => conversationDateTime;

    public NPC GetNPC(int index)
    {
        return index == 0 ? npc1 : npc2;
    }
}
