using UnityEngine;
using OpenAI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Lodestars;

public class LlamaClient : ChatClient
{
    private LlamaAPI llamaApi;
    private List<ChatMessage> conversationHistory = new List<ChatMessage>();

    void Start()      
    {
        llamaApi = new LlamaAPI("", null);
    }

    public override async Task<string> SendChatMessageAsync(string messageContent)
    {  
        /*string first = conversationHistory.First().Content;
        string last = conversationHistory.Last().Content;
        string lastRole = conversationHistory.Last().Role;
        Debug.Log("Sending message: " + messageContent +"\n With following history: " + first + "\n and last message: " + lastRole + "  " + last);*/

        // Process the received message
        await SendMessageToAI(messageContent);

        if (conversationHistory.Count > 0)
        {
            // Assuming that the last message in the conversation history is the AI's response
            return conversationHistory[conversationHistory.Count - 1].Content;
        }
        return "No response generated.";

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
        }
        else
        {
            Debug.LogError("Failed to get response from AI");
            throw new ChatClientFailedException();
        }
    }

    public void SetSystemMessage(string newDescription, List<string> sessionHistory, NPC currentSpeaker, NPC npc1)
    {
        conversationHistory.Clear();
        conversationHistory.Add(new ChatMessage { Role = "system", Content = newDescription });

        bool isNpc1Speaking = currentSpeaker == npc1;

        for (int i = 0; i < sessionHistory.Count; i++)
        {
            bool messageFromNpc1 = i % 2 == 0;

            conversationHistory.Add(new ChatMessage
            {
                Role = (messageFromNpc1 == isNpc1Speaking) ? "user" : "assistant",
                Content = sessionHistory[i]
            });
        }
    }
}