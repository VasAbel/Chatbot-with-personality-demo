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

    private string BuildSituationFor(NPC speaker)
    {
        bool isNpc1 = speaker == npc1;

        string currentArea = isNpc1 ? npc1CurrentArea : npc2CurrentArea;
        string heading = isNpc1 ? npc1Heading : npc2Heading;

        return
$@"- Current in-game time: {conversationTimestamp}
- You are currently at: {currentArea}
- Before meeting your conversation partner, you were heading to: {heading}";
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
