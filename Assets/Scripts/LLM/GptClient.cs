using UnityEngine;
using OpenAI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

public class GptClient : ChatClient
{
    private OpenAIApi openAIApi;
    private List<ChatMessage> conversationHistory = new List<ChatMessage>();
    private readonly System.Random rng = new System.Random();
    string apiKey = "";

    void Start()
    {
        apiKey = SecretManager.Instance.GetGPTSecrets();
        openAIApi = new OpenAIApi(apiKey);
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
            Model = "gpt-4o-mini",
            Messages = conversationHistory,
            MaxTokens = 120,
            PresencePenalty = 1,
            FrequencyPenalty = 1,
        };

        try
        {
            var response = await openAIApi.CreateChatCompletion(request);
            string aiResponse = null;
            if (response.Choices != null && response.Choices.Count > 0)
            {
                aiResponse = response.Choices[0].Message.Content;

            }
            else
            {
                Debug.LogError("Failed to get response from AI");
                //throw new ChatClientFailedException();
                aiResponse = GenerateFallbackReply(messageContent);
            }
            conversationHistory.Add(new ChatMessage { Role = "assistant", Content = aiResponse });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"LLM call failed, using fallback. Reason: {ex.Message}");
            var aiResponse = GenerateFallbackReply(messageContent);
            conversationHistory.Add(new ChatMessage { Role = "assistant", Content = aiResponse });
        }

    }
    
    public async Task<string> RequestJsonAsync(string system, string user, int maxTokens = 500)
    {
        var req = new CreateChatCompletionRequest
        {
            Model = "gpt-4o-mini",        // cheaper, very good at JSON
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = system },
                new ChatMessage { Role = "user",   Content = user   }
            },
            MaxTokens = maxTokens,
            Temperature = 0.2f,
            PresencePenalty = 0,
            FrequencyPenalty = 0,
            // IMPORTANT: force valid JSON
            ResponseFormat = new ResponseFormat
            {
                Type = "json_object"
            }
        };

        var resp = await openAIApi.CreateChatCompletion(req);
        return resp.Choices != null && resp.Choices.Count > 0
            ? resp.Choices[0].Message.Content
            : "{}";
    }

    private string Pick(string[] options) => options[rng.Next(options.Length)];
    
    private string GenerateFallbackReply(string userMessage)
    {
        // Special case: first-turn starter used by ConsoleChatbot
        if (userMessage.StartsWith("Start a conversation", StringComparison.OrdinalIgnoreCase))
        {
            var greet = Pick(new[]
            {
                "Hey there! Nice to see you around.",
                "Hi! Fancy running into you here.",
                "Hello! How’s your day going?",
                "Oh, hey! Been up to anything interesting?"
            });
            var follow = Pick(new[]
            {
                "What brings you here today?",
                "How are things on your side?",
                "Anything new happening?",
                "What are you working on?"
            });
            return $"{greet} {follow}";
        }

        // If user asked a question
        if (userMessage.Contains("?"))
        {
            var shortAnswer = Pick(new[]
            {
                "Good question—I'd say it depends.",
                "I think that makes sense.",
                "Probably, but I'd like to hear your take.",
                "Could be! What do you think?"
            });
            var bounce = Pick(new[]
            {
                "How do you see it?",
                "What’s your opinion?",
                "Curious what you’d choose.",
                "I’m open to ideas."
            });
            return $"{shortAnswer} {bounce}";
        }

        // Generic small-talk fallback
        var smallTalk = Pick(new[]
        {
            "I was just thinking about grabbing a drink from the well.",
            "Market’s busy today—lots of chatter.",
            "It’s a calm day; perfect for a short walk.",
            "Townhall looks lively; maybe there’s some meeting."
        });
        var promptBack = Pick(new[]
        {
            "How’s your day going?",
            "What are you up to?",
            "Anything interesting happening?",
            "Got any plans?"
        });
        return $"{smallTalk} {promptBack}";
    }

    public void SetSystemMessage(List<string> sessionHistory, NPC currentSpeaker, NPC npc1)
    {
        conversationHistory.Clear();

        string coreBlock = string.IsNullOrWhiteSpace(currentSpeaker.memory.corePersonality)
        ? "(no core personality stored yet)"
        : currentSpeaker.memory.corePersonality.Trim();

        string socialSnippet = "";
        if (currentSpeaker.memory.socialByNpc != null && currentSpeaker.memory.socialByNpc.Count > 0)
        {
            foreach (var kv in currentSpeaker.memory.socialByNpc)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
                    continue;

                socialSnippet += $"- {kv.Key}: {kv.Value.Trim()}\n";
            }
        }

        if (string.IsNullOrWhiteSpace(socialSnippet))
        {
            socialSnippet = "- You currently do not recall anything specific about other people.\n";
        }

        string thoughtsSnippet;
        if (currentSpeaker.memory.currentThoughts == null || currentSpeaker.memory.currentThoughts.Count == 0)
        {
            thoughtsSnippet = "- (no active short-term plans or worries)\n";
        }
        else
        {
            thoughtsSnippet = string.Join("\n", currentSpeaker.memory.currentThoughts
                .OrderByDescending(t => t.salience)
                .Select(t =>
                    $"- {t.text} [salience {Mathf.RoundToInt(t.salience * 100)}%, confidence {Mathf.RoundToInt(t.confidence * 100)}%]"
                ));
        }

        string knownPeople =
            (currentSpeaker.memory.socialByNpc != null && currentSpeaker.memory.socialByNpc.Count > 0)
            ? string.Join(", ", currentSpeaker.memory.socialByNpc.Keys)
            : "(no one yet)";

        string sys = $@"
            You are role-playing the NPC **{currentSpeaker.getName()}** in a small village.
            Speak naturally, briefly, and in character.
            Your character description:
            # Who you are (stable personality, values, long-term traits
            {coreBlock}

            # Social memory – people you already know
            You currently remember these things about the following people:
            {(string.IsNullOrWhiteSpace(socialSnippet) ? "- (none yet)\n" : socialSnippet)}

            Names you recognize: {knownPeople}

            # Current plans & thoughts (short-term, may fade or change)
            {thoughtsSnippet}

            # Style & norms
            - Always reply **as {currentSpeaker.getName()}** in natural prose.
            - Do **NOT** prefix your reply with any name or label (no ""Tim:"", ""Amy:"", etc.).
            - Never dump your whole biography; reveal small pieces across turns.
            - Before each reply, conceptually check whether your conversation partner's name
                appears in your social memory.
                    - If they **ARE** in social memory: treat them as someone you already know.
                    • Do NOT introduce yourself again.
                    • Do NOT re-explain basic facts about yourself you've already shared.
                    • You may lightly reference things you remember about them or past topics.
                    - If they are **NOT** in social memory: treat this as your first meeting and
                    briefly introduce yourself **once**.
            - Keep replies concise: usually 1–3 sentences.
            - Use core personality as your default behaviour; use social memory implicitly
            (do **not** talk about ""memory"", ""files"", or ""logs"" — just act as if you remember).
            - High-salience thoughts are more likely to come up; you may mention or act on them more naturally.
            - High-confidence thoughts: speak and plan decisively.
            - Low-confidence thoughts: express uncertainty, hesitation, or openness to changing your mind.
            - Debate respectfully. Stand by your opinion until not persuaded, but if someone gives strong arguments, 
                you may genuinely change your view and mention that change in a natural way.
            ";
        
        conversationHistory.Add(new ChatMessage { Role = "system", Content = sys});

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