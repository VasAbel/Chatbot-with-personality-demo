using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Assets.Game_Manager;

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
        string playerName = ConfigManager.Instance.GetPlayerName();
        int exchanges = messageHistory.Count;
        string vouchGuidance;
        if (exchanges >= 6)
        {
            vouchGuidance =
                $"- By now you've had a real conversation with {playerName} and have formed an impression. " +
                $"If it's positive, it's natural to say so — you might mention you'd be happy to speak well of them " +
                $"to others in the village, including Steve. Say it in your own words, once, only if it genuinely fits.";
        }
        else if (exchanges >= 4)
        {
            vouchGuidance =
                $"- You're getting a sense of who {playerName} is. " +
                $"If the conversation has felt warm and genuine, you can let that show.";
        }
        else
        {
            vouchGuidance =
                $"- You've only just met {playerName}. Be friendly and curious — ask about them.";
        }

        string situation =
        $@"- Current in-game time: {timestamp}
- You are currently at: {currentArea}
- Before this encounter, you were heading to: {heading}
- You are talking to a stranger who is visiting the village. You don't know their name or story yet — let them tell you.
{vouchGuidance}
- At the end of every reply, append a trust tag on its own line: [TRUST_DELTA: N] where N is -5 to +5.
  Guidelines:
  * Warm, curious, or genuine: +1 to +2
  * Shows real interest in the village or its people: +2 to +3
  * Mentions something you care about: +2
  * Rude, evasive, pushy, or suspicious: -1 to -3
  * Neutral small talk: 0
- Never skip the trust tag. It must be the very last line.";

        client.SetSystemMessage(messageHistory, npc, npc, situation);
    }

    public override bool IsUserConversation() => true;
}