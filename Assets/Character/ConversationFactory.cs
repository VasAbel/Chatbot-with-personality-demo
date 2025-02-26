using System.Collections.Generic;
using UnityEngine;

public class ConversationFactory : MonoBehaviour
{
    public ConsoleChatbot chatbotManager;
    private Dictionary<KeyCode, List<NPC>> keyToNPCs = new Dictionary<KeyCode, List<NPC>>();

    public void RegisterNPC(KeyCode key, NPC npc)
    {
        if (!keyToNPCs.ContainsKey(key))
        {
            keyToNPCs[key] = new List<NPC>();
        }

        if (!keyToNPCs[key].Contains(npc))
        {
            keyToNPCs[key].Add(npc);
        }
    }

    void Update()
    {
        foreach (var entry in keyToNPCs)
        {
            if (Input.GetKeyDown(entry.Key))
            {
                TryStartConversation(entry.Key);
            }
        }
    }

    private void TryStartConversation(KeyCode key)
    {
        if (keyToNPCs.TryGetValue(key, out List<NPC> npcs) && npcs.Count >= 2)
        {
            NPC npc1 = npcs[0];
            NPC npc2 = npcs[1];

            Debug.Log($"Starting conversation between {npc1.getName()} and {npc2.getName()}");

            ConversationSession newSession = new ConversationSession(npc1, npc2);
            chatbotManager.StartChatSession(newSession);
        }
        else
        {
            Debug.LogWarning("Not enough NPCs registered for this key press.");
        }
    }
}

