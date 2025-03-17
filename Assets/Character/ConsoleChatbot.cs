using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.IO;

public class ConsoleChatbot : MonoBehaviour
{
    private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public LlamaClient client;
    private Dictionary<string, ConversationSession> activeConversations = new Dictionary<string, ConversationSession>();
    private readonly SemaphoreSlim conversationSemaphore = new SemaphoreSlim(1, 1);

    public void StartChatSession(ConversationSession session)
    {

        if (activeConversations.ContainsKey(session.conversationID))
        {
            Debug.LogWarning($"Conversation {session.conversationID} is already active.");
            return;
        }

        activeConversations[session.conversationID] = session;
        Debug.Log($"Conversation {session.conversationID} started.");
        
        _ = RunConversation(session);
    }

    private async Task RunConversation(ConversationSession session)
    {
        string logFilePath = Path.Combine(Application.persistentDataPath, $"{session.conversationID}.txt");
        Debug.Log($"Log file saved at: {logFilePath}");

        string initialPrompt = "Start the conversation.";

        while (session.IsActive)
        {
            await conversationSemaphore.WaitAsync();
            try
            {
                session.PrepareForNextSpeaker(client);
                NPC currentSpeaker = session.GetCurrentSpeaker();
                string response = await client.SendChatMessageAsync(initialPrompt);

                if (cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                string logEntry = $"{currentSpeaker.getName()}: {response}";
                Debug.Log(logEntry);

                File.AppendAllText(logFilePath, logEntry + "\n");

                session.UpdateMessageHistory(initialPrompt);
                initialPrompt = response;
            }
            finally
            {
                conversationSemaphore.Release();
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
            session.IsActive = false;
        }

        activeConversations.Clear();
    }
}
