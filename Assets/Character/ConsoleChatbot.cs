using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

public class ConsoleChatbot : MonoBehaviour
{
    private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public LlamaClient fallbackClient;
    private Dictionary<string, ConversationSession> activeConversations = new Dictionary<string, ConversationSession>();

    public void StartChatSession(ConversationSession session)
    {
        string conversationID = $"{session.GetNPC1().getName()}-{session.GetNPC2().getName()}";

        if (activeConversations.ContainsKey(conversationID))
        {
            Debug.LogWarning($"Conversation {conversationID} is already active.");
            return;
        }

        activeConversations[conversationID] = session; // Store session before starting
        Debug.Log($"Conversation {conversationID} started.");
        
        _ = RunConversation(session); // Start conversation asynchronously
    }

    private async Task RunConversation(ConversationSession session)
    {
        string initialPrompt = "Ask me about my day.";

        while (session.IsActive)
        {
            session.PrepareForNextSpeaker(fallbackClient); // Ensure correct role switching

            NPC currentSpeaker = session.GetCurrentSpeaker();
            string response = await fallbackClient.SendChatMessageAsync(initialPrompt);
            
            if (cancellationTokenSource.Token.IsCancellationRequested)
                break;

            Debug.Log($"{currentSpeaker.getName()}: {response}");

            session.UpdateMessageHistory(response);
            initialPrompt = response;
        }

        Debug.Log("Conversation ended.");
    }

    private void OnApplicationQuit()
    {
        StopAllConversations();
    }

    public void StopAllConversations()
    {
        Debug.Log("Stopping all conversations...");

        foreach (var session in activeConversations.Values)
        {
            session.IsActive = false; // This should break the while loop in RunConversation()
        }

        activeConversations.Clear();
    }
}
