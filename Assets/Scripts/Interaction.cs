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

    void Start()
    {
        interactionText.SetActive(false);
        dialogueBox.gameObject.SetActive(false);
    }

    void Update()
    {
        if(isNearby && Input.GetKeyUp(KeyCode.F))
        {
            dialogueBox.gameObject.SetActive(!dialogueBox.gameObject.activeSelf);
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
        }
    }
}
