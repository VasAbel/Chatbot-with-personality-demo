using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Interaction : MonoBehaviour
{
    public GameObject interactionText;
    public GameObject dialogueBox;
    private bool isNearby = false;

    void Start()
    {
        interactionText.SetActive(false);
        dialogueBox.SetActive(false);
    }

    void Update()
    {
        if(isNearby && Input.GetKeyUp(KeyCode.F))
        {
            dialogueBox.SetActive(!dialogueBox.activeSelf);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            interactionText.SetActive(true);
            isNearby = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            interactionText.SetActive(false);
            isNearby = false;
            dialogueBox.SetActive(false);
        }
    }
}
