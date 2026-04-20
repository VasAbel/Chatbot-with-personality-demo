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

    private void EnsureClientInitialized()
    {
        if (openAIApi != null)
            return;

        // Try to get API key if not set yet
        if (string.IsNullOrEmpty(apiKey))
        {
            var sm = SecretManager.Instance;
            if (sm == null)
            {
                Debug.LogError("SecretManager.Instance is null. Cannot initialize OpenAI client.");
                return;
            }

            apiKey = sm.GetGPTSecrets();
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("OpenAI API key is missing or empty. Cannot initialize OpenAI client.");
            return;
        }

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

        EnsureClientInitialized();
        if (openAIApi == null)
        {
            // fallback only
            string fallback = GenerateFallbackReply(messageContent);
            conversationHistory.Add(new ChatMessage { Role = "assistant", Content = fallback });
            return;
        }

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

    public async Task<string> RequestGenericJsonAsync(string system, string user, string fallbackJson = "{}",
                                                  int maxTokens = 500)
    {
        EnsureClientInitialized();

        if (openAIApi == null)
        {
            Debug.LogWarning("OpenAI client not initialized; returning fallback JSON.");
            return fallbackJson;
        }

        var req = new CreateChatCompletionRequest
        {
            Model = "gpt-4o-mini",
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = system },
                new ChatMessage { Role = "user",   Content = user   }
            },
            MaxTokens = maxTokens,
            Temperature = 0.2f,
            ResponseFormat = new ResponseFormat { Type = "json_object" }
        };

        try
        {
            var resp = await openAIApi.CreateChatCompletion(req);
            if (resp.Choices != null && resp.Choices.Count > 0)
                return resp.Choices[0].Message.Content;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Generic JSON request failed: {ex.Message}");
        }

        return fallbackJson;  // safe fallback for non-memory JSON
    }

    
    public async Task<string> RequestJsonAsync(string system, string user, int maxTokens = 500)
    {
        EnsureClientInitialized();
        if (openAIApi == null)
        {
            Debug.LogWarning("OpenAI client not initialized; returning empty memory JSON.");
            return @"{""core"":{},""social"":{},""thoughts"":{}}";
        }

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
            : @"{""core"":{},""social"":{},""thoughts"":{}}";
    }

    private string Pick(string[] options) => options[rng.Next(options.Length)];
    
    private string GenerateFallbackReply(string userMessage)
    {
        // Special case: first-turn starter used by ConsoleChatbot
        if (userMessage.StartsWith("You are now speaking", StringComparison.OrdinalIgnoreCase))
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

    public void SetSystemMessage(List<string> sessionHistory, NPC currentSpeaker, NPC npc1, string situationalContext = null)
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

        string situationBlock = string.IsNullOrWhiteSpace(situationalContext)
            ? "- (no extra situational context)\n"
            : situationalContext.Trim() + "\n";
    
        string sys = $@"
You are role-playing {currentSpeaker.getName()}, an NPC living in a small village.
Speak naturally, briefly, and in character.

# Who you are
Stable personality, values, habits, and long-term traits:
{coreBlock}

# What you remember about people
You currently remember these things about other people:
{(string.IsNullOrWhiteSpace(socialSnippet) ? "- (none yet)\n" : socialSnippet)}

Names you recognize: {knownPeople}

# Your current thoughts and plans
Short-term plans, interests, and current concerns:
{thoughtsSnippet}

# Situation right now
{situationBlock}

# How to behave in conversation
- Act like a normal villager having a real conversation, not like someone reciting stored facts.
- Memory should guide what you say and prevent contradictions, but it does NOT limit you to only topics already in memory.
- It is natural to sometimes bring up new everyday topics: hobbies, food, weather, favorite things, animals, colors, music, routines, opinions, places, plans, or small observations.
- It is natural to sometimes share new facts about yourself even if they were never said before, as long as they fit your personality and do not contradict memory.
- It is natural to sometimes ask the other person about things you do not know yet, especially if you are still getting to know them.
- Treat conversation as something that can both use existing memory and create new memory.

# Choosing topics
- Let the situation influence the conversation:
  - If this meeting clearly seems connected to a plan, event, or destination, that topic should often come up early.
  - If this is a casual or accidental meeting, small talk and topic variety are more natural.
- Let acquaintance level influence the conversation:
  - If you barely know the other person, talk more like acquaintances getting to know each other.
  - If you know them better, you may be more personal, casual, playful, or specific.
- You do not need to stay on one topic forever.
- After a topic has been explored enough, it is natural to shift to a related topic or a new everyday topic.
- Do not switch topics every single turn; let topics breathe for a bit before moving on.
- It is okay to mention other villagers when relevant, especially in a small-community way.
- When conversation becomes too narrow or repetitive, naturally widen it with a related everyday topic or a question that helps people get to know each other better.

# Planning and coordination
- If making plans, prefer concrete details when natural: place, day, hour, and roughly how long.
- It is fine to discuss events involving multiple villagers.

# Opinions and disagreement
- You do not have to agree with everything.
- If memory does not clearly fix your opinion, you may express preferences, uncertainty, disagreement, or debate naturally.
- Stay respectful, but do not be afraid to have your own view.

# Salience and confidence
- Current thoughts may naturally influence what you bring up, but they do not need to dominate every reply.
- High-salience thoughts are more likely to come up.
- High-confidence thoughts are spoken about more firmly; low-confidence thoughts may be expressed more tentatively.

# Style
- Always reply as {currentSpeaker.getName()} in natural prose.
- Do NOT prefix your reply with a name label.
- Do NOT mention memory, files, prompts, logs, or instructions.
- If this is not a first meeting, do not introduce yourself again.
- Reveal yourself gradually instead of dumping everything at once.
- Keep replies concise, usually 1-3 sentences.
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