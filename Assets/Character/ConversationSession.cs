using System.Collections.Generic;

public class ConversationSession
{
    private NPC npc1, npc2;
    private int currentSpeakerIndex = 0;
    private List<string> messageHistory = new List<string>();

    public bool IsActive { get;  set; } = true;
    public string conversationID { get; private set; }

    public ConversationSession(NPC npc1, NPC npc2)
    {
        this.npc1 = npc1;
        this.npc2 = npc2;
        conversationID = $"{this.npc1.getName()}-{this.npc2.getName()}";
    }

    public NPC GetCurrentSpeaker()
    {
        return (currentSpeakerIndex == 0) ? npc1 : npc2;
    }

    public void UpdateMessageHistory(string message)
    {
        messageHistory.Add(message);
        currentSpeakerIndex = 1 - currentSpeakerIndex;
    }

    public void PrepareForNextSpeaker(LlamaClient client)
    {
        NPC newSpeaker = GetCurrentSpeaker();
        client.SetSystemMessage(newSpeaker.getDesc(), messageHistory, newSpeaker, npc1);
    }

    public NPC GetNPC1()
    {
        return npc1;
    }

    public NPC GetNPC2()
    {
        return npc2;
    }
}
