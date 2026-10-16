using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Coordinates active conversation sessions and their lifecycle.
/// Memory persistence and event-registry updates are delegated to dedicated services.
/// </summary>
public class ConversationManager : MonoBehaviour
{
    public GptClient client;

    [SerializeField] private bool logMemoryDeltasToConsole = true;
    [SerializeField] private bool logMemoryDeltasToFile = true;

    private readonly Dictionary<string, ConversationSession> activeConversations =
        new Dictionary<string, ConversationSession>();

    // The GPT client keeps mutable conversation history for live dialogue.
    // Prompt preparation + live send therefore remain one exclusive operation.
    // NpcMemoryManager shares this semaphore to preserve the previous request ordering.
    private readonly SemaphoreSlim conversationSemaphore = new SemaphoreSlim(1, 1);

    private NpcMemoryManager memoryManager;

    public NpcMemoryManager MemoryManager
    {
        get
        {
            EnsureMemoryManager();
            return memoryManager;
        }
    }

    private void EnsureMemoryManager()
    {
        if (memoryManager == null)
        {
            memoryManager = new NpcMemoryManager(
                client,
                conversationSemaphore,
                () => logMemoryDeltasToConsole,
                () => logMemoryDeltasToFile
            );
        }
    }

    public bool TryStartNpcConversation(NPC npc1, NPC npc2)
    {
        if (npc1 == null || npc2 == null || npc1 == npc2)
            return false;

        if (npc1.isConversationBlocked || npc2.isConversationBlocked)
            return false;

        ConversationSession session = ConversationFactory.CreateNpcSession(npc1, npc2);
        if (!TryStartSession(session))
            return false;

        npc1.isConversationBlocked = true;
        npc2.isConversationBlocked = true;
        return true;
    }

    public bool TryStartUserConversation(
        NPC npc,
        TMPro.TMP_InputField dialogueBox,
        GameObject responseBox)
    {
        if (npc == null || npc.isConversationBlocked)
            return false;

        // The simulation has one player, so only one user conversation can be active.
        if (activeConversations.Values.Any(s => s.IsAuthoritativeUserConversation))
            return false;

        ConversationSession session = ConversationFactory.CreateUserSession(
            npc,
            dialogueBox,
            responseBox
        );

        if (!TryStartSession(session))
            return false;

        npc.isConversationBlocked = true;
        npc.isTalkingToUser = true;
        return true;
    }

    public bool IsUserConversationActiveWith(NPC npc)
    {
        return npc != null &&
               activeConversations.Values.Any(session =>
                   session.IsAuthoritativeUserConversation &&
                   session.Participants.Contains(npc));
    }

    private bool TryStartSession(ConversationSession session)
    {
        if (session == null)
            return false;

        if (activeConversations.ContainsKey(session.conversationID))
        {
            Debug.LogWarning($"Conversation {session.conversationID} is already active.");
            return false;
        }

        EnsureMemoryManager();
        activeConversations[session.conversationID] = session;
        _ = RunConversationAsync(session);
        return true;
    }

    private sealed class ConversationRunState
    {
        public string pendingPrompt;
        public bool closingStarted;
        public int closingTurnsRemaining;
    }

    private async Task RunConversationAsync(ConversationSession session)
    {
        string logFilePath = Path.Combine(
            Application.persistentDataPath,
            $"{session.conversationID}.txt"
        );

        var state = new ConversationRunState
        {
            pendingPrompt = session.BuildInitialPrompt()
        };

        CancellationToken token = session.CancellationTokenSource.Token;

        while (session.IsActive)
        {
            NPC speaker = session.GetCurrentSpeaker();

            bool continueRunning = speaker != null
                ? await RunNpcTurnAsync(session, state, logFilePath)
                : await RunExternalTurnAsync(session, token, logFilePath);

            if (!continueRunning)
                break;
        }

        File.AppendAllText(logFilePath, "Conversation ended.\n");

        // Graceful NPC closure finishes inside the run loop, so post-processing
        // starts here only after both final turns are part of the transcript.
        if (session.IsClosingRequested)
        {
            activeConversations.Remove(session.conversationID);
            await FinalizeConversationAsync(session);
        }
    }

    private async Task<bool> RunNpcTurnAsync(
        ConversationSession session,
        ConversationRunState state,
        string logFilePath)
    {
        await conversationSemaphore.WaitAsync();
        try
        {
            if (session.IsClosingRequested && !state.closingStarted)
            {
                state.closingStarted = true;
                state.closingTurnsRemaining = 2;
                Debug.Log($"[Conversation Closing] {session.conversationID}: starting two-turn graceful closure.");
            }

            if (state.closingStarted)
            {
                session.PrepareForClosingSpeaker(
                    client,
                    isFinalReply: state.closingTurnsRemaining == 1
                );
            }
            else
            {
                session.PrepareForNextSpeaker(client);
            }

            NPC speaker = session.GetCurrentSpeaker();
            if (speaker == null)
                return false;

            string raw = await client.SendChatMessageAsync(state.pendingPrompt ?? "");
            string response = StripSpeakerPrefix(raw, speaker.getName());

            if (session.CancellationTokenSource.IsCancellationRequested)
                return false;

            LogThirdNpcMentions(session, speaker, response);

            string logEntry = $"{speaker.getName()}: {response}";
            Debug.Log(logEntry);
            File.AppendAllText(logFilePath, logEntry + "\n");
            speaker.GetComponent<ChatBubbleAnchor>()?.Show(logEntry);

            session.AddNpcSpokenLine(speaker, response);
            session.UpdateMessageHistory(state.pendingPrompt ?? "");
            state.pendingPrompt = response;

            if (state.closingStarted)
            {
                state.closingTurnsRemaining--;
                if (state.closingTurnsRemaining <= 0)
                {
                    session.IsActive = false;
                    Debug.Log($"[Conversation Closing] {session.conversationID}: graceful closing turns completed.");
                }
            }

            return true;
        }
        finally
        {
            conversationSemaphore.Release();
        }
    }

    private async Task<bool> RunExternalTurnAsync(
        ConversationSession session,
        CancellationToken token,
        string logFilePath)
    {
        string externalInput = await session.WaitForExternalInputAsync(token);
        if (string.IsNullOrWhiteSpace(externalInput))
            return false;

        await conversationSemaphore.WaitAsync();
        try
        {
            session.PrepareForNextSpeaker(client);
            session.AddExternalSpokenLine(externalInput);
            session.UpdateMessageHistory(externalInput);

            NPC speaker = session.GetCurrentSpeaker();
            if (speaker == null)
                return false;

            string raw = await client.SendChatMessageAsync(externalInput);
            string response = StripSpeakerPrefix(raw, speaker.getName());

            if (session.CancellationTokenSource.IsCancellationRequested)
                return false;

            session.ShowNpcResponse(response);

            string logEntry = $"{speaker.getName()}: {response}";
            Debug.Log(logEntry);
            File.AppendAllText(logFilePath, $"User: {externalInput}\n");
            File.AppendAllText(logFilePath, logEntry + "\n");

            session.AddNpcSpokenLine(speaker, response);
            session.UpdateMessageHistory(response);

            return true;
        }
        finally
        {
            conversationSemaphore.Release();
        }
    }

    public bool RequestNpcConversationClose(NPC npc1, NPC npc2)
    {
        ConversationSession session = FindNpcConversation(npc1, npc2);
        if (session == null)
            return false;

        bool accepted = session.RequestGracefulClose();
        if (accepted)
            Debug.Log($"Conversation {session.conversationID} received a graceful-close request.");

        return accepted;
    }

    public bool StopUserConversation(NPC npc)
    {
        ConversationSession session = activeConversations.Values.FirstOrDefault(candidate =>
            candidate.IsAuthoritativeUserConversation &&
            candidate.Participants.Contains(npc));

        if (session == null)
            return false;

        // Preserve the previous behavior: after the player closes the visible
        // conversation, the NPC may move again while post-processing keeps it blocked
        // from starting another conversation.
        npc.isTalkingToUser = false;

        session.CancellationTokenSource.Cancel();
        session.IsActive = false;
        activeConversations.Remove(session.conversationID);

        _ = FinalizeConversationAsync(session);
        return true;
    }

    private ConversationSession FindNpcConversation(NPC npc1, NPC npc2)
    {
        if (npc1 == null || npc2 == null)
            return null;

        return activeConversations.Values.FirstOrDefault(session =>
            !session.IsAuthoritativeUserConversation &&
            session.Participants.Count == 2 &&
            session.Participants.Contains(npc1) &&
            session.Participants.Contains(npc2));
    }

    private async Task FinalizeConversationAsync(ConversationSession session)
    {
        bool skipped = ShouldSkipPostProcessing(session);

        try
        {
            if (skipped)
            {
                Debug.LogWarning(
                    $"[MEMORY-SKIP] Skipping post-processing for {session.conversationID}: " +
                    "conversation transcript is empty or too short."
                );
                return;
            }

            if (SocialEventManager.Instance != null)
            {
                await SocialEventManager.Instance.UpdateEventsFromConversationAsync(session, client);
            }
            else
            {
                Debug.LogWarning("[EVENTS] SocialEventManager is missing, so no global event update was performed.");
            }

            await MemoryManager.UpdateMemoryForSessionAsync(session);
        }
        finally
        {
            if (session.Participants.Count == 2)
            {
                MeasurementLogger.Instance?.LogConversationFinished(
                    session.Participants[0],
                    session.Participants[1],
                    session.conversationID,
                    session.GetSpokenTranscript().Count,
                    skipped
                );
            }

            foreach (NPC npc in session.Participants)
            {
                if (npc != null)
                    npc.isConversationBlocked = false;
            }

            session.OnReleased();
        }
    }

    private static bool ShouldSkipPostProcessing(ConversationSession session)
    {
        if (session == null)
            return true;

        IReadOnlyList<string> transcript = session.GetSpokenTranscript();
        return transcript == null ||
               transcript.Count < 2 ||
               !transcript.Any(line => !string.IsNullOrWhiteSpace(line));
    }

    // Thin compatibility/dependency facade for NPC daily lifecycle.
    // The memory logic itself lives entirely in NpcMemoryManager.
    public Task CleanNpcMemoryAsync(NPC npc)
    {
        return MemoryManager.CleanNpcMemoryAsync(npc);
    }

    public void StopAllConversations()
    {
        foreach (ConversationSession session in activeConversations.Values)
        {
            session.IsActive = false;
            session.CancellationTokenSource.Cancel();
        }

        activeConversations.Clear();
    }

    protected virtual void OnApplicationQuit()
    {
        StopAllConversations();
    }

    private static string StripSpeakerPrefix(string text, string speakerName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string[] patterns =
        {
            $@"^\s*\**{Regex.Escape(speakerName)}\**\s*[:\-–—]\s*",
            @"^\s*\**assistant\**\s*[:\-–—]\s*",
            @"^\s*\**npc\**\s*[:\-–—]\s*"
        };

        foreach (string pattern in patterns)
            text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"^[`""]+|[`""]+$", "");
        return text.Trim();
    }

    private static void LogThirdNpcMentions(
        ConversationSession session,
        NPC speaker,
        string response)
    {
        if (session.Participants.Count != 2 || speaker == null)
            return;

        NPC listener = session.Participants[0] == speaker
            ? session.Participants[1]
            : session.Participants[0];

        foreach (NPC npc in UnityEngine.Object.FindObjectsOfType<NPC>())
        {
            if (npc == null)
                continue;

            string mentionedName = npc.getName();
            if (mentionedName == speaker.getName() || mentionedName == listener.getName())
                continue;

            if (Regex.IsMatch(
                response,
                $@"\b{Regex.Escape(mentionedName)}\b",
                RegexOptions.IgnoreCase))
            {
                MeasurementLogger.Instance?.LogNpcMention(
                    speaker,
                    listener,
                    mentionedName,
                    session.conversationID
                );
            }
        }
    }
}
