using UnityEngine;

public class NPCInteractionHandler : MonoBehaviour
{
    public int platformID;  
    public NPC npcData;  
    public ConsoleChatbot speechRecognizer;  

    void Start()
    {
        if (speechRecognizer == null)
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

    private void TriggerNPCInteraction()
    {
        if (npcData == null)
        {
            Debug.LogError("NPC data is not assigned to this platform!");
            return;
        }

        if (speechRecognizer != null)
        {
            speechRecognizer.SetCurrentNpcGameObject(this.gameObject);
            speechRecognizer.SendInitializationToServer(npcData.getDesc());

            Debug.Log($"NPC {npcData.getName()} initialized.");
            speechRecognizer.StartChat();
        }
    }
}
