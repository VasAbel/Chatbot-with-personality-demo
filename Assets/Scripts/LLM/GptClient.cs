using System;
using System.Collections.Generic;
using System.Reflection;
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
    public enum ModelChoice
    {
        Gpt4oMini,
        Gpt6Luna,
        Gpt61Terra,
        Gpt61Sol
    }

    private sealed class ModelProfile
    {
        public string ModelId { get; }
        public string ReasoningEffort { get; }
        public bool UseLegacySamplingParameters { get; }

        public ModelProfile(
            string modelId,
            string reasoningEffort,
            bool useLegacySamplingParameters)
        {
            ModelId = modelId;
            ReasoningEffort = reasoningEffort;
            UseLegacySamplingParameters = useLegacySamplingParameters;
        }
    }

    [Header("Model experiment")]
    [SerializeField] private ModelChoice selectedModel = ModelChoice.Gpt4oMini;

    public string SelectedModelId => GetModelProfile().ModelId;

    private OpenAIApi openAIApi;
    private readonly List<ChatMessage> conversationHistory = new List<ChatMessage>();
    private string apiKey = "";

    private void Awake()
    {
        SimulationRunContext.Initialize(SelectedModelId);
    }

    private ModelProfile GetModelProfile()
    {
        switch (selectedModel)
        {
            case ModelChoice.Gpt6Luna:
                return new ModelProfile(
                    "gpt-6-luna",
                    "none",
                    useLegacySamplingParameters: false
                );

            case ModelChoice.Gpt61Terra:
                return new ModelProfile(
                    "gpt-6.1-terra",
                    "none",
                    useLegacySamplingParameters: false
                );

            case ModelChoice.Gpt61Sol:
                return new ModelProfile(
                    "gpt-6.1-sol",
                    "low",
                    useLegacySamplingParameters: false
                );

            default:
                return new ModelProfile(
                    "gpt-4o-mini-2024-07-18",
                    reasoningEffort: null,
                    useLegacySamplingParameters: true
                );
        }
    }

    private void ApplyModelSettings(
        CreateChatCompletionRequest request,
        int maxTokens,
        bool jsonRequest)
    {
        ModelProfile profile = GetModelProfile();
        request.Model = profile.ModelId;

        if (profile.UseLegacySamplingParameters)
        {
            request.MaxTokens = maxTokens;

            if (jsonRequest)
            {
                request.Temperature = 0.2f;
                request.PresencePenalty = 0;
                request.FrequencyPenalty = 0;
            }
            else
            {
                request.PresencePenalty = 1;
                request.FrequencyPenalty = 1;
            }

            return;
        }

        // Newer model families use max_completion_tokens rather than the older max_tokens
        // field. The installed Unity OpenAI package may or may not expose that property,
        // so set it when available and otherwise leave the API default instead of sending
        // a potentially incompatible legacy token-limit field.
        TrySetOptionalProperty(request, "MaxCompletionTokens", maxTokens);

        if (!string.IsNullOrWhiteSpace(profile.ReasoningEffort))
            TrySetOptionalProperty(request, "ReasoningEffort", profile.ReasoningEffort);
    }

    private static void TrySetOptionalProperty(
        object target,
        string propertyName,
        object value)
    {
        if (target == null)
            return;

        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public
        );

        if (property == null || !property.CanWrite)
            return;

        try
        {
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;

            object converted;
            if (targetType.IsEnum && value is string enumText)
            {
                converted = Enum.Parse(targetType, enumText, ignoreCase: true);
            }
            else
            {
                converted = Convert.ChangeType(value, targetType);
            }

            property.SetValue(target, converted);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"Could not set optional OpenAI request property '{propertyName}': {ex.Message}"
            );
        }
    }

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
            Messages = conversationHistory
        };

        ApplyModelSettings(
            request,
            maxTokens: 120,
            jsonRequest: false
        );

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
            Debug.LogError($"LLM conversation request failed on {SelectedModelId}: {ex.Message}");
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
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = system },
                new ChatMessage { Role = "user", Content = user }
            },
            ResponseFormat = new ResponseFormat { Type = "json_object" }
        };

        ApplyModelSettings(
            request,
            maxTokens,
            jsonRequest: true
        );

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
            Debug.LogWarning($"JSON request failed on {SelectedModelId}: {ex.Message}. Returning caller-provided fallback JSON.");
        }

        return fallbackJson;
    }
}
