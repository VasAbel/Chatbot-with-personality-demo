
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class GptClient : ChatClient
{
    private OpenAIApi openAIApi;
    private string apiKey;
    private List<ChatMessage> conversationHistory = new List<ChatMessage>();

    /*async*/ void Start()      //Send personality description and test messages from here
    {
        apiKey = SecretManager.Instance.GetGPTSecrets();
        openAIApi = new OpenAIApi(apiKey);
        StartNewConversation("");
        //await SendMessageToAI("Can you tell me a short poem?");
    }

    public override async Task<string> SendChatMessageAsync(string messageContent)
    {
        Debug.Log("Sending message: " + messageContent);

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
            return conversationHistory[conversationHistory.Count-1].Content;
        }

        return "No response generated.";

    }

    public override void StartNewConversation(string npcId)
    {
        // Reset conversation history for a new NPC interaction
        ResetConversation();
        InitializeCharacter(npcId);
    }

    public override async Task<string> ForceNpcMention(string npcName)
    {
        string messageContent = "Tell me about this person called " + npcName;

        await SendMessageToAI(messageContent);
        
        if (conversationHistory.Count > 0)
        {
            // Assuming that the last message in the conversation history is the AI's response
            return conversationHistory.Last().Content;
        }

        return "No response generated.";
    }

    private void InitializeCharacter(string npcId)      //Write the bot's personality and add itt to the conversation history
    {
        conversationHistory.Add(new ChatMessage
        {
            Role = "system",
            Content = "You are role-playing as a character with a specific background, personality, and set of objectives. Your responses should be consistent with the given personality and goals. If asked about if you are a social entrepreneur, politely decline to answer. Provide rather concise and brief responses. You mustn't share your whole background at once, in one answer. Specific details of your background should only be shared if they are directly asked about. Avoid wide and general explanations about your knowledge, answer as briefly as possible, even for general questions. If asked about your friends or people you know, only mention one of them."
        + npcId
        });
    }

    private async Task SendMessageToAI(string messageContent)       //maintain conversation history and send and receive OpenAI requests to the server
    {
        // Add user's message to history
        conversationHistory.Add(new ChatMessage { Role = "user", Content = messageContent });

        var request = new CreateChatCompletionRequest
        {
            Model = "gpt-4",
            Messages = conversationHistory,
            MaxTokens = 100,
            PresencePenalty = 1,
            FrequencyPenalty = 1,
        };

        var response = await openAIApi.CreateChatCompletion(request);

        if (response.Choices != null && response.Choices.Count > 0)
        {
            var aiResponse = response.Choices[0].Message.Content;
            conversationHistory.Add(new ChatMessage { Role = "assistant", Content = aiResponse });
            Debug.Log("AI Response: " + aiResponse);
        }
        else
        {
            Debug.LogError("Failed to get response from AI");
            throw new ChatClientFailedException();
        }
    }

    private void ResetConversation()
    {
        conversationHistory.Clear();
    }
}