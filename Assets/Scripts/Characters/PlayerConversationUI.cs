using TMPro;
using UnityEngine;

/// <summary>
/// Owns the shared scene references and visibility state for player-NPC conversation UI.
/// Attach exactly one instance to the UI/Canvas object and wire the three fields in the Inspector.
/// </summary>
public class PlayerConversationUI : MonoBehaviour
{
    public static PlayerConversationUI Instance { get; private set; }

    [SerializeField] private GameObject interactionPrompt;
    [SerializeField] private TMP_InputField dialogueBox;
    [SerializeField] private GameObject responseBox;

    public TMP_InputField DialogueBox => dialogueBox;
    public GameObject ResponseBox => responseBox;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Multiple PlayerConversationUI instances found. Keep exactly one.");
            return;
        }

        Instance = this;

        SetInteractionPromptVisible(false);
        SetConversationVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetInteractionPromptVisible(bool visible)
    {
        if (interactionPrompt != null)
            interactionPrompt.SetActive(visible);
    }

    public void SetConversationVisible(bool visible)
    {
        if (dialogueBox != null)
            dialogueBox.gameObject.SetActive(visible);

        if (responseBox != null)
            responseBox.SetActive(visible);
    }
}
