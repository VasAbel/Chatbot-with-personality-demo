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
using Unity.VisualScripting;
using Newtonsoft.Json;

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
                    createdUnix = t.createdUnix,
                    gameTimestamp = t.gameTimestamp
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
    ""update"": [{ ""index"": 0, ""new"": ""..."" }],
    ""remove"":      [0, 1]
  }
}

SECTION DEFINITIONS:

- core (weeks/months/years/permanent): stable, long-term facts about THIS NPC (job, values, deep preferences, recurring habits, looks).
  Things that are true even months or years later.
  Never put information about other people into core.
  Never put temporary plans, current projects or transient thoughts here.

- social:
  What THIS NPC believes about OTHER NPCs, their traits, habits, preferences, roles, and changes in their life.
  Keys in ""social"" must be other NPC names only (never the self name).

- thoughts:
  TYPICAL WORDS: today/this week/currently/trying/planning/worried/excited
  Short-term or **evolving ideas/plans** of THIS NPC: current projects, considering/planning/might/soon
  Volatile thoughts that can appear, change, or disappear quickly.
  VERY IMPORTANT: Planning activities with others is a typical element of thought section. Always note down specific locations or timeslots (days, hours, dates) IF IT WAS DISCUSSED IN THE CONVERSATION, so the NPC memorizes what they agreed to. 

CLASSIFY WITH THESE EXAMPLES:
- core (about SELF): ""Teaches history."" ""Values craftsmanship."" ""Often hikes on weekends."" ""Believes healthy food is important to be happy.""
- thoughts: ""Currently building a table."" ""Considering collaborating with Gabriel soon."" ""Thinking about hosting a party."" ""Planning to visit her brother.""
- social (about OTHERS): ""Gabriel teaches history and loves storytelling."" ""John is currently planning to throw a party.""

OPERATIONS:
-add: Put the sentence here if it is a COMPLETELY NEW INFORMATION that IS NOT PART OF THE MEMORY YET IN ANY FORM. If there already is a differently phrased sentence with the SAME MEANING, DO NOT add the new one. If there already is a SIMILAR sentence with LESS INFORMATION, use UPDATE instead of add.
Example: ""core"": {
    ""add"":    [""Her favourite animals are horses""]
    ...

-update: Put the index of the old sentence and the new sentence here if there is an old one WITH A SIMILAR MEANING BUT LESS/CONTRADICTED INFORMATION. If a sentence has no relevance anymore, DO NOT UPDATE IT, REMOVE INSTEAD.
        IMPORTANT: When updating an item, do not lose any context from the previous version, because this is the *single source of truth* for the NPC. Example: ""XY is making a chair for a restaurant"" -> ""XY has almost finished the chair, *that he was making for a restaurant*"". Without the last part, the LLM reading the memory wouldn't understand what chair the memory talks about, since that information would have been lost after udate.
Example: ""core"": {
    ...
    ""update"": [{ ""index"": 0, ""new"": ""Her favourite animals are black horses"" }] --> eg. original sentence on idx 0 was ""Her favourite animals are horses""

-remove: Put the index of the sentence here if it became CLEARLY CONTRADICTED, ABANDONED OR OUTDATED AND NOT UPDATED. If you see something that was CLEARLY PLACED IN THE WRONG SECTION, put it in remove.
    CONTRADICTED: The NPC clearly states the opposite of an information or states changing their mind about it. (SELF about core and thoughts or OTHER NPC about something in Social) (Example: ""XY loves dogs"" -> Conversation: ""XY: I don't like dogs anymore"")
    ABANDONED: An information about a process that is now finished and not worth to remember (Example: ""XY is making dinner"" -> Conversation: ""I am done with making dinner)
    OUTDATED: An information that was once new and worth to memorize but is not interesting in the long term. (Example: ""XY has finished the job he was working on for a long time"" -> The topic is now outdated and not contributing to the personality of the NPC anymore)
    Watch out for these 3 types of information carefully, decide with a brain of a human (what are things that are not building personality and were just remembered as a temporary information once but are not important in the long run)
Example: ""social"": {
    ""XY"": {
      ...
      ""remove"": [0]

CONSISTENCY (CRITICAL):
Before adding a new sentence, always double check if there is an existing sentence with the same topic. Even if the sentence is not exactly the same, has less or additional information, but HAS THE SAME BASE STATEMENT, PREFER UPDATING it instead of adding a new sentence with overlapping parts.
If you see DUPLICATES in meaning or VERY SIMILAR TOPICS among existing sentences, resolve them:

- If two sentences express the SAME INFORMATION → REMOVE one of them.
- If one sentence contains ALL the information of another plus MORE → REMOVE the less informative one.
- If two sentences contain PARTIAL information that complements each other → MERGE them:
    - UPDATE one sentence with the combined information.
    - REMOVE the other sentence.

Always operate using the correct indices from PREVIOUS MEMORY. Prefer keeping the sentence that is clearer or more specific

*Never place the same index in both remove and update.*

INDEXING (CRITICAL):
- PREVIOUS CORE / SOCIAL / THOUGHTS will be given as indexed lists (0..N-1).

GENERAL RULES:
- Keep strings concise (< 120 chars).
- Avoid near-duplicates; do not restate the same idea with slightly different wording.
- ""core"", ""social"", and ""thoughts"" must always be JSON OBJECTS, not arrays.
- If there are no changes for a section, omit that section completely
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
    .Select((t,i) =>
    $"{i}: [{(string.IsNullOrWhiteSpace(t.gameTimestamp) ? "unknown time" : t.gameTimestamp)}] {t.text.Trim()} " +
    $"(sal {Mathf.RoundToInt(t.salience*100)}%, conf {Mathf.RoundToInt(t.confidence*100)}%)")))}

CURRENT Conversation (latest session, including speaker names):
{fullConversation}

Task:
- Decide what to add, update, or remove in core, social, and thoughts.
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

    Start with a short, natural greeting.
    Treat them as someone you already know, you find information about them in your memory under the tag [{partner.getName()}]. Do NOT introduce yourself again.

    Based on past conversations, you believe {partner.getName()} already knows these things about you:
    {(string.IsNullOrWhiteSpace(partnerKnowsAboutMe) ? "- (nothing specific yet)" : partnerKnowsAboutMe)}

    Important:
    - Avoid re-explaining the above unless correcting or meaningfully expanding it.
    - Do not mention 'memory' or these instructions.

    For your first message, choose a natural opening that fits the situation:
    - You may refer to why this meeting seems to be happening.
    - Or you may open with ordinary small talk, a question, an observation, or a light personal topic.
    - You do not need to repeat known facts unless you are updating, correcting, or expanding them.

    Now say your first message.";
    }

    private static string BuildFirstMeetingPrompt(NPC partner)
    {
            return $@"
        You are now speaking to someone you have never met before.

        Important:
        - You do NOT know their name yet.
        - Treat this as a first meeting.

        Start with a short natural greeting and briefly introduce yourself once, including your name.
        After that, speak like someone open to getting to know the other person:
        - You may ask a simple everyday question,
        - make a light observation,
        - or bring up a small, natural topic.

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

    public async Task CleanNpcMemoryAsync(NPC npc)
    {
        if (npc == null)
        {
            Debug.LogWarning("[MEMORY-CLEAN] NPC is null.");
            return;
        }

        if (client == null)
        {
            Debug.LogWarning($"[MEMORY-CLEAN] No client available for {npc.getName()}.");
            return;
        }

        await conversationSemaphore.WaitAsync();
        try
        {
            var before = CloneMemory(npc.memory);

            string system = @"
    You are a memory cleaner for a role-playing village simulation.
    Reply with VALID JSON ONLY. No markdown. No commentary.

    Return the FULL cleaned memory using exactly this schema:

    {
    ""core"": [""...""],
    ""social"": {
        ""NPC_NAME"": [""..."", ""...""]
    },
    ""thoughts"": [
        {
        ""text"": ""..."",
        ""gameTimestamp"": ""yyyy-MM-dd HH:mm dddd"",
        ""confidence"": 0.65,
        ""salience"": 0.55
        }
    ]
    }

    GOALS, IN THIS ORDER:

    1. SECTION CONSISTENCY
    - core = stable long-term facts about this NPC only.
    - social = what this NPC believes about other people.
    - thoughts = temporary / current / recent plans, worries, projects, intentions, short-term interests.
    - Move items to the correct section if needed.
    - If a sentence mixes stable and temporary information, split or rewrite it into cleaner atomic items.
    - Any sentence containing concrete future plans, current projects, specific upcoming events, or explicit time references belongs in thoughts, not core.

    2. REDUNDANCY
    - Remove exact duplicates.
    - If one item says everything another item says and more, keep the richer one.
    - If two related items contain complementary information, merge them into one clearer item.

    3. IMPORTANCE FILTERING
    - Remove low-value trivia that a human-like villager would probably not keep as useful memory.
    - Keep information that matters for personality, relationships, preferences, plans, social behavior, or future scheduling.

    4. CLEAN COMPRESSION
    - Prefer fewer, clearer, more informative memory items.
    - Preserve useful information even when compressing.
    - Do not remove important nuance just to shorten the memory.

    5. CHRONOLOGY SANITY
    - thoughts should remain temporary and current.
    - stable long-term traits should be in core, not thoughts.
    - social should only contain information about other people.

    TIMESTAMP RULES (CRITICAL):
    - Every thought must have a valid gameTimestamp in the exact format: yyyy-MM-dd HH:mm dddd
    - Every social item should begin with a timestamp prefix in the exact format: [yyyy-MM-dd HH:mm dddd]
    - Keep the original timestamp when the cleaned item still represents the same underlying memory.
    - If multiple thought or social items are merged, use the latest timestamp among the merged source items.
    - Do not invent placeholder timestamps; always reuse the matching timestamp from the original memory when possible.
    - Preserve timestamps for all thought and social items matching their related original memory item.
    - Only omit a timestamp if the original matching source item truly had no timestamp.

    IMPORTANT RULES:
    - Never invent major new facts.
    - Prefer keeping original wording unless merging or correcting is necessary.
    - Do not rewrite sentences just for style; only rewrite if it improves structure or removes redundancy.
    - Be conservative: preserve meaning unless removing redundancy or low-value clutter.
    - Do not put the NPC's own facts into social.
    - Do not put facts about other NPCs into core.
    - Keep the result natural and easy for another LLM to understand later.

    FINAL CHECK:
    - Ensure no information appears in more than one section.
    - Ensure each item is in the correct section.
    - Ensure no duplicates or near-duplicates remain.
    - Ensure every thought has a valid timestamp.
    ";

            string user = BuildCleanerUserPrompt(npc);

            string rawJson;
            if (client is GptClient gpt)
            {
                rawJson = await gpt.RequestGenericJsonAsync(system, user, fallbackJson: @"{""core"":[],""social"":{},""thoughts"":[]}", maxTokens: 1200);
            }
            else
            {
                rawJson = await client.SendChatMessageAsync(system + "\n\n" + user);
            }

            rawJson = SanitizeJson(rawJson);

            CleanedMemoryDto cleaned = null;
            try
            {
                cleaned = JsonConvert.DeserializeObject<CleanedMemoryDto>(rawJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MEMORY-CLEAN] Failed to parse cleaner JSON for {npc.getName()}: {ex.Message}\nRaw: {rawJson}");
                return;
            }

            if (cleaned?.thoughts != null && cleaned.thoughts.Any(t =>
                t == null ||
                string.IsNullOrWhiteSpace(t.text) ||
                !IsValidGameTimestampExact(t.gameTimestamp)))
            {
                Debug.LogWarning($"[MEMORY-CLEAN] Invalid thought timestamp in cleaner output for {npc.getName()}, skipping apply.\nRaw: {rawJson}");
                return;
            }

            ApplyCleanedMemory(npc, cleaned);
            LogMemoryClean(npc.getName(), before, npc, rawJson);
            npc.LogMemoryToFile();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MEMORY-CLEAN] Cleaner failed for {npc.getName()}: {ex.Message}");
        }
        finally
        {
            conversationSemaphore.Release();
        }
    }

    private static string NormalizeMemoryLine(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return StripBulletAndTimestamp(s).Trim();
    }

    private static string BuildIndexedCoreBlock(NPC npc)
    {
        var lines = (npc.memory.corePersonality ?? "")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));

        return ToIndexedLines(lines);
    }

    private static string BuildIndexedSocialBlock(NPC npc)
    {
        if (npc.memory.socialByNpc == null || npc.memory.socialByNpc.Count == 0)
            return "(none)";

        var blocks = new List<string>();

        foreach (var kv in npc.memory.socialByNpc.OrderBy(k => k.Key))
        {
            var lines = (kv.Value ?? "")
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l));

            string indexed = ToIndexedLines(lines);
            if (string.IsNullOrWhiteSpace(indexed))
                continue;

            blocks.Add($"[{kv.Key}]\n{indexed}");
        }

        return blocks.Count == 0 ? "(none)" : string.Join("\n\n", blocks);
    }

    private static string BuildIndexedThoughtsBlock(NPC npc)
    {
        if (npc.memory.currentThoughts == null || npc.memory.currentThoughts.Count == 0)
            return "(none)";

        return string.Join("\n", npc.memory.currentThoughts
            .OrderByDescending(t => t.salience)
            .ThenByDescending(t => t.confidence)
            .Select((t, i) =>
                $"{i}: [{(string.IsNullOrWhiteSpace(t.gameTimestamp) ? "unknown time" : t.gameTimestamp)}] {t.text.Trim()} " +
                $"(sal {Mathf.RoundToInt(t.salience * 100)}%, conf {Mathf.RoundToInt(t.confidence * 100)}%)"));
    }

    private static string BuildCleanerUserPrompt(NPC npc)
    {
        return $@"
    Clean the memory of this NPC.

    NPC name: {npc.getName()}

    CURRENT CORE (indexed):
    {BuildIndexedCoreBlock(npc)}

    CURRENT SOCIAL (indexed per NPC):
    {BuildIndexedSocialBlock(npc)}

    CURRENT THOUGHTS (indexed):
    {BuildIndexedThoughtsBlock(npc)}
    IMPORTANT:
    Items earlier in the list are more important (higher salience).
    Preserve or prioritize them when merging or removing.

    Return the fully cleaned memory as JSON using the exact schema described in the system prompt.
    Return only JSON.";
    }

    private void ApplyCleanedMemory(NPC npc, CleanedMemoryDto cleaned)
    {
        if (cleaned == null)
        {
            Debug.LogWarning($"[MEMORY-CLEAN] {npc.getName()} cleaner returned null DTO, skipping apply.");
            return;
        }

        // ---------- CORE ----------
        var coreLines = (cleaned.core ?? new List<string>())
            .Select(NormalizeMemoryLine)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        npc.memory.corePersonality = string.Join("\n", coreLines.Select(s => "- " + s));

        // ---------- SOCIAL ----------
        var newSocial = new Dictionary<string, string>();

        if (cleaned.social != null)
        {
            foreach (var kv in cleaned.social)
            {
                string otherName = kv.Key?.Trim();
                if (string.IsNullOrWhiteSpace(otherName))
                    continue;

                if (string.Equals(otherName, npc.getName(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var lines = (kv.Value ?? new List<string>())
                    .Select(s => s?.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(18)
                    .Select(s => "- " + s)
                    .ToList();

                if (lines.Count > 0)
                    newSocial[otherName] = string.Join("\n", lines);
            }
        }

        npc.memory.socialByNpc = newSocial;

        // ---------- THOUGHTS ----------
        var newThoughts = new List<Thought>();

        if (cleaned.thoughts != null)
        {
            foreach (var t in cleaned.thoughts)
            {
                if (t == null) continue;

                string text = NormalizeMemoryLine(t.text);
                if (string.IsNullOrWhiteSpace(text)) continue;

                newThoughts.Add(new Thought
                {
                    text = text,
                    gameTimestamp = t.gameTimestamp.Trim(),
                    salience = Mathf.Clamp01(t.salience <= 0 ? 0.55f : t.salience),
                    confidence = Mathf.Clamp01(t.confidence <= 0 ? 0.65f : t.confidence),
                    createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }
        }

        npc.memory.currentThoughts = newThoughts
            .OrderByDescending(t => t.salience)
            .ThenByDescending(t => t.confidence)
            .Take(7)
            .ToList();
    }

    private static bool IsValidGameTimestampExact(string ts)
    {
        if (string.IsNullOrWhiteSpace(ts))
            return false;

        return DateTime.TryParseExact(
            ts.Trim(),
            "yyyy-MM-dd HH:mm dddd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _
        );
    }

    private void LogMemoryClean(string npcName, NpcMemory beforeMem, NPC afterNpc, string rawJson)
    {
        string beforeThoughts = (beforeMem.currentThoughts == null) ? "" :
            string.Join("\n", beforeMem.currentThoughts
                .OrderByDescending(t => t.salience)
                .ThenByDescending(t => t.confidence)
                .Select((t, i) => $"{i}: - {t.text} (sal {t.salience:0.00}, conf {t.confidence:0.00})"));

        string afterThoughts = (afterNpc.memory.currentThoughts == null) ? "" :
            string.Join("\n", afterNpc.memory.currentThoughts
                .OrderByDescending(t => t.salience)
                .ThenByDescending(t => t.confidence)
                .Select((t, i) => $"{i}: - {t.text} (sal {t.salience:0.00}, conf {t.confidence:0.00})"));

        string block =
    $@"[MEMORY-CLEAN] npc={npcName} time={DateTime.Now}

    RAW CLEAN JSON:
    {SafeShort(rawJson, 15000)}

    BEFORE CORE:
    {SafeShort(beforeMem.corePersonality ?? "(none)", 6000)}

    AFTER CORE:
    {SafeShort(afterNpc.memory.corePersonality ?? "(none)", 6000)}

    BEFORE SOCIAL:
    {SafeShort(string.Join("\n\n", (beforeMem.socialByNpc ?? new Dictionary<string, string>())
        .Select(kv => $"[{kv.Key}]\n{kv.Value}")), 6000)}

    AFTER SOCIAL:
    {SafeShort(string.Join("\n\n", (afterNpc.memory.socialByNpc ?? new Dictionary<string, string>())
        .Select(kv => $"[{kv.Key}]\n{kv.Value}")), 6000)}

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

    [Serializable]
    private class CleanedThoughtDto
    {
        public string text;
        public string gameTimestamp;
        public float confidence;
        public float salience;
    }

    [Serializable]
    private class CleanedMemoryDto
    {
        public List<string> core;
        public Dictionary<string, List<string>> social;
        public List<CleanedThoughtDto> thoughts;
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
            public List<UpdateByIndex> update;
            public List<int> remove;
        }
    }

    private static string StripTimestampPrefix(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        s = s.Trim();

        // matches prefixes like:
        // [2026-01-01 14:00 Thursday] something
        if (s.StartsWith("["))
        {
            int close = s.IndexOf(']');
            if (close > 0 && close + 1 < s.Length)
                return s.Substring(close + 1).Trim();
        }

        return s;
    }

    private string BuildMemoryStamp(NPC npc)
    {
        return $"[{npc.GetMemoryTimestampTag()}]";
    }

    private string StampMemoryLine(NPC npc, string text)
    {
        string clean = StripBulletAndTimestamp(text);
        return $"{BuildMemoryStamp(npc)} {clean}";
    }

    private static string StripBulletAndTimestamp(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        s = s.Trim().TrimStart('-', ' ').Trim();
        s = StripTimestampPrefix(s);
        return s.Trim();
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
            var target = StripBulletAndTimestamp(fact);

            bool exists = lines.Any(l =>
            {
                var raw = StripBulletAndTimestamp(l);
                return raw.Equals(target, StringComparison.OrdinalIgnoreCase)
                    || RoughlySameFact(raw, target);
            });

            if (!exists)
                lines.Add("- " + StampMemoryLine(npc, target));
        }

        static string GetExistingLineTimestamp(string line)
        {
            return ExtractTimestampPrefix(line);
        }

        // =========================
        // CORE
        // =========================
        if (delta.core != null)
        {
            var lines = SplitLines(npc.memory.corePersonality);

            if (delta.core.remove != null && delta.core.remove.Count > 0)
            {
                foreach (var idx in delta.core.remove.Distinct().OrderByDescending(i => i))
                {
                    if (idx >= 0 && idx < lines.Count)
                        lines.RemoveAt(idx);
                }
            }

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
                        AddIfNotExists(lines, newText);
                    }
                }
            }

            if (delta.core.add != null && delta.core.add.Count > 0)
            {
                foreach (var a in delta.core.add)
                    AddIfNotExists(lines, a);
            }

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

                if (sDelta.update != null && sDelta.update.Count > 0)
                {
                    foreach (var u in sDelta.update)
                    {
                        if (u == null) continue;
                        var newText = StripBulletAndTimestamp(u.@new);
                        if (string.IsNullOrWhiteSpace(newText)) continue;

                        if (u.index >= 0 && u.index < lines.Count)
                        {
                            string oldTs = GetExistingLineTimestamp(lines[u.index]);
                            if (!string.IsNullOrWhiteSpace(oldTs))
                                lines[u.index] = $"- [{oldTs}] {newText}";
                            else
                                lines[u.index] = "- " + StampMemoryLine(npc, newText);
                        }
                        else
                        {
                            AddIfNotExists(lines, newText);
                        }
                    }
                }

                if (sDelta.add != null && sDelta.add.Count > 0)
                {
                    foreach (var a in sDelta.add)
                        AddIfNotExists(lines, a);
                }

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
            npc.DecayThoughts(1.0f);

            if (npc.memory.currentThoughts == null)
                npc.memory.currentThoughts = new List<Thought>();

            var thoughts = npc.memory.currentThoughts;

            // IMPORTANT: indices must match the sorted order shown in promptFor()
            var ordered = thoughts
                .OrderByDescending(t => t.salience)
                .ThenByDescending(t => t.confidence)
                .ToList();

            Thought FindSimilar(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                var trimmed = StripBulletAndTimestamp(text);
                return thoughts.FirstOrDefault(t => RoughlySameFact(t.text, trimmed));
            }

            if (delta.thoughts.remove != null && delta.thoughts.remove.Count > 0)
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

            if (delta.thoughts.update != null && delta.thoughts.update.Count > 0)
            {
                foreach (var u in delta.thoughts.update)
                {
                    if (u == null) continue;

                    string newText = StripBulletAndTimestamp(u.@new);
                    if (string.IsNullOrWhiteSpace(newText)) continue;

                    if (u.index >= 0 && u.index < ordered.Count)
                    {
                        var target = ordered[u.index];
                        target.text = newText;
                        target.salience = Mathf.Clamp01(target.salience + 0.15f);
                        target.confidence = Mathf.Clamp01(target.confidence + 0.10f);
                        // keep original timestamp for same thought
                    }
                    else
                    {
                        var existing = FindSimilar(newText);
                        if (existing != null)
                        {
                            existing.text = newText;
                            existing.salience = Mathf.Clamp01(existing.salience + 0.15f);
                            existing.confidence = Mathf.Clamp01(existing.confidence + 0.10f);
                            // keep existing timestamp here too
                        }
                        else
                        {
                            thoughts.Add(new Thought
                            {
                                text = newText,
                                confidence = 0.6f,
                                salience = 0.6f,
                                createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                gameTimestamp = npc.GetMemoryTimestampTag()
                            });
                        }
                    }
                }
            }

            if (delta.thoughts.add != null && delta.thoughts.add.Count > 0)
            {
                foreach (var s in delta.thoughts.add)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var trimmed = StripBulletAndTimestamp(s);

                    var existing = FindSimilar(trimmed);
                    if (existing == null)
                    {
                        thoughts.Add(new Thought
                        {
                            text = trimmed,
                            confidence = 0.6f,
                            salience = 0.6f,
                            createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            gameTimestamp = npc.GetMemoryTimestampTag()
                        });
                    }
                }
            }

            npc.memory.currentThoughts = thoughts
                .OrderByDescending(t => t.salience)
                .ThenByDescending(t => t.confidence)
                .Take(7)
                .ToList();
        }

        Debug.Log($"[Memory Timestamp] {npc.getName()} updated memory at {npc.GetMemoryTimestampTag()}");
    }

    private static string ExtractTimestampPrefix(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        s = s.Trim();

        // Expected format at the start:
        // [2026-01-02 11:00 Friday] Some memory text
        if (!s.StartsWith("["))
            return null;

        int close = s.IndexOf(']');
        if (close <= 1)
            return null;

        return s.Substring(1, close - 1).Trim();
    }


    private static bool RoughlySameFact(string oldLine, string newLine)
    {
        if (string.IsNullOrWhiteSpace(oldLine) || string.IsNullOrWhiteSpace(newLine))
            return false;

        // Normalize: remove bullet, trim, strip trailing punctuation like . ! ? ; :
        string Normalize(string s)
        {
            s = StripTimestampPrefix(s);
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