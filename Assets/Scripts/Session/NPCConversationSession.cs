using System;
using System.Collections.Generic;
using UnityEngine;

public class NPCConversationSession : ConversationSession
{
    private readonly NPC npc1;
    private readonly NPC npc2;
    private readonly IReadOnlyList<NPC> participants;
    private int currentSpeakerIndex;

    private readonly string npc1CurrentArea;
    private readonly string npc2CurrentArea;
    private readonly string npc1Heading;
    private readonly string npc2Heading;
    private readonly string conversationTimestamp;
    private readonly DateTime conversationDateTime;
    private readonly string npc1EventContext;
    private readonly string npc2EventContext;

    private bool closingRequested;

    public NPCConversationSession(NPC npc1, NPC npc2)
    {
        this.npc1 = npc1;
        this.npc2 = npc2;
        participants = new[] { npc1, npc2 };
        conversationID = $"{npc1.getName()}-{npc2.getName()}";

        npc1CurrentArea = npc1.GetCurrentAreaName();
        npc2CurrentArea = npc2.GetCurrentAreaName();
        npc1Heading = GetScheduleHeading(npc1);
        npc2Heading = GetScheduleHeading(npc2);

        var timer = UnityEngine.Object.FindObjectOfType<NPCGlobalTimer>();
        conversationDateTime = timer != null ? timer.GetCurrentDateTime() : DateTime.Now;
        conversationTimestamp = timer != null ? timer.GetFullTimestamp() : "Unknown time";

        npc1EventContext = SocialEventManager.Instance != null
            ? SocialEventManager.Instance.BuildConversationContext(npc1, npc2)
            : "- Global event registry unavailable.";

        npc2EventContext = SocialEventManager.Instance != null
            ? SocialEventManager.Instance.BuildConversationContext(npc2, npc1)
            : "- Global event registry unavailable.";

        Debug.Log(
            $"[Conversation Start] {npc1.getName()} ↔ {npc2.getName()}\n" +
            $"Time: {conversationTimestamp}\n" +
            $"{npc1.getName()} | at: {npc1CurrentArea} | heading to: {npc1Heading}\n" +
            $"{npc2.getName()} | at: {npc2CurrentArea} | heading to: {npc2Heading}"
        );
    }

    private static string GetScheduleHeading(NPC npc)
    {
        NpcScheduleController schedule = npc.GetComponent<NpcScheduleController>();
        return schedule != null ? schedule.GetHeadingDisplayName() : "unknown place";
    }

    public override IReadOnlyList<NPC> Participants => participants;
    public override DateTime ConversationDateTime => conversationDateTime;
    public override bool IsClosingRequested => closingRequested;

    public override NPC GetCurrentSpeaker()
    {
        return currentSpeakerIndex == 0 ? npc1 : npc2;
    }

    public override void UpdateMessageHistory(string message)
    {
        currentSpeakerIndex = 1 - currentSpeakerIndex;
        NPC nextSpeaker = GetCurrentSpeaker();

        if (message.StartsWith("You are now speaking to"))
            messageHistory.Add(message);
        else
            messageHistory.Add($"{nextSpeaker.getName()}: {message}");
    }

    private string BuildSituationFor(NPC speaker)
    {
        bool isNpc1 = speaker == npc1;
        string currentArea = isNpc1 ? npc1CurrentArea : npc2CurrentArea;
        string heading = isNpc1 ? npc1Heading : npc2Heading;
        string eventContext = isNpc1 ? npc1EventContext : npc2EventContext;

        return
$@"- Current in-game time: {conversationTimestamp}
- You are currently at: {currentArea}
- Before meeting your conversation partner, you were heading to: {heading}
- Registered social-event context relevant to you:
{eventContext}";
    }

    public override void PrepareForNextSpeaker(GptClient client)
    {
        NPC newSpeaker = GetCurrentSpeaker();
        string systemPrompt = NpcConversationPromptBuilder.BuildNormalPrompt(
            newSpeaker,
            BuildSituationFor(newSpeaker)
        );

        client.SetConversationContext(
            systemPrompt,
            messageHistory,
            evenHistoryEntriesAreUser: newSpeaker == npc1
        );
    }

    public override bool RequestGracefulClose()
    {
        if (closingRequested)
            return false;

        closingRequested = true;
        return true;
    }

    public override void PrepareForClosingSpeaker(GptClient client, bool isFinalReply)
    {
        NPC newSpeaker = GetCurrentSpeaker();

        Debug.Log(
            $"[LLM Closing Context] Speaker: {newSpeaker.getName()} | finalReply={isFinalReply} | using dedicated compact closing prompt"
        );

        string systemPrompt = NpcConversationPromptBuilder.BuildClosingPrompt(
            newSpeaker,
            isFinalReply
        );

        client.SetConversationContext(
            systemPrompt,
            messageHistory,
            evenHistoryEntriesAreUser: newSpeaker == npc1,
            historyStartIndex: Math.Max(0, messageHistory.Count - 6)
        );
    }

    public override string BuildInitialPrompt()
    {
        bool knowsPartner = KnowsPartner(npc1, npc2);

        string partnerKnowsAboutMe = "";
        if (npc2.memory.socialByNpc != null &&
            npc2.memory.socialByNpc.TryGetValue(npc1.getName(), out var knownByPartner) &&
            !string.IsNullOrWhiteSpace(knownByPartner))
        {
            partnerKnowsAboutMe = knownByPartner;
        }

        return knowsPartner
            ? NpcConversationPromptBuilder.BuildKnownPartnerPrompt(npc2, partnerKnowsAboutMe)
            : NpcConversationPromptBuilder.BuildFirstMeetingPrompt();
    }

    private static bool KnowsPartner(NPC self, NPC partner)
    {
        if (self?.memory?.socialByNpc == null)
            return false;

        return self.memory.socialByNpc.TryGetValue(partner.getName(), out string value) &&
               !string.IsNullOrWhiteSpace(value);
    }

}
