using System.Collections.Generic;

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
        messageHistory.Add(message);
        currentSpeakerIndex = 1 - currentSpeakerIndex;
    }

    public override void PrepareForNextSpeaker(LlamaClient client)
    {
        NPC newSpeaker = GetCurrentSpeaker();
        client.SetSystemMessage(newSpeaker.getDesc(), messageHistory, newSpeaker, npc1);
    }

    public override bool IsUserConversation() => false;
}
