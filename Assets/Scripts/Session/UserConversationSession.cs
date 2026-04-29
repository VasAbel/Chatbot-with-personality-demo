using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UserConversationSession : ConversationSession
{
    private NPC npc;
    private bool isUserTurn = true;
    public NPC GetNPC() => npc;
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
    - Before meeting the player, you were heading to: {heading}
    - You are talking to a stranger named Jade who is visiting the village.
    - At the end of every reply, append a trust tag: [TRUST_DELTA: N] where N is -5 to +5.
      Use these guidelines:
      - Friendly, warm, curious questions or compliments: +1 or +2
      - Player shows genuine interest in you or the village: +2 to +3
      - Rude, evasive, or suspicious behavior: -1 to -3
      - Neutral small talk: 0
    - If your accumulated trust with this player feels high (they have been consistently warm and genuine),
      naturally say something like 'I would vouch for you with Steve' or 'I'll put in a good word for you'
      or 'Steve should let you in, you seem trustworthy'. Only do this once and only if it genuinely fits.
    - The trust tag must be the very last line of every reply. Never skip it.";

        client.SetSystemMessage(messageHistory, npc, npc, situation);
    }

    public override bool IsUserConversation() => true;
}