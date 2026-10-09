using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class Interaction : MonoBehaviour
{
    private GameObject interactionText = null;
    private TMP_InputField dialogueBox = null;
    public GameObject responseBox = null;
    private ConversationFactory factory = null;
    private bool isPlayerNearby = false;
    private NPC npcComponent = null;
    private NpcMovement npcMovement = null;
    private PlayerMovement playerMovement = null;

    void Start()
    {
        GameObject canvas = GameObject.Find("Canvas");

        if (canvas != null)
        {
            if (interactionText == null)
            {
                interactionText = FindChildByNameIncludingInactive(canvas.transform, "PressF");
            }

            if (dialogueBox == null)
            {
                dialogueBox = canvas.GetComponentInChildren<TMP_InputField>(true);
            }

            if (responseBox == null)
            {
                responseBox = GameObject.FindGameObjectWithTag("ResponseText");
            }
        }
        else
        {
            Debug.LogError("Canvas not found in the scene.");
        }

        if (interactionText != null)
            interactionText.SetActive(false);
        else
            Debug.LogWarning("PressF text not found (even when inactive).");

        if (dialogueBox != null)
            dialogueBox.gameObject.SetActive(false);
        else
            Debug.LogWarning("Dialogue box (TMP_InputField) not found (even when inactive).");

        if (responseBox != null)
            responseBox.SetActive(false);
        else
            Debug.LogWarning("Response box (GameObject) not found (even when inactive).");

        if (factory == null)
        {
            GameObject factoryObject = GameObject.Find("ConvSessFactory");
            if (factoryObject != null)
            {
                factory = factoryObject.GetComponent<ConversationFactory>();
            }
            else
            {
                Debug.LogError("ConversationFactory object 'ConvsessFactory' not found in the scene.");
            }
        }

        npcComponent = GetComponent<NPC>();
        npcMovement = GetComponent<NpcMovement>();

        if (npcComponent == null || npcMovement == null)
        {
            Debug.LogError("Missing required components on NPC GameObject.");
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerMovement = player.GetComponent<PlayerMovement>();
        }
    }

    void Update()
    {
        bool isActiveUserSession = factory.GetNpcToUser() == npcComponent;
        if (isActiveUserSession)
        {
            if (Input.GetKeyUp(KeyCode.Tab))
            {
                dialogueBox.gameObject.SetActive(false);
                responseBox.SetActive(false);
                playerMovement.canMove = true;
                npcMovement.canMove = true;
                interactionText.SetActive(isPlayerNearby);

                // The visible conversation is over, but keep the NPC conversation-blocked
                // until event/memory post-processing has finished.
                npcComponent.isTalkingToUser = false;
                factory.StopUserConversation(npcComponent);
            }
        }
        else if (isPlayerNearby && Input.GetKeyUp(KeyCode.F) && !npcComponent.isConversationBlocked)
        {
            dialogueBox.gameObject.SetActive(true);
            responseBox.SetActive(true);
            playerMovement.canMove = false;
            npcMovement.canMove = false;
            interactionText.SetActive(false);

            npcComponent.isConversationBlocked = true;
            npcComponent.isTalkingToUser = true;
            factory.RegisterUserNPC(npcComponent, dialogueBox, responseBox);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            interactionText.SetActive(true);
            isPlayerNearby = true;
        }
        else if (other.CompareTag("NPC_Object"))
        {
            NPC otherNPCComponent = other.gameObject.GetComponent<NPC>();

            if (npcComponent.isConversationBlocked || otherNPCComponent.isConversationBlocked)
                return;

            npcComponent.isConversationBlocked = true;
            otherNPCComponent.isConversationBlocked = true;

            NpcMovement otherNpcMovement = other.gameObject.GetComponent<NpcMovement>();
            npcMovement.canMove = false;
            otherNpcMovement.canMove = false;

            List<NPC> npcs;
            if (npcComponent.idx < otherNPCComponent.idx)
            {
                npcs = new List<NPC>
                {
                    npcComponent,
                    otherNPCComponent
                };
            }
            else
            {
                npcs = new List<NPC>
                {
                    otherNPCComponent,
                    npcComponent
                };
            }
            string sessionKey = npcs[0].getName() + "-" + npcs[1].getName();
            factory.RegisterNPC(sessionKey, npcs);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            interactionText.SetActive(false);
            isPlayerNearby = false;
        }

        else if (other.CompareTag("NPC_Object"))
        {
            NPC otherNPCComponent = other.GetComponent<NPC>();

            if (ShouldContinueConversationAtSameDestination(otherNPCComponent))
            {
                Debug.Log(
                    $"[Conversation Continue] {npcComponent.getName()} and {otherNPCComponent.getName()} " +
                    "are scheduled for the same place this hour, so collider exit does not close their conversation."
                );
                return;
            }

            bool closingRequested = factory.StopNPCConversation(npcComponent, otherNPCComponent);

            if (closingRequested)
            {
                // They may continue moving while the two final conversational turns are generated.
                // isConversationBlocked stays true until post-conversation processing finishes.
                npcMovement.canMove = true;
                otherNPCComponent.GetComponent<NpcMovement>().canMove = true;
            }
        }
    }

    private bool ShouldContinueConversationAtSameDestination(NPC otherNpc)
    {
        if (npcComponent == null || otherNpc == null)
            return false;

        NPCGlobalTimer timer = FindObjectOfType<NPCGlobalTimer>();
        if (timer == null)
            return false;

        int hour = timer.GetCurrentHour();
        string myPlace = npcComponent.GetCurrentPlace(hour);
        string otherPlace = otherNpc.GetCurrentPlace(hour);

        return !string.IsNullOrWhiteSpace(myPlace) &&
               string.Equals(myPlace, otherPlace, System.StringComparison.OrdinalIgnoreCase);
    }

    private GameObject FindChildByNameIncludingInactive(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child.gameObject;
        }
        return null;
    }
}