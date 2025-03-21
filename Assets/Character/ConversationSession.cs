using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class ConversationSession
{
    protected List<string> messageHistory = new List<string>();
    public bool IsActive { get; set; } = true;
    public string conversationID { get; protected set; }

    public abstract NPC GetCurrentSpeaker(); // NPC talking this turn
    public abstract void UpdateMessageHistory(string message);
    public abstract void PrepareForNextSpeaker(LlamaClient client);
    public abstract bool IsUserConversation();
}
