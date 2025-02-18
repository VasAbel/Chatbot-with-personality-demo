using UnityEngine;
using OpenAI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Lodestars;
using Assets.Game_Manager;

public class LlamaClient : ChatClient
{
    private LlamaAPI llamaApi;
    private string apiKey = SecretManager.Instance.GetFallbackAPIKey();
    private List<ChatMessage> conversationHistory = new List<ChatMessage>();
    void Start()      
    {
        llamaApi = new LlamaAPI("", null);
        StartNewConversation("");
    }

    public override async Task<string> SendChatMessageAsync(string messageContent)
    {
        //Debug.Log("Sending message: " + messageContent);

        if (messageContent.StartsWith("init:"))
        {
            string npcDescription = messageContent.Substring(5);
            StartNewConversation(npcDescription);
            return "Conversation initialized with NPC.";
        }

        // Process the received message
        await SendMessageToAI(messageContent);

        if (conversationHistory.Count > 0)
        {
            // Assuming that the last message in the conversation history is the AI's response
            return conversationHistory[conversationHistory.Count - 1].Content;
        }

        return "No response generated.";

    }
    public override void StartNewConversation(string npcId)
    {
        // Reset conversation history for a new NPC interaction
        ResetConversation();
        InitializeCharacter(npcId);
    }

    private void InitializeCharacter(string npcId)      //Write the bot's personality and add itt to the conversation history
    {
        conversationHistory.Add(new ChatMessage { Role = "system", Content = "You are role-playing as a character with a specific background, personality, and set of objectives. Your responses should be consistent with the given personality and goals. You mustn't share your whole background at once, in one answer. Try to get involved in longer, multi-round dialogs rather than long monologues about your whole identity."
         + npcId });
    }

    private async Task SendMessageToAI(string messageContent)       //maintain conversation history and send and receive OpenAI requests to the server
    {
        // Add user's message to history
        conversationHistory.Add(new ChatMessage { Role = "user", Content = messageContent });

        var request = new CreateChatCompletionRequest
        {
            Model = "casperhansen/llama-3-70b-instruct-awq",
            Messages = conversationHistory,
            MaxTokens = 100
        };

        var response = await llamaApi.CreateChatCompletion(request);

        if (response.Choices != null && response.Choices.Count > 0)
        {
            var aiResponse = response.Choices[0].Message.Content;
            conversationHistory.Add(new ChatMessage { Role = "assistant", Content = aiResponse });
            //Debug.Log("AI Response: " + aiResponse);
        }
        else
        {
            Debug.LogError("Failed to get response from AI");
            throw new ChatClientFailedException();
        }
    }

    public void SetSystemMessage(string newDescription)
    {
        if (conversationHistory.Count > 0 && conversationHistory[0].Role == "system")
        {
            // Create a copy, modify it, and assign it back
            ChatMessage updatedMessage = conversationHistory[0];
            updatedMessage.Content = newDescription;
            conversationHistory[0] = updatedMessage;
        }
        else
        {
            conversationHistory.Insert(0, new ChatMessage { Role = "system", Content = newDescription });
        }

        // Swap "user" and "assistant" roles in the history
        for (int i = 1; i < conversationHistory.Count; i++)
        {
            ChatMessage modifiedMessage = conversationHistory[i];
            modifiedMessage.Role = modifiedMessage.Role == "user" ? "assistant" : "user";
            conversationHistory[i] = modifiedMessage; // Reassign the struct
        }

        // Remove the last message to prevent duplication
        if (conversationHistory.Count > 1)
        {
            conversationHistory.RemoveAt(conversationHistory.Count - 1);
        }
    }

    private void ResetConversation()
    {
        conversationHistory.Clear();
    }
}