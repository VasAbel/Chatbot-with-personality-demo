using UnityEngine;

public class NPCInteractionHandler : MonoBehaviour
{
    public KeyCode inputToReact;
    public NPC npcData;
    public ConversationFactory factory;

    void Start()
    {
        if (factory == null)
        {
            factory = FindObjectOfType<ConversationFactory>();
        }

        if (npcData == null)
        {
            Debug.LogError("NPC data is not assigned!");
            return;
        }
        else
        {
            factory.RegisterNPC(inputToReact, npcData);
            Debug.Log($"{npcData.getName()} registered as NPC-NPC participant.");
        }
    }
}

