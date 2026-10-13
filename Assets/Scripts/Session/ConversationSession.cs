using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public abstract class ConversationSession
{
    protected readonly List<string> messageHistory = new List<string>();
    protected readonly List<string> spokenTranscript = new List<string>();

    public bool IsActive { get; set; } = true;
    public string conversationID { get; protected set; }
    public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

    public abstract IReadOnlyList<NPC> Participants { get; }
    public abstract DateTime ConversationDateTime { get; }
    public virtual bool IsAuthoritativeUserConversation => false;

    public virtual bool IsClosingRequested => false;

    public IReadOnlyList<string> GetSpokenTranscript() => spokenTranscript;
    public IReadOnlyList<string> GetMessageHistory() => messageHistory;

    public abstract NPC GetCurrentSpeaker();
    public abstract void UpdateMessageHistory(string message);
    public abstract void PrepareForNextSpeaker(GptClient client);

    public virtual string BuildInitialPrompt() => "";

    public virtual void AddNpcSpokenLine(NPC speaker, string message)
    {
        if (speaker == null || string.IsNullOrWhiteSpace(message))
            return;

        spokenTranscript.Add($"{speaker.getName()}: {message.Trim()}");
    }

    public virtual void AddExternalSpokenLine(string message)
    {
    }

    public virtual Task<string> WaitForExternalInputAsync(CancellationToken token)
    {
        return Task.FromResult<string>(null);
    }

    public virtual void ShowNpcResponse(string response)
    {
    }

    public virtual bool RequestGracefulClose()
    {
        return false;
    }

    public virtual void PrepareForClosingSpeaker(GptClient client, bool isFinalReply)
    {
        PrepareForNextSpeaker(client);
    }

    public virtual void OnReleased()
    {
    }
}
