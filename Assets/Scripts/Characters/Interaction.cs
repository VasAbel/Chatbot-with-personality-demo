using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class Interaction : MonoBehaviour
{
    private GameObject interactionText = null;
    private TMP_InputField dialogueBox = null;
    private ConversationFactory factory = null;
    private bool isPlayerNearby = false;
    private NPC npcComponent = null;
    private NpcMovement npcMovement = null;
    private PlayerMovement playerMovement= null;

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
                playerMovement.canMove = true;
                npcMovement.canMove = true;
                interactionText.SetActive(isPlayerNearby); 

                npcComponent.isInConversation = false;
                npcComponent.isTalkingToUser = false;
                factory.StopUserConversation(npcComponent);
            }
        }
        else if (isPlayerNearby && Input.GetKeyUp(KeyCode.F) && !npcComponent.isInConversation) 
        {
            dialogueBox.gameObject.SetActive(true);
            playerMovement.canMove = false; 
            npcMovement.canMove = false;
            interactionText.SetActive(false); 

            npcComponent.isInConversation = true;
            npcComponent.isTalkingToUser = true;
            factory.RegisterUserNPC(npcComponent, dialogueBox);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            interactionText.SetActive(true);
            isPlayerNearby = true;
        }
        else if(other.CompareTag("NPC_Object"))
        {
            NPC otherNPCComponent = other.gameObject.GetComponent<NPC>();

            if (npcComponent.isInConversation || otherNPCComponent.isInConversation)
                return;
            
            npcComponent.isInConversation = true;
            otherNPCComponent.isInConversation = true;

            NpcMovement otherNpcMovement = other.gameObject.GetComponent<NpcMovement>();
            npcMovement.canMove = false;
            otherNpcMovement.canMove = false;
            
            List<NPC> npcs;
            if(npcComponent.idx < otherNPCComponent.idx)
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

            bool stopped = factory.StopNPCConversation(npcComponent, otherNPCComponent);

            if (stopped)
            {
                npcComponent.isInConversation = false;
                npcMovement.canMove = true;

                otherNPCComponent.isInConversation = false;
                otherNPCComponent.GetComponent<NpcMovement>().canMove = true;
            }
        }
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
