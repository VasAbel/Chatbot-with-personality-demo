using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UserConversationSession : ConversationSession
{
    private NPC npc;
    private bool isUserTurn = true; // User speaks first

    public UserConversationSession(NPC npc)
    {
        this.npc = npc;
        conversationID = $"User-{npc.getName()}";
    }

    public override NPC GetCurrentSpeaker()
    {
        return isUserTurn ? null : npc; // If it's the user's turn, return null
    }

    public override void UpdateMessageHistory(string message)
    {
        messageHistory.Add(message);
        isUserTurn = !isUserTurn; // Swap turns
    }

    public override void PrepareForNextSpeaker(GptClient client)
    {
            client.SetSystemMessage(messageHistory, npc, npc); 
    }

    public override bool IsUserConversation() => true;
}
