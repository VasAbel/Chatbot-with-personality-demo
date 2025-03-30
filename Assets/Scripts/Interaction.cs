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
    public NPC npcComponent;
    public Npc npcMovement;

    void Start()
    {
        interactionText.SetActive(false);
        dialogueBox.gameObject.SetActive(false);
    }

    void Update()
    {
        if (dialogueBox.gameObject.activeSelf)
        {
            npcMovement.canMove = false; 

            if (Input.GetKeyUp(KeyCode.Tab)) 
            {
                dialogueBox.gameObject.SetActive(false);
                npcMovement.canMove = true;
                interactionText.SetActive(isNearby); 
            }
        }
        else if (isNearby && Input.GetKeyUp(KeyCode.F)) 
        {
            dialogueBox.gameObject.SetActive(true);
            npcMovement.canMove = false; 
            interactionText.SetActive(false); 
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            interactionText.SetActive(true);
            isNearby = true;
            factory.RegisterUserNPC(npcComponent, dialogueBox);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            interactionText.SetActive(false);
            isNearby = false;
            dialogueBox.gameObject.SetActive(false);
            npcMovement.canMove = true;
        }
    }
}
