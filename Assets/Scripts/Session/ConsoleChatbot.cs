using UnityEngine;

/// <summary>
/// Compatibility component for existing Unity scenes/prefabs.
/// New code should depend on ConversationManager.
/// Keeping this class avoids breaking the serialized MonoBehaviour reference
/// when replacing the old ConsoleChatbot.cs script.
/// </summary>
public class ConsoleChatbot : ConversationManager
{
}
