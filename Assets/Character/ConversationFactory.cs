using System.Collections.Generic;
using UnityEngine;

public class ConversationFactory : MonoBehaviour
{
    public ConsoleChatbot chatbotManager;
    private Dictionary<KeyCode, List<NPC>> keyToNPCs = new Dictionary<KeyCode, List<NPC>>();
    private Dictionary<KeyCode, NPC> keyToUserNPC = new Dictionary<KeyCode, NPC>();

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

    public void RegisterUserNPC(KeyCode key, NPC npc)
    {
        if (!keyToUserNPC.ContainsKey(key))
        {
            keyToUserNPC[key] = npc;
        }
    }

    void Update()
    {
        foreach (var entry in keyToNPCs)
        {
            if (Input.GetKeyDown(entry.Key))
            {
                TryStartNPCConversation(entry.Key);
            }
        }

        foreach (var entry in keyToUserNPC)
        {
            if (Input.GetKeyDown(entry.Key))
            {
                TryStartUserConversation(entry.Key);
            }
        }
    }

    private void TryStartNPCConversation(KeyCode key)
    {
        if (keyToNPCs.TryGetValue(key, out List<NPC> npcs) && npcs.Count >= 2)
        {
            NPC npc1 = npcs[0];
            NPC npc2 = npcs[1];

            Debug.Log($"Starting conversation between {npc1.getName()} and {npc2.getName()}");

            ConversationSession newSession = new NPCConversationSession(npc1, npc2);
            chatbotManager.StartChatSession(newSession);
        }
        else
        {
            Debug.LogWarning("Not enough NPCs registered for this key press.");
        }
    }

    private void TryStartUserConversation(KeyCode key)
    {
        if (keyToUserNPC.TryGetValue(key, out NPC npc))
        {
            Debug.Log($"Starting user conversation with {npc.getName()}");

            ConversationSession newSession = new UserConversationSession(npc);
            chatbotManager.StartChatSession(newSession);
        }
        else
        {
            Debug.LogWarning("No NPC assigned for user conversation with this key.");
        }
    }
}

