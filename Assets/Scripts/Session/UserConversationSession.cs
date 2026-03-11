using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UserConversationSession : ConversationSession
{
    private NPC npc;
    private bool isUserTurn = true;

    private readonly string currentArea;
    private readonly string heading;
    private readonly string timestamp;

    public UserConversationSession(NPC npc)
    {
        this.npc = npc;
        conversationID = $"User-{npc.getName()}";

        currentArea = npc.GetCurrentAreaName();
        heading = npc.GetHeadingDisplayName();
        timestamp = npc.GetCurrentGameTimestamp();
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

    public override void PrepareForNextSpeaker(GptClient client)
    {
        string situation =
$@"- Current in-game time: {timestamp}
- You are currently at: {currentArea}
- Before meeting the player, you were heading to: {heading}";

        client.SetSystemMessage(messageHistory, npc, npc, situation);
    }

    public override bool IsUserConversation() => true;
}