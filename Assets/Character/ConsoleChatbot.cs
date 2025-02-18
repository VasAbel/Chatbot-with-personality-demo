using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;

public class ConsoleChatbot : MonoBehaviour
{
    public ChatClient chatClient;
    public LlamaClient fallbackClient;
    private bool isConversationActive = false;
    private List<GameObject> NPCsInConversation = new List<GameObject>();
    private int currentSpeakerIndex = 0;

    public void SetCurrentNpcGameObject(GameObject npc)
    {
        if(!NPCsInConversation.Contains(npc))
        {
            NPCsInConversation.Add(npc);
        }

        if(NPCsInConversation.Count == 2)
        {
            StartChat();
        }
        
    }

    public async void StartChat()
    {
        isConversationActive = true;
        Debug.Log("Chat started between NPCs");

        string initialPrompt = "Ask me about my day.";
        await RunConversation(initialPrompt);
    }

    public async Task RunConversation(string initialPrompt)
    {
        while(isConversationActive)
        {
            GameObject currentSpeaker = NPCsInConversation[currentSpeakerIndex];
            NPC speakerData = currentSpeaker.GetComponent<NPC>();

            string response = await GetAgentResponse(speakerData, initialPrompt);
            Debug.Log($"{speakerData.getName()}: {response}");

            currentSpeakerIndex = (currentSpeakerIndex + 1) % NPCsInConversation.Count;
            initialPrompt = response;
        }
    }

    private async Task<string> GetAgentResponse(NPC speakerData, string userMessage)
    {
        fallbackClient.SetSystemMessage(speakerData.getDesc());
        string response = await fallbackClient.SendChatMessageAsync(userMessage);
        return response;
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

    

    public async void SendInitializationToServer(string npcDescription)
    {
        Debug.Log($"Initializing conversation with NPC: {npcDescription}");

        string initMessage = "init:" + npcDescription;
        
        await fallbackClient.SendChatMessageAsync(initMessage);
        //await chatClient.SendChatMessageAsync(initMessage);
    }
}
