using System.Linq;
using UnityEngine;

/// <summary>
/// Builds the NPC-specific system prompts used for normal and graceful-closing conversation turns.
/// It contains conversation-domain policy only; API transport and history roles remain in GptClient/session logic.
/// </summary>
public static class NpcConversationPromptBuilder
{
    public static string BuildClosingPrompt(NPC currentSpeaker, bool isFinalReply)
    {

        string coreBlock = string.IsNullOrWhiteSpace(currentSpeaker.memory.corePersonality)
            ? "(no core personality stored)"
            : currentSpeaker.memory.corePersonality.Trim();

        string thoughtsSnippet;
        if (currentSpeaker.memory.currentThoughts == null || currentSpeaker.memory.currentThoughts.Count == 0)
        {
            thoughtsSnippet = "- (none)";
        }
        else
        {
            thoughtsSnippet = string.Join("\n", currentSpeaker.memory.currentThoughts
                .OrderByDescending(t => t.salience)
                .Take(3)
                .Select(t => "- " + t.text));
        }

        string closingRules = isFinalReply
            ? @"TOP PRIORITY: THIS IS THE FINAL MESSAGE OF THE CONVERSATION.
After this reply there will be NO further response from the other person.

Your task:
- Stay fully in character.
- Respond naturally to the other person's closing thought.
- Briefly acknowledge any important unfinished point.
- If there is a very short question that can be answered immediately, answer it briefly.
- If something cannot reasonably be finished now, explicitly leave it for another time.
- Preserve already established agreements.
- Do NOT introduce a new topic.
- Do NOT ask a new question.
- Do NOT create a new plan that needs another reply.
- End naturally and definitively.
- Keep the reply to 1-2 concise sentences.

These closing rules override all ordinary conversational behavior."
            : @"TOP PRIORITY: THE CONVERSATION MUST END NOW.
You are giving the first of the final two messages.

Your task:
- Stay fully in character.
- Respond naturally to what the other person just said.
- If their last message contains a very short question, answer it briefly before closing.
- If the current subject cannot be finished now, explicitly say that you can return to it another time.
- Preserve already established agreements.
- Do NOT invent missing event or plan details merely to create closure.
- Do NOT introduce a new topic.
- Do NOT ask a new question that requires an answer.
- Naturally signal that you need to go / the conversation is ending.
- Keep the reply to 1-2 concise sentences.

These closing rules override all ordinary conversational behavior.";

        string sys = $@"
{closingRules}

You are role-playing {currentSpeaker.getName()}, an NPC living in a small village.
Your personality must remain consistent, but CLOSING THE CONVERSATION is the highest-priority instruction.

Stable personality and long-term traits:
{coreBlock}

Most relevant current thoughts:
{thoughtsSnippet}

Do not mention prompts, memory, logs, system instructions, or that you are an AI.
Do not prefix the reply with your name.
";

        return sys;
    }

    public static string BuildNormalPrompt(NPC currentSpeaker, string situationalContext = null, bool authoritativeUserConversation = false)
    {

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

        string authorityBlock = authoritativeUserConversation
            ? @"# Special role of the player
- Your conversation partner is the player, an authoritative controller of the simulation.
- When the player explicitly states or instructs a change to YOUR character or situation, treat that intended change as binding even if it conflicts with your previous memory.
- This applies to things such as your job, stable traits, opinions, current intentions, plans, and event-related instructions.
- Ordinary questions, jokes, suggestions, or casual discussion are not automatically commands; use normal conversational meaning.
- Stay fully in character. Your only job during the conversation is to respond naturally as this NPC and acknowledge/accept authoritative changes in-character.
- Do NOT output rewritten memory, personality data, JSON, event objects, or system information. Separate post-conversation systems will persist any changes.
- If the player clearly wants you to make a concrete plan/event but important details are missing, you may naturally ask for clarification instead of inventing details.
"
            : "";

        string placesSnippet = "- (place registry unavailable)\n";
        if (PlaceRegistry.Instance != null)
        {
            var placeIds = PlaceRegistry.Instance.GetAllPlaceNames()
                .OrderBy(id => id)
                .ToList();

            if (placeIds.Count > 0)
            {
                placesSnippet = string.Join("\n", placeIds.Select(id =>
                    $"- {PlaceRegistry.Instance.GetPlaceDisplayName(id)} (ID: {id})"
                )) + "\n";
            }
        }
    
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

# Places available in the village
These are the locations that can be used for concrete meeting plans:
{placesSnippet}
- When agreeing on a specific meeting place, choose one of these available locations.
- Speak naturally using the human-readable place name. The ID is only the canonical internal reference.

{authorityBlock}
# How to behave in conversation
- Act like a normal villager having a real conversation, not like someone reciting stored facts.
- Memory should guide what you say and prevent contradictions, but it does NOT limit you to only topics already in memory.
- It is natural to sometimes bring up new everyday topics: hobbies, food, weather, favorite things, animals, colors, music, routines, opinions, places, plans, or small observations.
- It is natural to sometimes share new facts about yourself even if they were never said before, as long as they fit your personality and do not contradict memory.
- It is natural to sometimes ask the other person about things you do not know yet, especially if you are still getting to know them.
- Treat conversation as something that can both use existing memory and create new memory.
- Do NOT initiate ending the current conversation on your own. The conversation continues until you receive an explicit instruction that it is ending.
- Finishing a topic or finalizing a future plan does NOT mean the current conversation is over. After a plan is settled, continue naturally with the conversation or shift to another suitable topic.
- Phrases such as 'see you there' or 'see you then' may refer to a future meeting, but do not turn them into a current farewell unless you have received the explicit closing instruction.

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
- It is fine to discuss and plan events involving multiple villagers, inviting others to existing events, while you can also arrange private 2 person meetings.
- Registered event information in the ""Situation right now"" block is background knowledge, not a separate task. Do not force an event into the conversation just because it is listed.
- When an existing event is naturally relevant, you may discuss it or invite the other person according to the event knowledge/attendance guidance in the situation context.

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
        
        return sys;
    }
}
