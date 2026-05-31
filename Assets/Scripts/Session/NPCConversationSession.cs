using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Assets.Game_Manager;

public class NPCConversationSession : ConversationSession
{
    private NPC npc1, npc2;
    private int currentSpeakerIndex = 0;

    private readonly string npc1CurrentArea;
    private readonly string npc2CurrentArea;
    private readonly string npc1Heading;
    private readonly string npc2Heading;
    private readonly string conversationTimestamp;

    public NPCConversationSession(NPC npc1, NPC npc2)
    {
        this.npc1 = npc1;
        this.npc2 = npc2;
        conversationID = $"{this.npc1.getName()}-{this.npc2.getName()}";

        npc1CurrentArea = npc1.GetCurrentAreaName();
        npc2CurrentArea = npc2.GetCurrentAreaName();
        npc1Heading = npc1.GetHeadingDisplayName();
        npc2Heading = npc2.GetHeadingDisplayName();
        conversationTimestamp = npc1.GetCurrentGameTimestamp();

        Debug.Log(
            $"[Conversation Start] {npc1.getName()} ↔ {npc2.getName()}\n" +
            $"Time: {conversationTimestamp}\n" +
            $"{npc1.getName()} | at: {npc1CurrentArea} | heading to: {npc1Heading}\n" +
            $"{npc2.getName()} | at: {npc2CurrentArea} | heading to: {npc2Heading}"
        );
    }

    public override NPC GetCurrentSpeaker()
    {
        return (currentSpeakerIndex == 0) ? npc1 : npc2;
    }

    // In NPCConversationSession.cs
    public override void UpdateMessageHistory(string message)
    {
        NPC currentSpeaker = GetCurrentSpeaker();
        string speakerName = currentSpeaker != null ? currentSpeaker.getName() : null;

        currentSpeakerIndex = 1 - currentSpeakerIndex; // toggle AFTER reading

        if (message.StartsWith("You are now speaking to"))
            messageHistory.Add(message);
        else
            messageHistory.Add($"{speakerName}: {message}");
    }

    private string BuildSituationFor(NPC speaker)
    {
        bool isNpc1 = speaker == npc1;
        NPC partner = isNpc1 ? npc2 : npc1;
        string currentArea = isNpc1 ? npc1CurrentArea : npc2CurrentArea;
        string heading = isNpc1 ? npc1Heading : npc2Heading;

        // Rumors are already included in the global system prompt via SetSystemMessage.
        // No need to duplicate them here — just let the NPC bring them up naturally.

        string steveBlock = BuildSteveBlock(speaker, partner);

        return
    $@"- Current in-game time: {conversationTimestamp}
- You are currently at: {currentArea}
- Before meeting your conversation partner, you were heading to: {heading}{steveBlock}";
    }

    private string BuildSteveBlock(NPC speaker, NPC partner)
    {
        bool speakerIsSteve = speaker.getName() == "Steve";
        bool partnerIsSteve = partner.getName() == "Steve";

        if (!speakerIsSteve && !partnerIsSteve)
            return "";

        string playerName = ConfigManager.Instance.GetPlayerName();

        if (speakerIsSteve)
        {
            return $"\n\n- You are talking to a fellow villager you trust. " +
                   $"If they mention anything about the stranger {playerName}, pay close attention — " +
                   $"their opinion matters to you when deciding whether to let {playerName} into the Townhouse.";
        }
        else
        {
            string playerOpinion = "";
            foreach (var thought in speaker.memory.currentThoughts)
            {
                if (thought.text.Contains(playerName) && thought.salience > 0.2f)
                {
                    playerOpinion = thought.text;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(playerOpinion) && speaker.memory.socialByNpc.ContainsKey(playerName))
                playerOpinion = speaker.memory.socialByNpc[playerName];

            string opinionHint = !string.IsNullOrWhiteSpace(playerOpinion)
                ? $"\n  Your current impression of {playerName}: \"{playerOpinion}\""
                : $"\n  You may or may not have met {playerName} — share whatever honest impression you have.";

            return $"\n\n- You are talking to Steve, the guard at the Townhouse. " +
                   $"Steve only opens the Townhouse for visitors that the village's trusted residents vouch for. " +
                   $"Your vouch carries real weight with him. " +
                   $"If you have met the stranger named {playerName} and formed an opinion, tell Steve honestly what you think — " +
                   $"a simple 'I think {playerName} seems trustworthy' or 'I'm not sure about {playerName} yet' goes a long way." +
                   opinionHint;
        }
    }

    public override void PrepareForNextSpeaker(GptClient client)
    {
        NPC newSpeaker = GetCurrentSpeaker();
        string situation = BuildSituationFor(newSpeaker);
        Debug.Log(
            $"[LLM Context] Speaker: {newSpeaker.getName()}\n{situation}"
        );

        client.SetSystemMessage(messageHistory, newSpeaker, npc1, situation);
    }

    public override bool IsUserConversation() => false;

    public NPC GetNPC(int index)
    {
        return index == 0 ? npc1 : npc2;
    }
}
