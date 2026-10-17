using UnityEngine;

[RequireComponent(typeof(NPC))]
[RequireComponent(typeof(NpcMovement))]
public class Interaction : MonoBehaviour
{
    private ConversationManager conversationManager;
    private PlayerConversationUI conversationUi;

    private NPC selfNpc;
    private NpcMovement selfMovement;
    private PlayerMovement nearbyPlayerMovement;

    private bool isPlayerNearby;

    private void Awake()
    {
        selfNpc = GetComponent<NPC>();
        selfMovement = GetComponent<NpcMovement>();
    }

    private void Start()
    {
        conversationManager = FindObjectOfType<ConversationManager>();
        if (conversationManager == null)
            Debug.LogError("ConversationManager not found in the scene.");

        conversationUi = PlayerConversationUI.Instance;
        if (conversationUi == null)
            Debug.LogError("PlayerConversationUI not found in the scene.");
    }

    private void Update()
    {
        if (conversationManager == null || conversationUi == null)
            return;

        if (selfNpc.isTalkingToUser)
        {
            if (Input.GetKeyUp(KeyCode.Tab))
                EndUserConversation();

            return;
        }

        if (!isPlayerNearby || !Input.GetKeyUp(KeyCode.F))
            return;

        bool started = conversationManager.TryStartUserConversation(
            selfNpc,
            conversationUi.DialogueBox,
            conversationUi.ResponseBox
        );

        if (!started)
            return;

        selfMovement.canMove = false;

        if (nearbyPlayerMovement != null)
            nearbyPlayerMovement.canMove = false;

        conversationUi.SetConversationVisible(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNearby = true;
            nearbyPlayerMovement = other.GetComponentInParent<PlayerMovement>();

            if (!selfNpc.isConversationBlocked)
                conversationUi?.SetInteractionPromptVisible(true);

            return;
        }

        if (!other.CompareTag("NPC_Object"))
            return;

        NPC otherNpc = other.GetComponent<NPC>();
        if (otherNpc == null)
            return;

        if (!conversationManager.TryStartNpcConversation(selfNpc, otherNpc))
            return;

        selfMovement.canMove = false;

        NpcMovement otherMovement = otherNpc.GetComponent<NpcMovement>();
        if (otherMovement != null)
            otherMovement.canMove = false;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNearby = false;
            nearbyPlayerMovement = null;
            conversationUi?.SetInteractionPromptVisible(false);
            return;
        }

        if (!other.CompareTag("NPC_Object"))
            return;

        NPC otherNpc = other.GetComponent<NPC>();
        if (otherNpc == null)
            return;

        if (!conversationManager.RequestNpcConversationClose(selfNpc, otherNpc))
            return;

        // The graceful closing turns still belong to the current conversation,
        // but the NPCs may already continue moving toward their destinations.
        selfMovement.canMove = true;

        NpcMovement otherMovement = otherNpc.GetComponent<NpcMovement>();
        if (otherMovement != null)
            otherMovement.canMove = true;
    }

    private void EndUserConversation()
    {
        if (!conversationManager.StopUserConversation(selfNpc))
            return;

        selfMovement.canMove = true;

        if (nearbyPlayerMovement != null)
            nearbyPlayerMovement.canMove = true;

        conversationUi.SetConversationVisible(false);
        conversationUi.SetInteractionPromptVisible(isPlayerNearby);
    }
}
