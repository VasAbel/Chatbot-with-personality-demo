using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class RumorManager : MonoBehaviour
{
    public static RumorManager Instance { get; private set; }

    private readonly Dictionary<string, Rumor> _allRumors = new Dictionary<string, Rumor>();

    // Per-NPC rumor storage: npcName -> list of rumors that NPC knows
    private readonly Dictionary<string, List<Rumor>> _npcRumors = new Dictionary<string, List<Rumor>>();

    private GptClient _gpt;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _gpt = FindObjectOfType<GptClient>();
    }

    // Called when the player plants a rumor by typing it to an NPC.
    public void PlantRumor(string rumorText, NPC targetNpc)
    {
        var rumor = new Rumor(rumorText, "Player");
        _allRumors[rumor.rumorId] = rumor;

        GiveRumorToNpc(targetNpc.getName(), rumor);

        Debug.Log($"[Rumors] Player planted rumor to {targetNpc.getName()}: \"{rumorText}\"");
    }

    // Called at the end of an NPC-NPC conversation.
    // Each NPC passes any rumors the other hasn't heard yet.
    public async Task ExchangeRumors(NPC npc1, NPC npc2)
    {
        await PassRumors(npc1, npc2);
        await PassRumors(npc2, npc1);
    }

    public List<Rumor> GetRumorsKnownBy(string npcName)
    {
        if (_npcRumors.TryGetValue(npcName, out var list))
            return list;
        return new List<Rumor>();
    }

    public string GetSpreadReport()
    {
        if (_allRumors.Count == 0) return "No rumors in circulation.";

        var lines = new List<string>();
        foreach (var r in _allRumors.Values)
        {
            lines.Add($"[{r.rumorId}] Original: \"{r.originalText}\"");
            lines.Add($"  Chain: {string.Join(" -> ", r.spreadChain)}");

            // Show current text at each NPC if it differs from original
            foreach (var name in r.spreadChain.Skip(1)) // skip Player
            {
                var known = GetRumorsKnownBy(name).FirstOrDefault(x => x.rumorId == r.rumorId);
                if (known != null && known.currentText != r.originalText)
                    lines.Add($"  {name} heard: \"{known.currentText}\"");
            }
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    private async Task PassRumors(NPC sender, NPC receiver)
    {
        var senderRumors = GetRumorsKnownBy(sender.getName());
        var receiverName = receiver.getName();

        foreach (var rumor in senderRumors)
        {
            if (rumor.HasReached(receiverName)) continue;

            string textToPass = rumor.currentText;

            // 50/50: distort or pass verbatim
            bool distort = UnityEngine.Random.value < 0.5f;
            if (distort)
            {
                string distorted = await DistortRumor(rumor.currentText, sender.getName(), receiverName);
                if (!string.IsNullOrWhiteSpace(distorted))
                    textToPass = distorted;
            }

            var passed = rumor.PassTo(receiverName, textToPass);
            GiveRumorToNpc(receiverName, passed);

            Debug.Log($"[Rumors] {sender.getName()} -> {receiverName}: \"{textToPass}\"" +
                      (distort ? " (distorted)" : " (verbatim)"));
        }
    }

    private void GiveRumorToNpc(string npcName, Rumor rumor)
    {
        if (!_npcRumors.ContainsKey(npcName))
            _npcRumors[npcName] = new List<Rumor>();

        var existing = _npcRumors[npcName].FindIndex(r => r.rumorId == rumor.rumorId);
        if (existing >= 0)
            _npcRumors[npcName][existing] = rumor;
        else
            _npcRumors[npcName].Add(rumor);
    }

    private async Task<string> DistortRumor(string text, string speakerName, string listenerName)
    {
        if (_gpt == null) return null;

        string system = "You are simulating how rumors distort as they spread in a small village. " +
                        "Given a rumor, produce a slightly changed version: change one small detail, " +
                        "exaggerate slightly, or misremember a name or place. " +
                        "Keep it believable. Reply with ONLY the distorted rumor sentence, no json, no formatting, nothing else.";

        string user = $"{speakerName} is passing this rumor to {listenerName}:\n\"{text}\"\n\n" +
                      "Write the slightly distorted version as a single sentence.";

        try
        {
            string result = await _gpt.RequestGenericJsonAsync(
                system, user,
                fallbackJson: text,  // fallback = original if LLM fails
                maxTokens: 80
            );

            // RequestGenericJsonAsync expects JSON but we're abusing it for plain text here.
            // If the result starts with { it's probably a JSON error — return null to use verbatim.
            if (result.TrimStart().StartsWith("{")) return null;
            return result.Trim().Trim('"');
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Rumors] Distortion LLM call failed: {ex.Message}");
            return null;
        }
    }
}