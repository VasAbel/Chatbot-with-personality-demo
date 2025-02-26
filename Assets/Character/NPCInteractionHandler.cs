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

        if (npcData != null)
        {
            factory.RegisterNPC(inputToReact, npcData);
        }
        else
        {
            Debug.LogError("NPC data is not assigned!");
        }
    }
}

