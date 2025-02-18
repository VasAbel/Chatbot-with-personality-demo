using UnityEngine;

public class NPCInteractionHandler : MonoBehaviour
{
    public int platformID;  
    public NPC npcData;  
    public ConsoleChatbot manager;  

    void Start()
    {
        if (manager == null)
        {
            Debug.LogError("SpeechRecognizer not assigned!");
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.X)) // Start conversation when "X" is pressed
        {
            TriggerNPCInteraction();
        }
    }

    public void TriggerNPCInteraction()
    {
        if (npcData == null)
        {
            Debug.LogError("NPC data is not assigned to this platform!");
            return;
        }

        if (manager != null)
        {
            manager.SetCurrentNpcGameObject(this.gameObject);
            Debug.Log($"NPC {npcData.getName()} initialized.");
        }
    }
}   
