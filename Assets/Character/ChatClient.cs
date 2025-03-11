using UnityEngine;
using System.Threading.Tasks;
using System;

public abstract class ChatClient : MonoBehaviour
{
    public abstract Task<string> SendChatMessageAsync(string messageContent);

}

public class ChatClientFailedException : ApplicationException
{ }