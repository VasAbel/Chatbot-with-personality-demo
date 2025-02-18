using UnityEngine;
using OpenAI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

public abstract class ChatClient : MonoBehaviour
{
    public abstract Task<string> SendChatMessageAsync(string messageContent);
    public abstract void StartNewConversation(string npcId);

}

public class ChatClientFailedException : ApplicationException
{ }