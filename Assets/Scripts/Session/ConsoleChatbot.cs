using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;
using System.Text.RegularExpressions;

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

        Debug.Log("!sess.IsUserConv");
        if (!session.IsUserConversation())
        {
            NPC partner = ((NPCConversationSession)session).GetNPC(1);

            initialPrompt = 
        $@"You are now speaking to {partner.getName()}.
        Start with a natural greeting (1 short sentence).

        Then follow this rule:
        - If {partner.getName()} is *already* in the Social memory section of your character description:
            • Treat them as someone you already know.
            • Do NOT introduce yourself again.
            • Do NOT repeat basic facts about yourself unless it makes sense in context.
        - If {partner.getName()} does NOT appear in the Social memory section of your character description:
            • Treat this as your first meeting.
            • Briefly introduce yourself once, including your name.

        Do NOT mention 'social memory' or these instructions in your reply.
        Now say your first message to {partner.getName()}.";
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
                    
                    string raw = await client.SendChatMessageAsync(initialPrompt);

                    string response = StripSpeakerPrefix(raw, currentSpeaker.name);

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
Reply with VALID JSON ONLY (no markdown, no commentary).
Use exactly this schema:

{
  ""core"": {
    ""add"":    [""...""],
    ""update"": [{ ""old"": ""..."", ""new"": ""..."" }],
    ""remove"": [""...""]
  },
  ""social"": {
    ""NPC_NAME"": {
      ""add"":    [""...""],
      ""update"": [{ ""old"": ""..."", ""new"": ""..."" }],
      ""remove"": [""...""]
    }
  },
  ""thoughts"": {
    ""add"":       [""...""],
    ""reinforce"": [""...""],
    ""drop"":      [""...""]
  }
}

DEFINITIONS (very important):

- core:
  Facts about THIS NPC (self) that are **stable or long-term**:
  profession, long-held preferences, deep values, recurring habits.
  Core facts should still be true months or years later.
  Never put information about other people into core.
  Never put temporary plans, current projects, or ""currently ..."" sentences here.

- social:
  What THIS NPC believes about **other NPCs**:
  their traits, habits, preferences, roles, and changes in their life.
  Keys in ""social"" must be other NPC names only (never the self name).
  Social items can include both stable and temporary facts, as long as they are about others.

- thoughts:
  Short-term or **evolving ideas/plans** of THIS NPC:
  things they are currently considering, wanting to do, or thinking about.
  These are volatile and can appear, change, or disappear quickly.
  Use thoughts for items that contain phrases like ""currently"", ""thinking about"",
  ""considering"", ""planning to"", ""wants to"", ""might"", ""soon"", etc.

CLASSIFICATION RULES:

- If a sentence describes **an ongoing plan, project, or idea** (anything with
  ""currently"", ""thinking about"", ""considering"", ""planning"", ""wants to"" etc.),
  it belongs in **thoughts**, NOT in core.

- If a sentence is **biographical and time-insensitive** (e.g. job, long-term preference,
  ""loves X"", ""often does Y"", ""values Z""), it belongs in **core**.

- If a sentence describes someone else (another NPC), it belongs in **social**
  under that NPC's name, never in core or thoughts.

DUPLICATES, UPDATES, REMOVALS:

- Before adding anything new, carefully compare with the existing memory:
  - If the new info is basically the same meaning as an existing sentence,
    do NOT put it in ""add"".
    Instead, either:
      * skip it, OR
      * put it in ""update"" with { ""old"": existing_sentence, ""new"": improved_sentence }.

- ""core.add"" and ""social[NAME].add"":
  - Must contain only facts that are **not already present in any form** in the relevant section.

- ""core.update"" and ""social[NAME].update"":
  - Use an object { ""old"": ""..."", ""new"": ""..."" }.
  - ""old"" should be copied from an existing sentence,
    taken from PREVIOUS CORE or PREVIOUS SOCIAL.
  - ""new"" is the updated or refined version that should replace ""old"".

- ""core.remove"" and ""social[NAME].remove"":
  - List facts that are clearly contradicted or explicitly abandoned
    by what happened in the conversation OR by items you are adding/updating now.
  - E.g. if you add a new fact that directly conflicts with an old one,
    include the old one in ""remove"".

- When deciding what to remove, consider:
  - the previous memory AND
  - the facts you are about to add or update in this same JSON.
  Anything clearly inconsistent with the new state should be removed.

THOUGHTS-SPECIFIC RULES:

- ""thoughts.add"": new short-term ideas/plans of this NPC.
- ""thoughts.reinforce"": thoughts already present that were repeated or strongly supported.
- ""thoughts.drop"": thoughts that now feel outdated, unimportant, or clearly abandoned.

GENERAL RULES:

- Keep strings concise (< 120 chars).
- Avoid near-duplicates; do not restate the same idea with slightly different wording.
- ""core"", ""social"", and ""thoughts"" must always be JSON OBJECTS, not arrays.
- If there are no changes for a section, you may either:
  - omit that section completely, OR
  - include it as an empty object: {}.
- Limit each list (core.add, core.update, core.remove,
  each social[NAME].add/update/remove, thoughts.add/reinforce/drop) to at most 3 items.
";



        string promptFor(NPC self, NPC partner) => $@"
Self Name: {self.getName()}
Partner in this conversation: {partner.getName()}

Previous CORE (about {self.getName()} only, long-term traits):
{self.memory.corePersonality}

Previous SOCIAL (what {self.getName()} believes about others):
{string.Join("\n\n", self.memory.socialByNpc.Select(kv =>
    $"{kv.Key}:\n{kv.Value}"))}

Previous THOUGHTS (short-term plans/ideas of {self.getName()}):
{string.Join("\n", self.memory.currentThoughts.Select(t =>
    $"- {t.text} (salience {Mathf.RoundToInt(t.salience * 100)}%, confidence {Mathf.RoundToInt(t.confidence * 100)}%)"
))}

Conversation (latest session, including speaker names):
{fullConversation}

Task:
- Decide what to add, update, or remove in core, social, and thoughts.
- Only consider information that was actually revealed or implied in THIS conversation.
- Remember:
  - core & thoughts are ONLY about {self.getName()},
  - social is ONLY about others (never {self.getName()}).
- Use the JSON schema exactly as described above (with update objects {{ ""old"", ""new"" }}).

Return ONLY the JSON object now.";



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

        [Serializable] public class UpdatePair
        {
            public string old;
            public string @new;
        }

        [Serializable] public class CoreDelta
        {
            public List<string> add;
            public List<UpdatePair> update;
            public List<string> remove;
        }

        [Serializable] public class SocialDelta
        {
            public List<string> add;
            public List<UpdatePair> update;
            public List<string> remove;
        }

        [Serializable] public class ThoughtsDelta
        {
            public List<string> add;
            public List<string> reinforce;
            public List<string> drop;
        }
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

        // =========================
        // CORE
        // =========================
        if (delta.core != null)
        {
            // Split existing core into lines, ignore empties
            var lines = string.IsNullOrWhiteSpace(npc.memory.corePersonality)
                ? new List<string>()
                : npc.memory.corePersonality
                    .Split('\n')
                    .Select(l => l.TrimEnd('\r'))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

            // Helper: strip "- " prefix and whitespace
            string StripBullet(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                return s.Trim().TrimStart('-', ' ').Trim();
            }

            // 1) explicit removals first
            if (delta.core.remove != null)
            {
                foreach (var r in delta.core.remove.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    var target = StripBullet(r);
                    lines.RemoveAll(l =>
                    {
                        var raw = StripBullet(l);
                        return raw.Equals(target, StringComparison.OrdinalIgnoreCase)
                            || RoughlySameFact(raw, target);
                    });
                }
            }

            // Helper: add bullet if not already present (by exact or roughly-same text)
            void AddIfNotExists(string fact)
            {
                if (string.IsNullOrWhiteSpace(fact)) return;
                var target = StripBullet(fact);

                bool exists = lines.Any(l =>
                {
                    var raw = StripBullet(l);
                    return raw.Equals(target, StringComparison.OrdinalIgnoreCase)
                        || RoughlySameFact(raw, target);
                });

                if (!exists)
                    lines.Add("- " + target);
            }

            // 2) updates: explicit { old, new }
            if (delta.core.update != null)
            {
                foreach (var u in delta.core.update)
                {
                    if (u == null) continue;
                    var oldText = StripBullet(u.old);
                    var newText = StripBullet(u.@new);

                    if (string.IsNullOrWhiteSpace(newText)) continue;

                    // If we have an old to replace, try to find it
                    if (!string.IsNullOrWhiteSpace(oldText))
                    {
                        int idx = lines.FindIndex(l =>
                        {
                            var raw = StripBullet(l);
                            return raw.Equals(oldText, StringComparison.OrdinalIgnoreCase)
                                || RoughlySameFact(raw, oldText);
                        });

                        if (idx >= 0)
                        {
                            lines[idx] = "- " + newText;
                            continue;
                        }
                    }

                    // If no matching old found, just treat as an add
                    AddIfNotExists(newText);
                }
            }

            // 3) additions: only add if not already present
            if (delta.core.add != null)
            {
                foreach (var a in delta.core.add.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    AddIfNotExists(a);
                }
            }

            // keep core short
            if (lines.Count > 18)
                lines = lines.Take(18).ToList();

            npc.memory.corePersonality = string.Join("\n", lines);
        }

        // =========================
        // SOCIAL
        // =========================
        if (delta.social != null)
        {
            if (npc.memory.socialByNpc == null)
                npc.memory.socialByNpc = new Dictionary<string, string>();

            foreach (var entry in delta.social)
            {
                var otherName = entry.Key?.Trim();
                var sDelta = entry.Value;

                if (string.IsNullOrEmpty(otherName) || sDelta == null)
                    continue;

                npc.memory.socialByNpc.TryGetValue(otherName, out var existing);

                var lines = string.IsNullOrWhiteSpace(existing)
                    ? new List<string>()
                    : existing
                        .Split('\n')
                        .Select(l => l.TrimEnd('\r'))
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

                string StripBullet(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                    return s.Trim().TrimStart('-', ' ').Trim();
                }

                void AddIfNotExists(string fact)
                {
                    if (string.IsNullOrWhiteSpace(fact)) return;
                    var target = StripBullet(fact);

                    bool exists = lines.Any(l =>
                    {
                        var raw = StripBullet(l);
                        return raw.Equals(target, StringComparison.OrdinalIgnoreCase)
                            || RoughlySameFact(raw, target);
                    });

                    if (!exists)
                        lines.Add("- " + target);
                }

                void ApplyRemoves(List<string> removes)
                {
                    if (removes == null) return;

                    foreach (var r in removes.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        var target = StripBullet(r);
                        lines.RemoveAll(line =>
                        {
                            var raw = StripBullet(line);
                            return raw.Equals(target, StringComparison.OrdinalIgnoreCase)
                                || RoughlySameFact(raw, target);
                        });
                    }
                }

                void ApplyUpdates(List<MemoryDeltaRoot.UpdatePair> updates)
                {
                    if (updates == null) return;

                    foreach (var u in updates)
                    {
                        if (u == null) continue;
                        var oldText = StripBullet(u.old);
                        var newText = StripBullet(u.@new);

                        if (string.IsNullOrWhiteSpace(newText)) continue;

                        if (!string.IsNullOrWhiteSpace(oldText))
                        {
                            int idx = lines.FindIndex(line =>
                            {
                                var raw = StripBullet(line);
                                return raw.Equals(oldText, StringComparison.OrdinalIgnoreCase)
                                    || RoughlySameFact(raw, oldText);
                            });

                            if (idx >= 0)
                            {
                                lines[idx] = "- " + newText;
                                continue;
                            }
                        }

                        // No matching old → treat as add
                        AddIfNotExists(newText);
                    }
                }

                void ApplyAdds(List<string> adds)
                {
                    if (adds == null) return;
                    foreach (var s in adds)
                        AddIfNotExists(s);
                }

                // Order: remove → update → add
                ApplyRemoves(sDelta.remove);
                ApplyUpdates(sDelta.update);
                ApplyAdds(sDelta.add);

                // Keep per-person social memory bounded
                if (lines.Count > 18)
                    lines = lines.Take(18).ToList();

                npc.memory.socialByNpc[otherName] = string.Join("\n", lines);
            }
        }

        // =========================
        // THOUGHTS
        // =========================
        if (delta.thoughts != null)
        {
            // Apply some decay each update (you can tune this)
            npc.DecayThoughts(1.0f);

            if (npc.memory.currentThoughts == null)
                npc.memory.currentThoughts = new List<Thought>();

            var thoughts = npc.memory.currentThoughts;

            Thought FindSimilar(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                var trimmed = text.Trim();
                return thoughts.FirstOrDefault(t => RoughlySameFact(t.text, trimmed));
            }

            // ADD: create new or strengthen similar
            if (delta.thoughts.add != null)
            {
                foreach (var s in delta.thoughts.add)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var trimmed = s.Trim();

                    var existing = FindSimilar(trimmed);
                    if (existing != null)
                    {
                        // treat as implicit reinforce
                        existing.salience   = Mathf.Clamp01(existing.salience + 0.2f);
                        existing.confidence = Mathf.Clamp01(existing.confidence + 0.1f);
                    }
                    else
                    {
                        thoughts.Add(new Thought
                        {
                            text        = trimmed,
                            confidence  = 0.6f,
                            salience    = 0.6f,
                            createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        });
                    }
                }
            }

            // REINFORCE: strengthen existing similar thoughts
            if (delta.thoughts.reinforce != null)
            {
                foreach (var s in delta.thoughts.reinforce)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var existing = FindSimilar(s);
                    if (existing != null)
                    {
                        existing.salience   = Mathf.Clamp01(existing.salience + 0.2f);
                        existing.confidence = Mathf.Clamp01(existing.confidence + 0.2f);
                    }
                }
            }

            // DROP: remove similar thoughts
            if (delta.thoughts.drop != null)
            {
                foreach (var s in delta.thoughts.drop)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var trimmed = s.Trim();
                    thoughts.RemoveAll(t => RoughlySameFact(t.text, trimmed));
                }
            }

            // keep only top N by salience to control tokens
            npc.memory.currentThoughts = thoughts
                .OrderByDescending(t => t.salience)
                .Take(7)
                .ToList();
        }
    }


    private static bool RoughlySameFact(string oldLine, string newLine)
    {
        if (string.IsNullOrWhiteSpace(oldLine) || string.IsNullOrWhiteSpace(newLine))
            return false;

        // Normalize: remove bullet, trim, strip trailing punctuation like . ! ? ; :
        string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            s = s.TrimStart('-', ' ').Trim(); // remove leading bullet and spaces
            s = s.Trim();                     // general trim
            s = s.TrimEnd('.', '!', '?', ';', ':', '…'); // strip common end punctuation
            return s.Trim();
        }

        var o = Normalize(oldLine);
        var n = Normalize(newLine);

        if (string.IsNullOrEmpty(o) || string.IsNullOrEmpty(n))
            return false;

        // Exact match after normalization
        if (o.Equals(n, StringComparison.OrdinalIgnoreCase))
            return true;

        // One contains the other (ignoring final punctuation)
        if (n.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (o.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Prefix comparison on first ~30 chars
        int prefix = Math.Min(30, Math.Min(o.Length, n.Length));
        if (prefix >= 10)
        {
            var oPref = o.Substring(0, prefix);
            var nPref = n.Substring(0, prefix);
            if (oPref.Equals(nPref, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }


    private static string StripSpeakerPrefix(string text, string speakerName)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Patterns like: "Tim:", "**Tim:**", "TIM -", "Tim —"
        var patterns = new[]
        {
            $@"^\s*\**{Regex.Escape(speakerName)}\**\s*[:\-–—]\s*",
            @"^\s*\**assistant\**\s*[:\-–—]\s*",
            @"^\s*\**npc\**\s*[:\-–—]\s*"
        };

        foreach (var p in patterns)
            text = Regex.Replace(text, p, "", RegexOptions.IgnoreCase);

        // Also strip surrounding quotes/backticks if present
        text = Regex.Replace(text, @"^[`""]+|[`""]+$", "");

        return text.Trim();
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