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

    // --- Memory update logging ---
    [SerializeField] private bool logMemoryDeltasToConsole = true;
    [SerializeField] private bool logMemoryDeltasToFile = true;

    // counts conversations per pair, e.g. "Amy<->Gabriel" => 5
    private readonly Dictionary<string, int> _pairConversationCounts = new Dictionary<string, int>();

    private string GetPairKey(string a, string b)
    {
        // stable ordering so a<->b equals b<->a
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}<->{b}" : $"{b}<->{a}";
    }

    private int NextPairCount(string a, string b)
    {
        string key = GetPairKey(a, b);
        if (!_pairConversationCounts.TryGetValue(key, out int c)) c = 0;
        c++;
        _pairConversationCounts[key] = c;
        return c;
    }

    private static string SafeShort(string s, int max = 10000)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r\n", "\n");
        return s.Length <= max ? s : s.Substring(0, max) + "\n...(truncated)";
    }

    private void LogMemoryDelta(string selfName, string partnerName, string conversationId, int pairCount,
                                string jsonDelta, NpcMemory beforeMem, NPC selfNpcAfter)
    {
        string header = $"[MEMORY-DELTA] self={selfName} partner={partnerName} pair={GetPairKey(selfName, partnerName)} #{pairCount} convID={conversationId} time={DateTime.Now}";
        
        // before/after snapshots (core + social partner + thoughts)
        string BeforeCore = beforeMem.corePersonality ?? "";
        string AfterCore  = selfNpcAfter.memory.corePersonality ?? "";

        beforeMem.socialByNpc.TryGetValue(partnerName, out var beforeSocialPartner);
        selfNpcAfter.memory.socialByNpc.TryGetValue(partnerName, out var afterSocialPartner);

        string beforeThoughts = (beforeMem.currentThoughts == null) ? "" :
            string.Join("\n", beforeMem.currentThoughts
                .OrderByDescending(t => t.salience)
                .Select(t => $"- {t.text} (sal {t.salience:0.00}, conf {t.confidence:0.00})"));

        string afterThoughts = (selfNpcAfter.memory.currentThoughts == null) ? "" :
            string.Join("\n", selfNpcAfter.memory.currentThoughts
                .OrderByDescending(t => t.salience)
                .Select(t => $"- {t.text} (sal {t.salience:0.00}, conf {t.confidence:0.00})"));

        string block =
    $@"{header}

    DELTA JSON:
    {SafeShort(jsonDelta, 15000)}

    BEFORE CORE:
    {SafeShort(BeforeCore, 6000)}

    AFTER CORE:
    {SafeShort(AfterCore, 6000)}

    BEFORE SOCIAL[{partnerName}]:
    {SafeShort(beforeSocialPartner ?? "(none)", 3000)}

    AFTER SOCIAL[{partnerName}]:
    {SafeShort(afterSocialPartner ?? "(none)", 3000)}

    BEFORE THOUGHTS:
    {SafeShort(beforeThoughts, 6000)}

    AFTER THOUGHTS:
    {SafeShort(afterThoughts, 6000)}

    ------------------------------------------------------------
    ";

        if (logMemoryDeltasToConsole)
            Debug.Log(block);

        if (logMemoryDeltasToFile)
        {
            string path = Path.Combine(Application.persistentDataPath, "memory_deltas.log");
            File.AppendAllText(path, block);
        }
    }

    private NpcMemory CloneMemory(NpcMemory m)
    {
        if (m == null) return new NpcMemory();
        return new NpcMemory
        {
            corePersonality = m.corePersonality,
            socialByNpc = (m.socialByNpc != null)
                ? new Dictionary<string, string>(m.socialByNpc)
                : new Dictionary<string, string>(),
            currentThoughts = (m.currentThoughts != null)
                ? m.currentThoughts.Select(t => new Thought
                {
                    text = t.text,
                    salience = t.salience,
                    confidence = t.confidence,
                    createdUnix = t.createdUnix
                }).ToList()
                : new List<Thought>()
        };
    }

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
            NPC currentSpeaker = ((NPCConversationSession)session).GetNPC(0);
            NPC partner = ((NPCConversationSession)session).GetNPC(1);

            bool knows = KnowsPartner(currentSpeaker, partner);
            string partnerKnowsAboutMe = "";

            if (partner.memory.socialByNpc != null &&
                partner.memory.socialByNpc.TryGetValue(currentSpeaker.getName(), out var knownByPartner) &&
                !string.IsNullOrWhiteSpace(knownByPartner))
            {
                partnerKnowsAboutMe = knownByPartner;
            }

            initialPrompt = knows
        ? BuildKnownPartnerPrompt(partner, partnerKnowsAboutMe)
        : BuildFirstMeetingPrompt(partner);
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
Use exactly this schema (types matter):

{
  ""core"": {
    ""add"":    [""...""],
    ""update"": [{ ""index"": 0, ""new"": ""..."" }],
    ""remove"": [0, 1]
  },
  ""social"": {
    ""NPC_NAME"": {
      ""add"":    [""...""],
      ""update"": [{ ""index"": 0, ""new"": ""..."" }],
      ""remove"": [0, 1]
    }
  },
  ""thoughts"": {
    ""add"":       [""...""],
    ""reinforce"": [0, 1],
    ""remove"":      [0, 1]
  }
}

SECTION DEFINITIONS:

- core: stable, long-term facts about THIS NPC (job, values, deep preferences, recurring habits).
  Things that are true even months or years later.
  Never put information about other people into core.
  Never put temporary plans, current projects or transient thoughts here.

- social:
  What THIS NPC believes about OTHER NPCs, their traits, habits, preferences, roles, and changes in their life.
  Keys in ""social"" must be other NPC names only (never the self name).

- thoughts:
  Short-term or **evolving ideas/plans** of THIS NPC: current projects, considering/planning/might/soon
  Volatile thoughts that can appear, change, or disappear quickly.

CLASSIFY WITH THESE EXAMPLES:
- core (about SELF): ""Teaches history."" ""Values craftsmanship."" ""Often hikes on weekends."" ""Believes healthy food is important to be happy.""
- thoughts: ""Currently building a table."" ""Considering collaborating with Gabriel soon."" ""Thinking about hosting a party."" ""Planning to visit her brother.""
- social (about OTHERS): ""Gabriel teaches history and loves storytelling."" ""John is currently planning to throw a party.""

INDEX RULES (CRITICAL):
- PREVIOUS CORE / SOCIAL / THOUGHTS will be given as indexed lists (0..N-1).
- update/remove/reinforce must ONLY use those indices to mark which item should be updated/removed/reinforced.
- add must contain ONLY truly new items not already present in meaning. add mustn't contain entries that are conceptually the same as an existing item with a different phrasing. Only new, previously unmemorized information should be added.
- If something is the same idea with new detail: use update (not add).
- Only remove core/social if clearly contradicted or explicitly abandoned (not just unmentioned).

GENERAL RULES:

- Keep strings concise (< 120 chars).
- Avoid near-duplicates; do not restate the same idea with slightly different wording.
- ""core"", ""social"", and ""thoughts"" must always be JSON OBJECTS, not arrays.
- If there are no changes for a section, you may either:
  - omit that section completely, OR
  - include it as an empty object: {}.
";



        string promptFor(NPC self, NPC partner) => $@"
Self: {self.getName()}
Other NPC in this conversation: {partner.getName()}

Previous CORE (indexed, about {self.getName()} only):
{ToIndexedLines(self.memory.corePersonality
    .Split('\n')
    .Select(l => l.Trim())
    .Where(l => !string.IsNullOrWhiteSpace(l)))}

Previous SOCIAL (indexed per NPC, what {self.getName()} believes about others):
{string.Join("\n\n", self.memory.socialByNpc.Select(kv =>
$@"[{kv.Key}]
{ToIndexedLines((kv.Value ?? "")
    .Split('\n')
    .Select(l => l.Trim())
    .Where(l => !string.IsNullOrWhiteSpace(l)))}"))}

Previous THOUGHTS (indexed, short-term plans/ideas of {self.getName()}):
{(self.memory.currentThoughts == null || self.memory.currentThoughts.Count == 0
? "(none)"
: string.Join("\n", self.memory.currentThoughts
    .OrderByDescending(t => t.salience)
    .ThenByDescending(t => t.confidence)
    .Select((t,i) => $"{i}: {t.text.Trim()} (sal {Mathf.RoundToInt(t.salience*100)}%, conf {Mathf.RoundToInt(t.confidence*100)}%)")))}

CURRENT Conversation (latest session, including speaker names):
{fullConversation}

Task:
- Decide what to add, update, remove, or reinforce in core, social, and thoughts.
- Only consider information that was actually revealed or implied in the CURRENT conversation.
- Remember:
  - core & thoughts are ONLY about {self.getName()},
  - social is ONLY about others (never {self.getName()}).
- Use the JSON schema exactly as described above.

Return ONLY the JSON object.";

        int pairCount = NextPairCount(npc1.getName(), npc2.getName());
        string convId = session.conversationID;

        // Snapshot BEFORE
        var npc1BeforeMem = CloneMemory(npc1.memory);
        var npc2BeforeMem = CloneMemory(npc2.memory);

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

        LogMemoryDelta(npc1.getName(), npc2.getName(), convId, pairCount, json1, npc1BeforeMem, npc1);
        LogMemoryDelta(npc2.getName(), npc1.getName(), convId, pairCount, json2, npc2BeforeMem, npc2);

        npc1.LogMemoryToFile();
        npc2.LogMemoryToFile();
    }

    private static bool KnowsPartner(NPC self, NPC partner)
    {
        if (self?.memory?.socialByNpc == null) return false;
        if (!self.memory.socialByNpc.TryGetValue(partner.getName(), out var v)) return false;
        return !string.IsNullOrWhiteSpace(v);
    }

    private static string BuildKnownPartnerPrompt(NPC partner, string partnerKnowsAboutMe)
    {
        return $@"
    You are now speaking to {partner.getName()}.

    Start with a natural greeting (1 short sentence).
    Treat them as someone you already know (an acquaintance), you find information about them in your memory under the tag [{partner.getName()}]. Do NOT introduce yourself.

    Based on past conversations, you believe {partner.getName()} already knows these things about you:
    {(string.IsNullOrWhiteSpace(partnerKnowsAboutMe) ? "- (nothing specific yet)" : partnerKnowsAboutMe)}

    Important:
    - Avoid re-explaining the above unless correcting or meaningfully expanding it.
    - Do not mention 'memory' or these instructions.
    Now say your first message.";
    }

    private static string BuildFirstMeetingPrompt(NPC partner)
    {
            return $@"
        You are now speaking to {partner.getName()}, but you have never met before.

        Important:
        - You do NOT know their name yet.
        - Treat this as a first meeting.

        Start with a natural greeting (1 short sentence) and briefly introduce yourself once (including your name).
        Do not mention 'memory' or these instructions.
        Now say your first message.";
    }

    static string ToIndexedLines(IEnumerable<string> lines)
    {
        if (lines == null) return "(none)";
        var arr = lines.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (arr.Count == 0) return "(none)";
        return string.Join("\n", arr.Select((s,i) => $"{i}: {s.Trim().TrimStart('-',' ').Trim()}"));
    }
    
    [Serializable] class MemoryDeltaRoot
    {
        public CoreDelta core;
        public Dictionary<string, SocialDelta> social;
        public ThoughtsDelta thoughts;

        [Serializable] public class UpdateByIndex { public int index; public string @new; }

        [Serializable] public class CoreDelta
        {
            public List<string> add;
            public List<UpdateByIndex> update;
            public List<int> remove;
        }

        [Serializable] public class SocialDelta
        {
            public List<string> add;
            public List<UpdateByIndex> update;
            public List<int> remove;
        }

        [Serializable] public class ThoughtsDelta
        {
            public List<string> add;
            public List<int> reinforce;
            public List<int> remove;
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

        // Common helpers
        static string StripBullet(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return s.Trim().TrimStart('-', ' ').Trim();
        }

        static List<string> SplitLines(string block)
        {
            return string.IsNullOrWhiteSpace(block)
                ? new List<string>()
                : block.Split('\n')
                    .Select(l => l.TrimEnd('\r'))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
        }

        void AddIfNotExists(List<string> lines, string fact)
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

        // =========================
        // CORE
        // =========================
        if (delta.core != null)
        {
            // Split existing core into lines, ignore empties
            var lines = SplitLines(npc.memory.corePersonality);

            // 1) explicit removals first
            if (delta.core.remove != null && delta.core.remove.Count > 0)
            {
                foreach (var idx in delta.core.remove.Distinct().OrderByDescending(i => i))
                {
                    if (idx >= 0 && idx < lines.Count)
                        lines.RemoveAt(idx);
                }
            }

            // 2) updates: explicit { old, new }
            if (delta.core.update != null && delta.core.update.Count > 0)
            {
                foreach (var u in delta.core.update)
                {
                    if (u == null) continue;
                    var newText = StripBullet(u.@new);
                    if (string.IsNullOrWhiteSpace(newText)) continue;

                    if (u.index >= 0 && u.index < lines.Count)
                    {
                        lines[u.index] = "- " + newText;
                    }
                    else
                    {
                        // out of range -> treat as add (safe)
                        AddIfNotExists(lines, newText);
                    }
                }
            }

            // 3) additions: only add if not already present
            if (delta.core.add != null && delta.core.add.Count > 0)
            {
                foreach (var a in delta.core.add)
                    AddIfNotExists(lines, a);
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

                var lines = SplitLines(existing);

                if (sDelta.remove != null && sDelta.remove.Count > 0)
                {
                    foreach (var idx in sDelta.remove.Distinct().OrderByDescending(i => i))
                    {
                        if (idx >= 0 && idx < lines.Count)
                            lines.RemoveAt(idx);
                    }
                }

                // update by indices
                if (sDelta.update != null && sDelta.update.Count > 0)
                {
                    foreach (var u in sDelta.update)
                    {
                        if (u == null) continue;
                        var newText = StripBullet(u.@new);
                        if (string.IsNullOrWhiteSpace(newText)) continue;

                        if (u.index >= 0 && u.index < lines.Count)
                            lines[u.index] = "- " + newText;
                        else
                            AddIfNotExists(lines, newText);
                    }
                }

                // add
                if (sDelta.add != null && sDelta.add.Count > 0)
                {
                    foreach (var a in sDelta.add)
                        AddIfNotExists(lines, a);
                }

                // cap per-person
                if (lines.Count > 18) lines = lines.Take(18).ToList();

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

            var ordered = thoughts
            .OrderByDescending(t => t.salience)
            .ThenByDescending(t => t.confidence)
            .ToList();

            Thought FindSimilar(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                var trimmed = text.Trim();
                return thoughts.FirstOrDefault(t => RoughlySameFact(t.text, trimmed));
            }

            // ADD: create new or strengthen similar
            if (delta.thoughts.add != null && delta.thoughts.add.Count > 0)
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
            if (delta.thoughts.reinforce != null && delta.thoughts.reinforce.Count > 0)
            {
                foreach (var idx in delta.thoughts.reinforce.Distinct())
                {
                    if (idx >= 0 && idx < ordered.Count)
                    {
                        var t = ordered[idx];
                        t.salience = Mathf.Clamp01(t.salience + 0.2f);
                        t.confidence = Mathf.Clamp01(t.confidence + 0.2f);
                    }
                }
            }

            // DROP: remove similar thoughts
            if (delta.thoughts.remove != null)
            {
                foreach (var idx in delta.thoughts.remove.Distinct().OrderByDescending(i => i))
                {
                    if (idx >= 0 && idx < ordered.Count)
                    {
                        var toRemove = ordered[idx];
                        thoughts.Remove(toRemove);
                        ordered.RemoveAt(idx);
                    }
                }
            }

            // keep only top N by salience to control tokens
            npc.memory.currentThoughts = thoughts
                .OrderByDescending(t => t.salience)
                .ThenByDescending(t => t.confidence)
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