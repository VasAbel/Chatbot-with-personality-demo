using UnityEngine;
using OpenAI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Lodestars;
using Assets.Game_Manager;
using System.Threading;
using System.Runtime.InteropServices;

public class LlamaClient : ChatClient
{
    private LlamaAPI llamaApi;
    private string apiKey = SecretManager.Instance.GetFallbackAPIKey();
    private List<ChatMessage> conversationHistory = new List<ChatMessage>();
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    void Start()      
    {
        llamaApi = new LlamaAPI("", null);
        StartNewConversation("");
    }

    public override async Task<string> SendChatMessageAsync(string messageContent)
    {
        await semaphore.WaitAsync();  // Ensure only one request runs at a time
        try
        {
            string first = conversationHistory.First().Content;
            string last = conversationHistory.Last().Content;
            string lastRole = conversationHistory.Last().Role;
            Debug.Log("Sending message: " + messageContent +"\n With following history: " + first + "\n and last message: " + lastRole + "  " + last);

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
             Debug.Log("Got answer with message" + messageContent);
            return "No response generated.";
        }
        finally
        {
            semaphore.Release(); // Allow the next request to proceed
        }
    }

    public override void StartNewConversation(string npcId)
    {
        // Reset conversation history for a new NPC interaction
        ResetConversation();
        InitializeCharacter(npcId);
    }

    private void InitializeCharacter(string npcId)      //Write the bot's personality and add itt to the conversation history
    {
        conversationHistory.Add(new ChatMessage { Role = "system", Content = "You are role-playing as a character with a specific background, personality, and set of objectives. You live in a small town where you often get engaged in everyday conversations with your fellow citizens. Your responses should be consistent with the given personality and goals. You mustn't share your whole background at once, in one answer. Try to get involved in longer, multi-round dialogs rather than long monologues about your whole identity. Inquire about the day and thoughts on given topics of your interlocutor, and share yours. Your personality is the following:"
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

    public void SetSystemMessage(string newDescription, List<string> sessionHistory)
    {
        conversationHistory.Clear();

        conversationHistory.Add(new ChatMessage { Role = "system", Content = newDescription });

        foreach (string message in sessionHistory)
        {
            conversationHistory.Add(new ChatMessage { Role = (conversationHistory.Count % 2 == 1) ? "user" : "assistant", Content = message });
        }

        // Swap "user" and "assistant" roles in the history
        for (int i = 1; i < conversationHistory.Count; i++)
        {
            ChatMessage modifiedMessage = conversationHistory[i];
            modifiedMessage.Role = modifiedMessage.Role == "user" ? "assistant" : "user";
            conversationHistory[i] = modifiedMessage; // Reassign the struct
        }
    }

    private void ResetConversation()
    {
        conversationHistory.Clear();
    }
}