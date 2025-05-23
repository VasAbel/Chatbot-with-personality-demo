using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ConversationFactory : MonoBehaviour
{
    public ConsoleChatbot chatbotManager;
    private Dictionary<string, List<NPC>> keyToNPCs = new Dictionary<string, List<NPC>>();
    private NPC npcToUser;

    public void RegisterNPC(string key, List<NPC> npcs)
    {
        if (!keyToNPCs.ContainsKey(key))
        {
            keyToNPCs[key] = new List<NPC>();
        }

        if (!keyToNPCs[key].Contains(npcs[0]))
        {
            keyToNPCs[key].Add(npcs[0]);
        }

        if (!keyToNPCs[key].Contains(npcs[1]))
        {
            keyToNPCs[key].Add(npcs[1]);
        }
        TryStartNPCConversation(key);
    }

    public void RegisterUserNPC(NPC npc, TMP_InputField dialogueBox, GameObject responseBox)
    {
        if (npcToUser != null)
        {
            Debug.Log("A user conversation is already active. Ignoring new NPC registration.");
            return;
        }

        npcToUser = npc;
        TryStartUserConversation(dialogueBox, responseBox);
    }

    /*void Update()
    {
        foreach (var entry in keyToNPCs)
        {
            if (Input.GetKeyDown(entry.Key))
            {
                TryStartNPCConversation(entry.Key);
            }
        }
    }*/

    public void StopUserConversation(NPC npc)
    {
        string convoID = $"User-{npc.getName()}";
        chatbotManager.StopSession(convoID);

        if (npcToUser == npc)
        {
            npcToUser = null;
        }
        else
        {
            Debug.LogError($"StopUserConversation called for {npc.getName()} but the registered NPC is {npcToUser}!");
        }
    }

    public bool StopNPCConversation(NPC npc1, NPC npc2)
    {
        string convoID = (npc1.idx < npc2.idx) ? 
                        $"{npc1.getName()}-{npc2.getName()}" :
                        $"{npc2.getName()}-{npc1.getName()}";

        return chatbotManager.StopSession(convoID);
    }

    private void TryStartNPCConversation(string key)
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

    private void TryStartUserConversation(TMP_InputField dialogueBox, GameObject responseBox)
    {
        if (npcToUser != null)
        {
            Debug.Log($"Starting user conversation with {npcToUser.getName()}");

            ConversationSession newSession = new UserConversationSession(npcToUser);
            chatbotManager.userInputField = dialogueBox;
            chatbotManager.messageInputField = responseBox;
            chatbotManager.StartChatSession(newSession);
        }
        else
        {
            Debug.LogWarning("No NPC assigned for user conversation with this key.");
        }
    }

    public NPC GetNpcToUser()
    {
        return npcToUser;
    }
}

