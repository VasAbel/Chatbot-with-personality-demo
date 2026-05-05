using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

public static class NpcPlanGenerator
{
    [System.Serializable]
    private class PlanResponse
    {
        public bool hasPlan;
        public string location;
        public string reason;
    }

    public static async Task TryGeneratePlan(NPC npc, string conversationSummary, GptClient client)
    {
        var registry = PlaceRegistry.Instance;
        if (registry == null) return;

        string places = string.Join(", ", registry.GetAllPlaceNames());

        string system =
            "You decide whether an NPC in a village simulation wants to go somewhere after a conversation. " +
            "Reply with a json object like: " +
            "{\"hasPlan\": true, \"location\": \"Townhouse\", \"reason\": \"wants to tell Steve about Jade\"} " +
            "or {\"hasPlan\": false, \"location\": \"\", \"reason\": \"\"}. " +
            "Only make a plan if something in the conversation genuinely motivates it. " +
            "Use only valid location IDs from the provided list.";

        string user =
            $"NPC: {npc.getName()}\n" +
            $"Available locations: {places}\n" +
            $"Recent conversation:\n{conversationSummary}\n\n" +
            $"Does {npc.getName()} want to go somewhere specific because of this conversation?";

        string raw;
        try
        {
            raw = await client.RequestGenericJsonAsync(system, user, maxTokens: 100);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Plan] LLM plan request failed for {npc.getName()}: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(raw)) return;

        PlanResponse resp;
        try
        {
            resp = JsonConvert.DeserializeObject<PlanResponse>(raw);
        }
        catch
        {
            Debug.LogWarning($"[Plan] Failed to parse plan JSON for {npc.getName()}: {raw}");
            return;
        }

        if (resp == null || !resp.hasPlan || string.IsNullOrWhiteSpace(resp.location)) return;

        // Validate the location exists
        if (!registry.GetAllPlaceNames().Contains(resp.location))
        {
            Debug.LogWarning($"[Plan] {npc.getName()} tried to plan to unknown location: {resp.location}");
            return;
        }

        npc.SetPlan(resp.location, resp.reason);
    }
}