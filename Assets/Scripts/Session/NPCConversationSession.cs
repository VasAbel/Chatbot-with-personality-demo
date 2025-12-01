using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

public class NPCConversationSession : ConversationSession
{
    private NPC npc1, npc2;
    private int currentSpeakerIndex = 0;

    public NPCConversationSession(NPC npc1, NPC npc2)
    {
        this.npc1 = npc1;
        this.npc2 = npc2;
        conversationID = $"{this.npc1.getName()}-{this.npc2.getName()}";
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

        if (message.StartsWith("Start a conversation"))
        {
            messageHistory.Add(message);
        }   
        else
        {
            messageHistory.Add($"{speakerName}: {message}");
        }
    }

    public override void PrepareForNextSpeaker(GptClient client)
    {
        NPC newSpeaker = GetCurrentSpeaker();
        client.SetSystemMessage(messageHistory, newSpeaker, npc1);
    }

    public override bool IsUserConversation() => false;

    public NPC GetNPC(int index)
    {
        if(index == 0) return npc1;
        else return npc2;
    }
}
