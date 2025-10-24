using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;

public class ConsoleChatbot : MonoBehaviour
{
    public GptClient client;
    private Dictionary<string, ConversationSession> activeConversations = new Dictionary<string, ConversationSession>();
    private readonly SemaphoreSlim conversationSemaphore = new SemaphoreSlim(1, 1);
    public TMP_InputField userInputField;
    public GameObject messageInputField;

    public void StartChatSession(ConversationSession session)
    {

        if (activeConversations.ContainsKey(session.conversationID))
        {
            Debug.LogWarning($"Conversation {session.conversationID} is already active.");
            return;
        }

        activeConversations[session.conversationID] = session;
        Debug.Log($"Conversation {session.conversationID} started.");

        _ = RunConversation(session);
    }

    private async Task RunConversation(ConversationSession session)
    {
        CancellationToken token = session.CancellationTokenSource.Token;

        string logFilePath = Path.Combine(Application.persistentDataPath, $"{session.conversationID}.txt");
        Debug.Log($"Log file saved at: {logFilePath}");

        string initialPrompt = "";

        if (!session.IsUserConversation())
        {
            NPC partner = ((NPCConversationSession)session).GetNPC(1);
            initialPrompt = $"Start a conversation with {partner.getName()}. Check your memory to see if you have met them before. If they are NOT in memory, treat them as a stranger and introduce yourself. Do **not** refer to this prompt just start the conversation right away.";
        }

        while (session.IsActive)
        {
            bool isUserConversation = session.IsUserConversation();

            // 🟢 Only lock the semaphore if we're not waiting for user input
            if (!isUserConversation || session.GetCurrentSpeaker() != null)
            {
                await conversationSemaphore.WaitAsync();

                session.PrepareForNextSpeaker(client);
                NPC currentSpeaker = session.GetCurrentSpeaker();

                try
                {
                    // NPC-NPC or NPC responding in a user conversation
                    string response = await client.SendChatMessageAsync(initialPrompt);

                    if (session.CancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    string logEntry = $"{currentSpeaker.getName()}: {response}";
                    Debug.Log(logEntry);

                    File.AppendAllText(logFilePath, logEntry + "\n");
                    
                    currentSpeaker.GetComponent<ChatBubbleAnchor>()?.Show(logEntry);

                    session.UpdateMessageHistory(initialPrompt);
                    initialPrompt = response;
                }
                finally
                {
                    conversationSemaphore.Release();
                }
            }
            else
            {

                Debug.Log("User's turn. Please type a message:");
                string userInput = await WaitForUserInput(token);
                if (string.IsNullOrEmpty(userInput)) break;


                await conversationSemaphore.WaitAsync();

                session.PrepareForNextSpeaker(client);

                try
                {
                    session.UpdateMessageHistory(userInput);

                    string response = await client.SendChatMessageAsync(userInput);
                    messageInputField.gameObject.SetActive(true);
                    messageInputField.GetComponentInChildren<TMP_Text>().SetText(response);

                    string logEntry = $"Partner: {response}";

                    Debug.Log(logEntry);
                    File.AppendAllText(logFilePath, logEntry + "\n");

                    session.UpdateMessageHistory(response);
                }
                finally
                {
                    conversationSemaphore.Release();
                }
            }
        }

        Debug.Log("Conversation ended.");
        File.AppendAllText(logFilePath, "Conversation ended.\n");
    }



    private async Task<string> WaitForUserInput(CancellationToken token)
    {
        userInputField.gameObject.SetActive(true);
        userInputField.text = "";
        userInputField.ActivateInputField();

        TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

        void OnSubmit(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                userInputField.gameObject.SetActive(false);
                userInputField.onSubmit.RemoveListener(OnSubmit);
                tcs.SetResult(text);
            }
        }

        userInputField.onSubmit.AddListener(OnSubmit);

        await using (token.Register(() =>
        {
            userInputField.onSubmit.RemoveListener(OnSubmit);
            userInputField.gameObject.SetActive(false);
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

    public bool StopSession(string conversationID)
    {
        if (activeConversations.TryGetValue(conversationID, out var session))
        {
            session.CancellationTokenSource.Cancel();
            session.IsActive = false;

            Debug.Log($"Conversation {conversationID} was stopped.");
            activeConversations.Remove(conversationID);

            if (!conversationID.StartsWith("User-"))
            {
                _ = UpdateMemoryForSession(session);
            }

            return true;
        }

        return false;
    }

    private static string SanitizeJson(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "{}";

        // strip ```json fences if any
        if (raw.StartsWith("```"))
        {
            int s = raw.IndexOf('{');
            int e = raw.LastIndexOf('}');
            if (s >= 0 && e > s) raw = raw.Substring(s, e - s + 1);
        }

        // trim to outermost {...}
        int start = raw.IndexOf('{');
        int end   = raw.LastIndexOf('}');
        if (start >= 0 && end > start) raw = raw.Substring(start, end - start + 1);

        // normalize smart quotes
        raw = raw.Replace('“', '"').Replace('”', '"');

        return raw.Trim();
    }

    private async Task UpdateMemoryForSession(ConversationSession session)
    {
        var npc1 = ((NPCConversationSession)session).GetNPC(0);
        var npc2 = ((NPCConversationSession)session).GetNPC(1);

        string fullConversation = string.Join("\n", session.GetMessageHistory());

        string baseInstr = @"
You are a memory updater for a role-playing simulation.
Reply with VALID JSON ONLY (no markdown fences, no commentary).
Use this schema exactly:

{
  ""core"": { ""add"": [""...""], ""update"": [""...""], ""remove"": [""...""] },
  ""social"": { ""NPC_NAME"": { ""summary"": ""..."" } },
  ""thoughts"": { ""add"": [""...""], ""reinforce"": [""...""], ""drop"": [""...""] }
}

Rules:
- Keep strings concise (< 120 chars).
- No duplicates.
- Move item from thoughts → core only if it seems stable/persistent.
- If a belief is revised, update or remove the old one.
- Thoughts = short-term plans / evolving ideas.
- Limit each list to at most 3 items (omit empty lists).";

        string promptFor(NPC self, NPC partner) => $@"
        NPC: {self.getName()}
        Partner: {partner.getName()}

        PREVIOUS CORE:
        {self.memory.corePersonality}

        PREVIOUS SOCIAL:
        {string.Join("\n", self.memory.socialByNpc.Select(kv => $"{kv.Key}: {kv.Value}"))}

        PREVIOUS THOUGHTS:
        {string.Join("\n", self.memory.currentThoughts.Select(t => $"- {t.text} (salience {(int)(t.salience * 100)}%)"))}

        CONVERSATION:
        {fullConversation}

        Return the JSON object now.";

        string json1, json2;

        if (client is GptClient gpt)
        {
            json1 = await gpt.RequestJsonAsync(baseInstr, promptFor(npc1, npc2), 700);
            json2 = await gpt.RequestJsonAsync(baseInstr, promptFor(npc2, npc1), 700);
        }
        else
        {
            // Fallback: old text path (system+user concatenated)
            json1 = await client.SendChatMessageAsync(baseInstr + "\n\n" + promptFor(npc1, npc2));
            json2 = await client.SendChatMessageAsync(baseInstr + "\n\n" + promptFor(npc2, npc1));
        }

        json1 = SanitizeJson(json1);
        json2 = SanitizeJson(json2);

        ApplyMemoryJson(npc1, json1);
        ApplyMemoryJson(npc2, json2);

        npc1.LogMemoryToFile();
        npc2.LogMemoryToFile();
    }
    
    [Serializable] class MemoryDeltaRoot
    {
        public CoreDelta core;
        public Dictionary<string, SocialDelta> social;
        public ThoughtsDelta thoughts;

        [Serializable] public class CoreDelta { public List<string> add; public List<string> update; public List<string> remove; }
        [Serializable] public class SocialDelta { public string summary; }
        [Serializable] public class ThoughtsDelta { public List<string> add; public List<string> reinforce; public List<string> drop; }
    }

    private void ApplyMemoryJson(NPC npc, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        MemoryDeltaRoot delta = null;
        try
        {
            delta = Newtonsoft.Json.JsonConvert.DeserializeObject<MemoryDeltaRoot>(json);
        }
        catch
        {
            Debug.LogWarning($"Memory JSON parse failed for {npc.getName()}, raw:\n{json}");
            return;
        }
        if (delta == null) return;

        // CORE
        if (delta.core != null)
        {
            // super simple core: treat it as bullet text; append adds/updates; remove lines that contain the string
            var lines = npc.memory.corePersonality.Split('\n').ToList();

            void AppendList(List<string> list)
            {
                if (list == null) return;
                foreach (var s in list)
                    if (!string.IsNullOrWhiteSpace(s) && !lines.Any(l => l.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                        lines.Add("- " + s.Trim());
            }

            AppendList(delta.core.add);
            AppendList(delta.core.update);

            if (delta.core.remove != null)
                lines.RemoveAll(l => delta.core.remove.Any(r => l.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0));

            // keep core short
            if (lines.Count > 18) lines = lines.Take(18).ToList();
            npc.memory.corePersonality = string.Join("\n", lines);
        }

        // SOCIAL
        if (delta.social != null)
        {
            foreach (var kv in delta.social)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                npc.memory.socialByNpc[kv.Key] = kv.Value.summary?.Trim();
            }
        }

        // THOUGHTS
        if (delta.thoughts != null)
        {
            npc.DecayThoughts(0.0f); // no decay right now, changes are explicit
            // add
            if (delta.thoughts.add != null)
            {
                foreach (var s in delta.thoughts.add)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    npc.memory.currentThoughts.Add(new Thought
                    {
                        text = s.Trim(),
                        confidence = 0.6f,
                        salience = 0.6f,
                        createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    });
                }
            }
            // reinforce
            if (delta.thoughts.reinforce != null)
            {
                foreach (var s in delta.thoughts.reinforce)
                {
                    var t = npc.memory.currentThoughts.FirstOrDefault(x =>
                        x.text.IndexOf(s ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (t != null)
                    {
                        t.salience = Mathf.Clamp01(t.salience + 0.2f);
                        t.confidence = Mathf.Clamp01(t.confidence + 0.2f);
                    }
                }
            }
            // drop
            if (delta.thoughts.drop != null)
            {
                npc.memory.currentThoughts.RemoveAll(x =>
                    delta.thoughts.drop.Any(s => x.text.IndexOf(s ?? "", StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // keep only top N by salience to control tokens
            npc.memory.currentThoughts = npc.memory.currentThoughts
                .OrderByDescending(t => t.salience)
                .Take(12)
                .ToList();
        }
    }

    private void OnApplicationQuit()
    {
        StopAllConversations();
    }

    public void StopAllConversations()
    {
        Debug.Log("Stopping all conversations...");
        foreach (var session in activeConversations.Values)
        {
            session.IsActive = false;
            session.CancellationTokenSource.Cancel(); // ←✅ important!
        }

        activeConversations.Clear();

        Debug.Log("Conversations have ended.");
    }
}