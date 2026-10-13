using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class ConversationFactory : MonoBehaviour
{
    [FormerlySerializedAs("chatbotManager")]
    public ConversationManager conversationManager;

    private readonly Dictionary<string, List<NPC>> keyToNPCs =
        new Dictionary<string, List<NPC>>();

    private NPC npcToUser;

    public void RegisterNPC(string key, List<NPC> npcs)
    {
        if (!keyToNPCs.ContainsKey(key))
            keyToNPCs[key] = new List<NPC>();

        if (!keyToNPCs[key].Contains(npcs[0]))
            keyToNPCs[key].Add(npcs[0]);

        if (!keyToNPCs[key].Contains(npcs[1]))
            keyToNPCs[key].Add(npcs[1]);

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

    public void StopUserConversation(NPC npc)
    {
        string convoID = $"User-{npc.getName()}";
        conversationManager.StopSession(convoID);

        if (npcToUser == npc)
            npcToUser = null;
        else
            Debug.LogError($"StopUserConversation called for {npc.getName()} but the registered NPC is {npcToUser}!");
    }

    public bool StopNPCConversation(NPC npc1, NPC npc2)
    {
        string convoID = npc1.idx < npc2.idx
            ? $"{npc1.getName()}-{npc2.getName()}"
            : $"{npc2.getName()}-{npc1.getName()}";

        return conversationManager.RequestNPCSessionClose(convoID);
    }

    private void TryStartNPCConversation(string key)
    {
        if (!keyToNPCs.TryGetValue(key, out List<NPC> npcs) || npcs.Count < 2)
        {
            Debug.LogWarning("Not enough NPCs registered for this conversation.");
            return;
        }

        NPC npc1 = npcs[0];
        NPC npc2 = npcs[1];

        conversationManager.StartChatSession(
            new NPCConversationSession(npc1, npc2)
        );
    }

    private void TryStartUserConversation(TMP_InputField dialogueBox, GameObject responseBox)
    {
        if (npcToUser == null)
        {
            Debug.LogWarning("No NPC assigned for user conversation.");
            return;
        }

        conversationManager.StartChatSession(
            new UserConversationSession(npcToUser, dialogueBox, responseBox)
        );
    }

    public NPC GetNpcToUser() => npcToUser;
}
