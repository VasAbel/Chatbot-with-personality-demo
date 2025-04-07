using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using UnityEngine.UI;
using TMPro;

public class ConsoleChatbot : MonoBehaviour
{
    public LlamaClient client;
    private Dictionary<string, ConversationSession> activeConversations = new Dictionary<string, ConversationSession>();
    private readonly SemaphoreSlim conversationSemaphore = new SemaphoreSlim(1, 1);
    public TMP_InputField userInputField;

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
        CancellationToken token = session.CancellationTokenSource.Token;
        
        string logFilePath = Path.Combine(Application.persistentDataPath, $"{session.conversationID}.txt");
        Debug.Log($"Log file saved at: {logFilePath}");

        string initialPrompt = "Start the conversation.";

        while (session.IsActive)
        {
            bool isUserConversation = session.IsUserConversation();

            // 🟢 Only lock the semaphore if we're not waiting for user input
            if (!isUserConversation || session.GetCurrentSpeaker() != null)
            {
                await conversationSemaphore.WaitAsync();

                session.PrepareForNextSpeaker(client);
                NPC currentSpeaker = session.GetCurrentSpeaker();

                try
                {
                // NPC-NPC or NPC responding in a user conversation
                    string response = await client.SendChatMessageAsync(initialPrompt);

                    if (session.CancellationTokenSource.Token.IsCancellationRequested)
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
            else
            {

                Debug.Log("User's turn. Please type a message:");
                string userInput = await WaitForUserInput(token);
                if (string.IsNullOrEmpty(userInput)) break;

                
                await conversationSemaphore.WaitAsync();

                session.PrepareForNextSpeaker(client);

                try
                {
                        session.UpdateMessageHistory(userInput);

                        string response = await client.SendChatMessageAsync(userInput);
                        string logEntry = $"Partner: {response}";

                        Debug.Log(logEntry);
                        File.AppendAllText(logFilePath, logEntry + "\n");

                        session.UpdateMessageHistory(response);
                }
                finally
                {
                        conversationSemaphore.Release();            
                }
            }
        }

        Debug.Log("Conversation ended.");
        File.AppendAllText(logFilePath, "Conversation ended.\n");
    }



    private async Task<string> WaitForUserInput(CancellationToken token)
    {
        userInputField.gameObject.SetActive(true);
        userInputField.text = "";
        userInputField.ActivateInputField();

        TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

        void OnSubmit(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                userInputField.gameObject.SetActive(false);
                userInputField.onSubmit.RemoveListener(OnSubmit);
                tcs.SetResult(text);
            }
        }

        userInputField.onSubmit.AddListener(OnSubmit);

        await using (token.Register(() =>
        {
            userInputField.onSubmit.RemoveListener(OnSubmit);
            userInputField.gameObject.SetActive(false);
            tcs.TrySetCanceled();
        }))
        {
            try
            {
                return await tcs.Task;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
        }
    }

    public void StopSession(string conversationID)
    {
        if (activeConversations.TryGetValue(conversationID, out var session))
        {
            session.CancellationTokenSource.Cancel();
            session.IsActive = false;

            Debug.Log($"Conversation {conversationID} was stopped by user.");
            activeConversations.Remove(conversationID);
        }
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
            session.CancellationTokenSource.Cancel(); // ←✅ important!
        }

        activeConversations.Clear();

        Debug.Log("Conversations have ended.");
    }
}
