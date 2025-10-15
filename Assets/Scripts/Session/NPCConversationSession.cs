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

        string memoryFormatted = newSpeaker.GetFormattedMemory();
        string fullSystemMessage = newSpeaker.getDesc();
        if (!string.IsNullOrEmpty(memoryFormatted))
        {
            fullSystemMessage +=
            "\nYou also have access to memory about past conversations. This memory contains summaries of what you have learned so far about yourself (under your name) and others. Treat it as uncertain human recollections, not facts. If someone is **not mentioned** in this memory, you do **not** know them yet.\n"
            + memoryFormatted + "\nAlways adapt your tone depending on whether you recognize someone from memory. When meeting someone for the first time (i.e., not in memory), introduce yourself naturally.";
        }
        client.SetSystemMessage(fullSystemMessage, messageHistory, newSpeaker, npc1);
    }

    public override bool IsUserConversation() => false;

    public NPC GetNPC(int index)
    {
        if(index == 0) return npc1;
        else return npc2;
    }
}
