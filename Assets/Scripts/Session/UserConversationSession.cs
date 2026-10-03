using System;
using UnityEngine;

public class UserConversationSession : ConversationSession
{
    private readonly NPC npc;
    private bool isUserTurn = true;

    private readonly string currentArea;
    private readonly string heading;
    private readonly string timestamp;
    private readonly DateTime conversationDateTime;
    private readonly string eventContext;

    public UserConversationSession(NPC npc)
    {
        this.npc = npc;
        conversationID = $"User-{npc.getName()}";

        currentArea = npc.GetCurrentAreaName();
        heading = npc.GetHeadingDisplayName();
        timestamp = npc.GetCurrentGameTimestamp();

        var timer = UnityEngine.Object.FindObjectOfType<NPCGlobalTimer>();
        conversationDateTime = timer != null ? timer.GetCurrentDateTime() : DateTime.Now;

        eventContext = SocialEventManager.Instance != null
            ? SocialEventManager.Instance.BuildConversationContext(npc, null, partnerIsUser: true)
            : "- Global event registry unavailable.";
    }

    public override NPC GetCurrentSpeaker()
    {
        return isUserTurn ? null : npc;
    }

    public override void UpdateMessageHistory(string message)
    {
        messageHistory.Add(message);
        isUserTurn = !isUserTurn;
    }

    public void AddUserLine(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            spokenTranscript.Add($"User: {message.Trim()}");
    }

    public void AddNpcLine(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            spokenTranscript.Add($"{npc.getName()}: {message.Trim()}");
    }

    public override void PrepareForNextSpeaker(GptClient client)
    {
        string situation =
$@"- Current in-game time: {timestamp}
- You are currently at: {currentArea}
- Before meeting the player, you were heading to: {heading}
- You are speaking directly with the player, who is an authoritative controller of the simulation.
- Registered social-event context relevant to you:
{eventContext}";

        client.SetSystemMessage(messageHistory, npc, npc, situation, authoritativeUserConversation: true);
    }

    public override bool IsUserConversation() => true;

    public NPC GetNPC() => npc;

    public DateTime GetConversationDateTime() => conversationDateTime;
}
