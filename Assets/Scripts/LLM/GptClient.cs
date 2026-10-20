using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenAI;
using UnityEngine;

/// <summary>
/// OpenAI transport client. It owns API initialization, request execution,
/// JSON-mode requests, and translation of session history into API chat roles.
/// NPC-specific prompt construction lives in NpcConversationPromptBuilder.
/// </summary>
public class GptClient : ChatClient
{
    private OpenAIApi openAIApi;
    private readonly List<ChatMessage> conversationHistory = new List<ChatMessage>();
    private string apiKey = "";

    private void EnsureClientInitialized()
    {
        if (openAIApi != null)
            return;

        if (string.IsNullOrEmpty(apiKey))
        {
            SecretManager secretManager = SecretManager.Instance;
            if (secretManager == null)
            {
                Debug.LogError("SecretManager.Instance is null. Cannot initialize OpenAI client.");
                return;
            }

            apiKey = secretManager.GetGPTSecrets();
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("OpenAI API key is missing or empty. Cannot initialize OpenAI client.");
            return;
        }

        openAIApi = new OpenAIApi(apiKey);
    }

    public void SetConversationContext(
        string systemPrompt,
        IReadOnlyList<string> sessionHistory,
        bool evenHistoryEntriesAreUser,
        int historyStartIndex = 0)
    {
        conversationHistory.Clear();
        conversationHistory.Add(new ChatMessage
        {
            Role = "system",
            Content = systemPrompt ?? ""
        });

        if (sessionHistory == null || sessionHistory.Count == 0)
            return;

        int startIndex = Math.Max(0, historyStartIndex);

        for (int i = startIndex; i < sessionHistory.Count; i++)
        {
            bool evenEntry = i % 2 == 0;

            conversationHistory.Add(new ChatMessage
            {
                Role = (evenEntry == evenHistoryEntriesAreUser) ? "user" : "assistant",
                Content = sessionHistory[i]
            });
        }
    }

    public override async Task<string> SendChatMessageAsync(string messageContent)
    {
        conversationHistory.Add(new ChatMessage
        {
            Role = "user",
            Content = messageContent ?? ""
        });

        EnsureClientInitialized();
        if (openAIApi == null)
            throw new InvalidOperationException("OpenAI client is not initialized; NPC conversation cannot continue.");

        var request = new CreateChatCompletionRequest
        {
            Model = "gpt-4o-mini",
            Messages = conversationHistory,
            MaxTokens = 120,
            PresencePenalty = 1,
            FrequencyPenalty = 1,
        };

        try
        {
            var response = await openAIApi.CreateChatCompletion(request);

            if (response.Choices == null || response.Choices.Count == 0 ||
                string.IsNullOrWhiteSpace(response.Choices[0].Message.Content))
            {
                throw new InvalidOperationException("OpenAI returned no usable conversation response.");
            }

            string aiResponse = response.Choices[0].Message.Content;
            conversationHistory.Add(new ChatMessage
            {
                Role = "assistant",
                Content = aiResponse
            });

            return aiResponse;
        }
        catch (Exception ex)
        {
            Debug.LogError($"LLM conversation request failed: {ex.Message}");
            throw;
        }
    }

    public async Task<string> RequestJsonAsync(
        string system,
        string user,
        int maxTokens = 500,
        string fallbackJson = "{}")
    {
        EnsureClientInitialized();

        if (openAIApi == null)
        {
            Debug.LogWarning("OpenAI client not initialized; returning caller-provided fallback JSON.");
            return fallbackJson;
        }

        var request = new CreateChatCompletionRequest
        {
            Model = "gpt-4o-mini",
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = system },
                new ChatMessage { Role = "user", Content = user }
            },
            MaxTokens = maxTokens,
            Temperature = 0.2f,
            PresencePenalty = 0,
            FrequencyPenalty = 0,
            ResponseFormat = new ResponseFormat { Type = "json_object" }
        };

        try
        {
            var response = await openAIApi.CreateChatCompletion(request);
            if (response.Choices != null && response.Choices.Count > 0 &&
                !string.IsNullOrWhiteSpace(response.Choices[0].Message.Content))
            {
                return response.Choices[0].Message.Content;
            }

            Debug.LogWarning("JSON request returned no usable response; returning caller-provided fallback JSON.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"JSON request failed: {ex.Message}. Returning caller-provided fallback JSON.");
        }

        return fallbackJson;
    }
}
