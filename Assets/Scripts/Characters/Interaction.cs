using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class Interaction : MonoBehaviour
{
    public GameObject interactionText;
    public TMP_InputField dialogueBox;
    public ConversationFactory factory;
    private bool isNearby = false;
    private bool isTalkingToPlayer = false;
    public NPC npcComponent;
    public Movement npcMovement;

    void Start()
    {
        interactionText.SetActive(false);
        dialogueBox.gameObject.SetActive(false);
    }

    void Update()
    {
        if (dialogueBox.gameObject.activeSelf && isTalkingToPlayer)
        {
            npcMovement.canMove = false; 

            if (Input.GetKeyUp(KeyCode.Tab)) 
            {
                dialogueBox.gameObject.SetActive(false);
                npcMovement.canMove = true;
                interactionText.SetActive(isNearby); 
                factory.StopUserConversation(npcComponent);
            }
        }
        else if (isNearby && Input.GetKeyUp(KeyCode.F)) 
        {
            dialogueBox.gameObject.SetActive(true);
            npcMovement.canMove = false; 
            interactionText.SetActive(false); 
            factory.RegisterUserNPC(npcComponent, dialogueBox);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isTalkingToPlayer = true;
            interactionText.SetActive(true);
            isNearby = true;
        }
        else if(other.CompareTag("NPC_Object"))
        {
            GameObject otherNPC = npcMovement.gameObject;
            NPC otherNPCComponent = otherNPC.GetComponent<NPC>();
            string sessionKey = npcComponent.getName() + "-" + otherNPCComponent.getName();
            List<NPC> npcs = new List<NPC>
            {
                npcComponent,
                otherNPCComponent
            };
            factory.RegisterNPC(sessionKey, npcs);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isTalkingToPlayer = false;
            interactionText.SetActive(false);
            isNearby = false;
            dialogueBox.gameObject.SetActive(false);
            npcMovement.canMove = true;
        }
    }
}
