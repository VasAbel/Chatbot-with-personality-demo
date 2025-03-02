using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.IO;

public class ConsoleChatbot : MonoBehaviour
{
    private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public LlamaClient fallbackClient;
    private Dictionary<string, ConversationSession> activeConversations = new Dictionary<string, ConversationSession>();
    private readonly SemaphoreSlim conversationSemaphore = new SemaphoreSlim(1, 1);

    public void StartChatSession(ConversationSession session)
    {

        if (activeConversations.ContainsKey(session.conversationID))
        {
            Debug.LogWarning($"Conversation {session.conversationID} is already active.");
            return;
        }

        activeConversations[session.conversationID] = session; // Store session before starting
        Debug.Log($"Conversation {session.conversationID} started.");
        
        _ = RunConversation(session); // Start conversation asynchronously
    }

    private async Task RunConversation(ConversationSession session)
    {
        string logFilePath = Path.Combine(Application.persistentDataPath, $"{session.conversationID}.txt");
        Debug.Log($"Log file saved at: {logFilePath}");

        string initialPrompt = "Ask me about my day.";

        while (session.IsActive)
        {
            await conversationSemaphore.WaitAsync(); // Ensure only one iteration modifies history at a time
            try
            {
                session.PrepareForNextSpeaker(fallbackClient); // Set the next speaker **exclusively**
                NPC currentSpeaker = session.GetCurrentSpeaker();

                string response = await fallbackClient.SendChatMessageAsync(initialPrompt);

                if (cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                string logEntry = $"{currentSpeaker.getName()}: {response}";
                Debug.Log(logEntry);

                File.AppendAllText(logFilePath, logEntry + "\n");

                session.UpdateMessageHistory(initialPrompt); // Safely update history
                initialPrompt = response;
            }
            finally
            {
                conversationSemaphore.Release(); // Allow the next iteration to modify history
            }
        }

        Debug.Log("Conversation ended.");
        File.AppendAllText(logFilePath, "Conversation ended.\n");
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
