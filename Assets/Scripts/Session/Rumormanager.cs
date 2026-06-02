using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class RumorManager : MonoBehaviour
{
    public static RumorManager Instance { get; private set; }

    private readonly Dictionary<string, Rumor> _allRumors = new Dictionary<string, Rumor>();

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

    //F5: plant predefined test rumors directly, bypassing the LLM filter.
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
            PlantDebugRumors();
    }

    private void PlantDebugRumors()
    {
        var allNpcs = FindObjectsOfType<NPC>();
        NPC tim     = allNpcs.FirstOrDefault(n => n.getName() == "Tim");
        NPC amy     = allNpcs.FirstOrDefault(n => n.getName() == "Amy");
        NPC gabriel = allNpcs.FirstOrDefault(n => n.getName() == "Gabriel");

        if (tim != null)
            PlantRumor("The visitor was seen sneaking around the old mill at night carrying a heavy sack.", tim);
        if (amy != null)
            PlantRumor("A stranger from a trade consortium is investigating a series of thefts across the region.", amy);
        if (gabriel != null)
            PlantRumor("The visitor claims something was stolen from their family years ago and the trail leads here.", gabriel);

        Debug.Log($"[Rumors] [DEBUG F5] Planted {_allRumors.Count} test rumor(s) directly into Tim, Amy, Gabriel. " +
                  "Wait for NPC-NPC meetings to observe spreading and distortion.");
    }

    //Called when the player plants a rumor by typing it to an NPC.
    public void PlantRumor(string rumorText, NPC targetNpc)
    {
        var rumor = new Rumor(rumorText, "Player");
        _allRumors[rumor.rumorId] = rumor;

        GiveRumorToNpc(targetNpc.getName(), rumor);

        Debug.Log($"[Rumors] Player planted rumor to {targetNpc.getName()}: \"{rumorText}\"");
    }

    //Uses LLM to decide if the message is actually rumor-worthy before planting anything.
    public async Task TryPlantPlayerRumor(string playerMessage, NPC targetNpc)
    {
        if (_gpt == null || string.IsNullOrWhiteSpace(playerMessage)) return;

        string playerName = Assets.Game_Manager.ConfigManager.Instance.GetPlayerName();

        string system =
            "You analyse a single message from a visitor to a villager in a small village simulation. " +
            "Decide if it contains gossip-worthy information — something a villager would naturally repeat to others.\n\n" +
            "COUNTS AS A RUMOR — reply with one plain sentence rephrasing the claim:\n" +
            "- Any claim about a specific person acting unusually, suspiciously, or surprisingly\n" +
            "- Information about an incident, problem, or ongoing situation in the village\n" +
            "- Background about the visitor that explains why they are here\n" +
            "- Claims about village property, resources, records, or land\n\n" +
            "DOES NOT COUNT — reply with exactly the word NONE:\n" +
            "- Generic greetings, compliments, or questions without specific factual claims\n" +
            "- Pure opinions with no attached factual claim\n\n" +
            "Reply with one plain sentence (no quotes, no JSON, no formatting), or exactly NONE.";

        string user =
            $"The visitor ({playerName}) said this to {targetNpc.getName()} in the village:\n" +
            $"\"{playerMessage}\"\n\n" +
            $"Would {targetNpc.getName()} find this worth repeating to other villagers? " +
            "Write the rumor sentence, or reply NONE.";

        try
        {
            string result = await _gpt.RequestPlainTextAsync(system, user, fallback: "NONE", maxTokens: 80);
            result = result.Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(result) || result.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[Rumors] Player message not rumor-worthy, skipping.");
                return;
            }

            PlantRumor(result, targetNpc);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Rumors] Player rumor check failed: {ex.Message}");
        }
    }

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

            // 50/50: distort or not
            bool distort = UnityEngine.Random.value < 0.5f;
            if (distort)
            {
                string distorted = await DistortRumor(rumor.currentText, sender.getName(), receiverName);
                if (!string.IsNullOrWhiteSpace(distorted))
                {
                    Debug.Log($"[Rumors] Distortion: \"{rumor.currentText}\" -> \"{distorted}\"");
                    textToPass = distorted;
                }
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
                        "Keep it believable and similar in length. " +
                        "Reply with ONLY the distorted rumor as a single plain sentence, nothing else.";

        string user = $"{speakerName} is passing this rumor to {listenerName}:\n\"{text}\"\n\n" +
                      "Write the slightly distorted version as a single sentence.";

        try
        {
            string result = await _gpt.RequestPlainTextAsync(system, user, fallback: text, maxTokens: 80);
            result = result.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(result)) return null;
            return result;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Rumors] Distortion LLM call failed: {ex.Message}");
            return null;
        }
    }

    public async Task TryGenerateRumorFromConversation(NPC npc, string conversationText)
    {
        if (_gpt == null) return;

        string system = "You are deciding whether an NPC in a village simulation should spread a rumor based on a conversation they just had. " +
                        "A rumor is a piece of interesting, surprising, or gossip-worthy information about another person, place, or event. " +
                        "Greetings, small talk, and opinions without factual claims are NOT rumors. " +
                        "Reply with the rumor as a single plain sentence, or exactly the word NONE if nothing is rumor-worthy.";

        string user = $"{npc.getName()} just had this conversation:\n\"{conversationText}\"\n\n" +
                      $"Is there anything rumor-worthy here? If yes, write it as a rumor {npc.getName()} would naturally spread. If no, reply NONE.";

        try
        {
            string result = await _gpt.RequestPlainTextAsync(system, user, fallback: "NONE", maxTokens: 60);
            result = result.Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(result) || result.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return;

            Debug.Log($"[Rumors] {npc.getName()} generated organic rumor: \"{result}\"");
            PlantRumor(result, npc);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Rumors] Organic rumor generation failed: {ex.Message}");
        }
    }
}
