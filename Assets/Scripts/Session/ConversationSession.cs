using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;

public abstract class ConversationSession
{
    protected List<string> messageHistory = new List<string>();
    public bool IsActive { get; set; } = true;
    public string conversationID { get; protected set; }
    public CancellationTokenSource CancellationTokenSource { get; private set; } = new CancellationTokenSource();
    protected List<string> spokenTranscript = new List<string>();

    public List<string> GetSpokenTranscript()
    {
        return spokenTranscript;
    }

    public abstract NPC GetCurrentSpeaker(); // NPC talking this turn
    public abstract void UpdateMessageHistory(string message);
    public abstract void PrepareForNextSpeaker(GptClient client);
    public abstract bool IsUserConversation();
    public List<string> GetMessageHistory()
    {
        return messageHistory;
    }
}
