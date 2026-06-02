using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Assets.Game_Manager;

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
            vouchBlock = "Every villager you trust has vouched for this stranger: " +
                         string.Join(", ", vouched) + ".";
        else
            vouchBlock = "Vouched for by: " + string.Join(", ", vouched) + ". " +
                         "Still no word from: " + string.Join(", ", unvouched) + ".";

        string rumorBlock = "(no rumors yet)";
        if (RumorManager.Instance != null)
        {
            var rumors = RumorManager.Instance.GetRumorsKnownBy(_guard.getName());
            if (rumors != null && rumors.Count > 0)
                rumorBlock = string.Join("\n",
                    rumors.Select(r => "- From " + r.heardFrom + ": \"" + r.currentText + "\""));
        }

        int vouchedCount = vouched.Count;
        string warmthHint;
        if (vouchedCount >= 2)
            warmthHint = "The village's trusted residents have largely spoken well of this stranger. You are close to being convinced — a sincere conversation could tip the balance.";
        else if (vouchedCount == 1)
            warmthHint = "One trusted villager has vouched for this stranger so far. You are cautiously open, but you want to hear more.";
        else
            warmthHint = "No one you trust has spoken about this stranger yet. Stay measured — be fair but firm.";

        string situation =
            "- Current in-game time: " + _timestamp + "\n" +
            "- You are stationed at: " + _currentArea + "\n" +
            "- Your current trust in this stranger: " + _guardState.TrustLevel.ToString("F0") + " / 100\n" +
            "- " + warmthHint + "\n\n" +
            "VOUCH STATUS:\n" + vouchBlock + "\n\n" +
            "RUMORS AND THINGS YOU'VE HEARD:\n" + rumorBlock;

        BuildGuardSystemMessage(client, situation);
    }

    private void BuildGuardSystemMessage(GptClient client, string situation)
    {
        client.SetSystemMessage(messageHistory, _guard, _guard, situation + ExtraGuardInstructions());
    }

    private string ExtraGuardInstructions()
    {
        bool allVouched = _guardState.requiredVouchers.All(n => _guardState.HasVouch(n));
        int vouchedCount = _guardState.requiredVouchers.Count(n => _guardState.HasVouch(n));

        string stance;
        if (_guardState.IsDoorUnlocked)
            stance = "\n\n*** THE DOOR IS NOW OPEN. YOUR DUTY IS FULFILLED. ***\n" +
                     "You MUST explicitly tell the stranger that the door is open and they may enter the Townhouse. " +
                     "Say something like: 'The door is open — you're welcome to go in.' " +
                     "Be warm and brief. Do NOT ask more questions or act like the door is still closed. " +
                     "This is the most important thing to communicate in your reply.";
        else if (allVouched)
            stance = "\n\nEvery villager you trust has vouched for this stranger. You feel ready to let them in — you are on the verge of opening the door.";
        else if (vouchedCount >= 2)
            stance = "\n\nTwo of your trusted villagers have spoken well of this stranger. You're genuinely warming up. A sincere conversation could be enough to open the door.";
        else if (vouchedCount == 1)
            stance = "\n\nOne trusted villager has vouched for this stranger. That counts for something — but you need more before you open the Townhouse.";
        else
            stance = "\n\nNo one you trust has spoken about this stranger yet. Be fair and hear them out — but don't open the door without good reason.";

        return
            stance +
            "\n\nIMPORTANT — you do NOT know this stranger's name. You have never been introduced to them. " +
            "If they ask whether you know them or what their name is, be honest that you don't know it unless they " +
            "tell you their name during THIS conversation. Never invent, guess, or assume a name for them." +
            "\n\nMandatory — end EVERY reply with this tag on its own line, no exceptions:\n" +
            "[TRUST_DELTA: N]\n" +
            "N is an integer from -10 to +10. Reflect how this exchange felt to you as Steve:\n" +
            "- Stranger is open, honest, warm, genuinely curious about the village: +3 to +5\n" +
            "- Shares a personal reason that feels real and compelling: +5 to +8\n" +
            "- Stranger is evasive, impatient, or avoids your questions: -2 to -4\n" +
            "- Stranger tries to pressure, bribe, or manipulate you: -5 to -10\n" +
            "- Neutral small talk: +1\n" +
            "The tag must be the very last line of your reply. Never skip it.";
    }
}
