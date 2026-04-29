using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GuardConversationSession : ConversationSession
{
    private readonly NPC _guard;
    private readonly GuardState _guardState;
    private bool _isUserTurn = true;
    private readonly string _currentArea;
    private readonly string _heading;
    private readonly string _timestamp;

    public GuardConversationSession(NPC guard, GuardState guardState)
    {
        _guard = guard;
        _guardState = guardState;
        conversationID = $"User-{guard.getName()}";

        _currentArea = guard.GetCurrentAreaName();
        _heading = guard.GetHeadingDisplayName();
        _timestamp = guard.GetCurrentGameTimestamp();
    }

    public NPC GetNPC() => _guard;

    public override NPC GetCurrentSpeaker() => _isUserTurn ? null : _guard;

    public override void UpdateMessageHistory(string message)
    {
        messageHistory.Add(message);
        _isUserTurn = !_isUserTurn;
    }

    public override bool IsUserConversation() => true;

    public override void PrepareForNextSpeaker(GptClient client)
    {
        //Build vouch status string
        var vouched = _guardState.requiredVouchers
                                   .Where(n => _guardState.HasVouch(n))
                                   .ToList();
        var unvouched = _guardState.requiredVouchers
                                   .Where(n => !_guardState.HasVouch(n))
                                   .ToList();

        string vouchBlock;
        if (vouched.Count == 0)
            vouchBlock = "None of the villagers have spoken well of this stranger yet.";
        else if (unvouched.Count == 0)
            vouchBlock = $"Every villager you trust has vouched for this stranger: " +
                         $"{string.Join(", ", vouched)}.";
        else
            vouchBlock = $"Vouched for by: {string.Join(", ", vouched)}. " +
                         $"Still no word from: {string.Join(", ", unvouched)}.";

        string rumorBlock = "(no rumors yet)";
        if (RumorManager.Instance != null)
        {
            var rumors = RumorManager.Instance.GetRumorsKnownBy(_guard.getName());
            if (rumors != null && rumors.Count > 0)
                rumorBlock = string.Join("\n",
                    rumors.Select(r => $"- From {r.heardFrom}: \"{r.currentText}\""));
        }

        string situation =
        $@"- Current in-game time: {_timestamp}
        - You are stationed at: {_currentArea}
        - Your trust in this stranger: {_guardState.TrustLevel:F0} / 100

        VOUCH STATUS:
        {vouchBlock}

        RUMORS YOU HAVE HEARD:
        {rumorBlock}";
        BuildGuardSystemMessage(client, situation);
    }

    private void BuildGuardSystemMessage(GptClient client, string situation)
    {
        client.SetSystemMessage(messageHistory, _guard, _guard, situation + ExtraGuardInstructions());
    }

    private string ExtraGuardInstructions()
    {
        bool allVouched = _guardState.requiredVouchers
                                     .All(n => _guardState.HasVouch(n));
        string stance = _guardState.IsDoorUnlocked
            ? "\n\nThe door is now open. You have stepped aside and allow the stranger to pass."
            : allVouched
                ? "\n\nEvery villager you trust has vouched for this stranger. " +
                  "You are still cautious, but you are very close to letting them in."
                : "\n\nYou will not open the Townhouse until you have heard good word " +
                  "from ALL of the village's trusted residents: Tim, Amy, and Gabriel. " +
                  "You can be warmed by honest conversation, but you will not be tricked.";
        return @"
MANDATORY — append this tag at the end of EVERY reply, no exceptions:
[TRUST_DELTA: N]
N is an integer from -10 to +10. Use these guidelines:
- Friendly small talk, warmth, curiosity about the village: +1 or +2
- Player shows genuine respect for the village or its people: +3 to +5
- Player mentions Tim, Amy, or Gabriel positively: +3
- Player is evasive, pushy, or asks to enter without earning trust: -2 to -5
- Neutral or unclear: 0
The tag must be the very last line. Never skip it." + stance;
    }
}