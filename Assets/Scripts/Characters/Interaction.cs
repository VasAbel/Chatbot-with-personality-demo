using TMPro;
using UnityEngine;

public class Interaction : MonoBehaviour
{
    private GameObject interactionText;
    private TMP_InputField dialogueBox;
    public GameObject responseBox;

    private ConversationManager conversationManager;
    private bool isPlayerNearby;
    private NPC npc;
    private NpcMovement npcMovement;
    private PlayerMovement playerMovement;

    private void Start()
    {
        CacheUiReferences();
        CacheRequiredComponents();
    }

    private void Update()
    {
        if (conversationManager == null || npc == null)
            return;

        if (conversationManager.IsUserConversationActiveWith(npc))
        {
            if (Input.GetKeyUp(KeyCode.Tab))
                EndUserConversation();

            return;
        }

        if (isPlayerNearby &&
            Input.GetKeyUp(KeyCode.F) &&
            conversationManager.TryStartUserConversation(npc, dialogueBox, responseBox))
        {
            SetUserConversationPresentation(active: true);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNearby = true;

            if (!conversationManager.IsUserConversationActiveWith(npc))
                interactionText?.SetActive(true);

            return;
        }

        if (!other.CompareTag("NPC_Object"))
            return;

        NPC otherNpc = other.GetComponent<NPC>();
        if (otherNpc == null)
            return;

        if (!conversationManager.TryStartNpcConversation(npc, otherNpc))
            return;

        npcMovement.canMove = false;

        NpcMovement otherMovement = otherNpc.GetComponent<NpcMovement>();
        if (otherMovement != null)
            otherMovement.canMove = false;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNearby = false;
            interactionText?.SetActive(false);
            return;
        }

        if (!other.CompareTag("NPC_Object"))
            return;

        NPC otherNpc = other.GetComponent<NPC>();
        if (otherNpc == null || ShouldContinueConversationAtSameDestination(otherNpc))
            return;

        if (!conversationManager.RequestNpcConversationClose(npc, otherNpc))
            return;

        // The graceful closing turns are still part of the conversation, but both NPCs
        // may start walking toward their next destinations while they finish talking.
        npcMovement.canMove = true;

        NpcMovement otherMovement = otherNpc.GetComponent<NpcMovement>();
        if (otherMovement != null)
            otherMovement.canMove = true;
    }

    private void EndUserConversation()
    {
        if (!conversationManager.StopUserConversation(npc))
            return;

        SetUserConversationPresentation(active: false);
    }

    private void SetUserConversationPresentation(bool active)
    {
        if (dialogueBox != null)
            dialogueBox.gameObject.SetActive(active);

        if (responseBox != null)
            responseBox.SetActive(active);

        if (playerMovement != null)
            playerMovement.canMove = !active;

        if (npcMovement != null)
            npcMovement.canMove = !active;

        if (interactionText != null)
            interactionText.SetActive(!active && isPlayerNearby);
    }

    private bool ShouldContinueConversationAtSameDestination(NPC otherNpc)
    {
        if (npc == null || otherNpc == null)
            return false;

        NPCGlobalTimer timer = FindObjectOfType<NPCGlobalTimer>();
        if (timer == null)
            return false;

        int hour = timer.GetCurrentHour();
        string myPlace = npc.GetCurrentPlace(hour);
        string otherPlace = otherNpc.GetCurrentPlace(hour);

        return !string.IsNullOrWhiteSpace(myPlace) &&
               string.Equals(
                   myPlace,
                   otherPlace,
                   System.StringComparison.OrdinalIgnoreCase
               );
    }

    private void CacheUiReferences()
    {
        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null)
        {
            Debug.LogError("Canvas not found in the scene.");
            return;
        }

        interactionText = FindChildByNameIncludingInactive(canvas.transform, "PressF");
        dialogueBox = canvas.GetComponentInChildren<TMP_InputField>(true);

        if (responseBox == null)
            responseBox = GameObject.FindGameObjectWithTag("ResponseText");

        interactionText?.SetActive(false);

        if (dialogueBox != null)
            dialogueBox.gameObject.SetActive(false);
        else
            Debug.LogWarning("Dialogue box (TMP_InputField) not found.");

        if (responseBox != null)
            responseBox.SetActive(false);
        else
            Debug.LogWarning("Response box (GameObject) not found.");
    }

    private void CacheRequiredComponents()
    {
        npc = GetComponent<NPC>();
        npcMovement = GetComponent<NpcMovement>();

        if (npc == null || npcMovement == null)
            Debug.LogError("Interaction requires NPC and NpcMovement components.");

        conversationManager = FindObjectOfType<ConversationManager>();
        if (conversationManager == null)
            Debug.LogError("ConversationManager not found in the scene.");

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            playerMovement = player.GetComponent<PlayerMovement>();
    }

    private static GameObject FindChildByNameIncludingInactive(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child.gameObject;
        }

        return null;
    }
}
