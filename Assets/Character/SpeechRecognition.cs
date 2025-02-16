using UnityEngine;
using System.Threading.Tasks;

public class ConsoleChatbot : MonoBehaviour
{
    public ChatClient chatClient;
    public ChatClient fallbackClient;
    private bool isConversationActive = false;
    private GameObject currentNpcGameObject;
    
    private void Update()
    {
        if (isConversationActive)
        {
            if (Input.GetKeyDown(KeyCode.Q)) SendMessageToAI("What is your name?");
            if (Input.GetKeyDown(KeyCode.W)) SendMessageToAI("What is your job?");
            if (Input.GetKeyDown(KeyCode.E)) SendMessageToAI("Do you have any advice for me?");
        }
    }

    public void StartChat()
    {
        isConversationActive = true;
        Debug.Log("Chat started. Press 1, 2, or 3 to ask a question.");
    }

    private async void SendMessageToAI(string message)
    {
        Debug.Log($"You: {message}");

        // Send to both LLMs (to maintain history)
        _ = fallbackClient.SendChatMessageAsync(message);
        
        // Try main LLM first
        string response = await TryMainLLMWithFallback(message);
        
        Debug.Log($"NPC: {response}");
    }

    private async Task<string> TryMainLLMWithFallback(string message)
    {
        Task<string> mainTask = chatClient.SendChatMessageAsync(message);

        try
        {
            return await mainTask; // If successful, return the response
        }
        catch
        {
            Debug.LogWarning("Main LLM failed. Switching to fallback.");
            return await fallbackClient.SendChatMessageAsync(message);
        }
    }

    public void SetCurrentNpcGameObject(GameObject npcGameObject)
    {
        currentNpcGameObject = npcGameObject;
    }

    public async void SendInitializationToServer(string npcDescription)
    {
        Debug.Log($"Initializing conversation with NPC: {npcDescription}");

        string initMessage = "init:" + npcDescription;
        
        // Send to both LLMs
        _ = fallbackClient.SendChatMessageAsync(initMessage);
        await chatClient.SendChatMessageAsync(initMessage);
    }
}
