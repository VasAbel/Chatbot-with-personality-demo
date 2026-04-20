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

                npcComponent.isInConversation = false;
                npcComponent.isTalkingToUser = false;
                factory.StopUserConversation(npcComponent);
                
                // Trigger NPC-NPC conversation after player talks to Amy
                TriggerPostPlayerConversation();
            }
        }
        else if (isPlayerNearby && Input.GetKeyUp(KeyCode.F) && !npcComponent.isInConversation)
        {
            dialogueBox.gameObject.SetActive(true);
            responseBox.SetActive(true);
            playerMovement.canMove = false;
            npcMovement.canMove = false;
            interactionText.SetActive(false);

            npcComponent.isInConversation = true;
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

            if (npcComponent.isInConversation || otherNPCComponent.isInConversation)
                return;

            npcComponent.isInConversation = true;
            otherNPCComponent.isInConversation = true;

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
            Debug.Log($"[Conversation Start] {npcComponent.getName()} and {otherNPCComponent.getName()} started talking");
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
            StartCoroutine(DelayedStop(npcComponent, otherNPCComponent, 15f));
        }
    }

    private IEnumerator DelayedStop(NPC a, NPC b, float delay)
    {
        string sessionKey = (a.idx < b.idx)
            ? $"{a.getName()}-{b.getName()}"
            : $"{b.getName()}-{a.getName()}";

        // Wait only if conversation hasn't had enough turns yet
        float waited = 0f;
        while (waited < delay)
        {
            var session = factory.GetActiveSession(sessionKey);
            if (session != null && session.TurnCount >= 4)
                break; // enough was said, let them go
            yield return new WaitForSeconds(0.5f);
            waited += 0.5f;
        }

        bool stopped = factory.StopNPCConversation(a, b);
        if (stopped)
        {
            Debug.Log($"[Conversation End] {a.getName()} and {b.getName()} finished talking");
            a.isInConversation = false;
            a.GetComponent<NpcMovement>().canMove = true;
            b.isInConversation = false;
            b.GetComponent<NpcMovement>().canMove = true;
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

    private void TriggerPostPlayerConversation()
    {
        if (npcComponent == null || npcComponent.getName() != "Amy")
            return;

        // Find another NPC to talk to (preferably Gabriel for testing)
        NPC[] allNPCs = FindObjectsOfType<NPC>();
        NPC targetNPC = null;

        foreach (NPC npc in allNPCs)
        {
            if (npc != npcComponent && !npc.isInConversation)
            {
                // Prioritize Gabriel for testing rumor propagation
                if (npc.getName() == "Gabriel")
                {
                    targetNPC = npc;
                    break;
                }
                // Fallback to any other available NPC
                else if (targetNPC == null)
                {
                    targetNPC = npc;
                }
            }
        }

        if (targetNPC != null)
        {
            Debug.Log($"[Test] {npcComponent.getName()} will talk to {targetNPC.getName()} when they meet naturally at scheduled locations");
            
            // No forced movement needed - they'll meet at Well (hours 12-13, 18-19) or other shared locations
            // The conversation content stays in Amy's memory until she meets Gabriel
        }
    }

    private IEnumerator DelayedConversationStart(NPC otherNPC, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (!npcComponent.isInConversation && !otherNPC.isInConversation)
        {
            List<NPC> npcs = new List<NPC> { npcComponent, otherNPC };
            string sessionKey = npcs[0].getName() + "-" + npcs[1].getName();
            factory.RegisterNPC(sessionKey, npcs);
        }
    }
}