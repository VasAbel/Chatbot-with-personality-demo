using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public class UserConversationSession : ConversationSession
{
    private readonly NPC npc;
    private readonly IReadOnlyList<NPC> participants;
    private bool isUserTurn = true;

    private readonly TMP_InputField dialogueBox;
    private readonly GameObject responseBox;

    private readonly string currentArea;
    private readonly string heading;
    private readonly string timestamp;
    private readonly DateTime conversationDateTime;
    private readonly string eventContext;

    public UserConversationSession(NPC npc, TMP_InputField dialogueBox, GameObject responseBox)
    {
        this.npc = npc;
        this.dialogueBox = dialogueBox;
        this.responseBox = responseBox;
        participants = new[] { npc };

        conversationID = $"User-{npc.getName()}";
        currentArea = npc.GetCurrentAreaName();

        NpcScheduleController schedule = npc.GetComponent<NpcScheduleController>();
        heading = schedule != null ? schedule.GetHeadingDisplayName() : "unknown place";

        var timer = UnityEngine.Object.FindObjectOfType<NPCGlobalTimer>();
        conversationDateTime = timer != null ? timer.GetCurrentDateTime() : DateTime.Now;
        timestamp = timer != null ? timer.GetFullTimestamp() : "Unknown time";

        eventContext = SocialEventManager.Instance != null
            ? SocialEventManager.Instance.BuildConversationContext(npc, null, partnerIsUser: true)
            : "- Global event registry unavailable.";
    }

    public override IReadOnlyList<NPC> Participants => participants;
    public override DateTime ConversationDateTime => conversationDateTime;
    public override bool IsAuthoritativeUserConversation => true;

    public override NPC GetCurrentSpeaker()
    {
        return isUserTurn ? null : npc;
    }

    public override void UpdateMessageHistory(string message)
    {
        messageHistory.Add(message);
        isUserTurn = !isUserTurn;
    }

    public override void AddExternalSpokenLine(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            spokenTranscript.Add($"User: {message.Trim()}");
    }

    public override async Task<string> WaitForExternalInputAsync(CancellationToken token)
    {
        if (dialogueBox == null)
            return null;

        dialogueBox.gameObject.SetActive(true);
        dialogueBox.text = "";
        dialogueBox.ActivateInputField();

        var tcs = new TaskCompletionSource<string>();

        void OnSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            dialogueBox.onSubmit.RemoveListener(OnSubmit);
            dialogueBox.gameObject.SetActive(false);
            tcs.TrySetResult(text);
        }

        dialogueBox.onSubmit.AddListener(OnSubmit);

        using (token.Register(() =>
        {
            dialogueBox.onSubmit.RemoveListener(OnSubmit);
            dialogueBox.gameObject.SetActive(false);
            tcs.TrySetCanceled();
        }))
        {
            try
            {
                return await tcs.Task;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
        }
    }

    public override void ShowNpcResponse(string response)
    {
        if (responseBox == null)
            return;

        responseBox.SetActive(true);
        TMP_Text text = responseBox.GetComponentInChildren<TMP_Text>();
        if (text != null)
            text.SetText(response);
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

        string systemPrompt = NpcConversationPromptBuilder.BuildNormalPrompt(
            npc,
            situation,
            authoritativeUserConversation: true
        );

        client.SetConversationContext(
            systemPrompt,
            messageHistory,
            evenHistoryEntriesAreUser: true
        );
    }

    public override void OnReleased()
    {
        npc.isTalkingToUser = false;
    }
    
}
