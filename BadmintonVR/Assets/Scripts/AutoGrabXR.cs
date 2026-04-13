using UnityEngine;
using System.Collections;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class AutoGrabXR : MonoBehaviour
{
    [Header("The Racket (Already in Scene)")]
    public XRGrabInteractable racketInteractable;

    [Header("The Hand Interactors (Use Near-Far)")]
    public XRBaseInteractor leftHandInteractor;
    public XRBaseInteractor rightHandInteractor;

    private void Start()
    {
        StartCoroutine(WaitAndGrabRoutine());
    }

    private IEnumerator WaitAndGrabRoutine()
    {
        // Wait exactly 2 seconds 
        Debug.Log("AutoGrab started. Waiting 2 seconds...");
        yield return new WaitForSeconds(2.0f);

        XRBaseInteractor targetHand = GameSettings.isLeftHanded ? leftHandInteractor : rightHandInteractor;
        
        if (targetHand == null || racketInteractable == null)
        {
            Debug.LogError("AutoGrab missing references!");
            yield break;
        }

        // --- TELEPORT TO THE HAND ---
        racketInteractable.transform.position = targetHand.transform.position;
        racketInteractable.transform.rotation = targetHand.transform.rotation;

        // --- FORCE THE GRAB ---
        XRInteractionManager manager = targetHand.interactionManager;
        IXRSelectInteractor interactorInterface = targetHand as IXRSelectInteractor;
        IXRSelectInteractable interactableInterface = racketInteractable as IXRSelectInteractable;

        if (manager != null && interactorInterface != null && interactableInterface != null)
        {
            manager.SelectEnter(interactorInterface, interactableInterface);
            
            string handName = GameSettings.isLeftHanded ? "LEFT" : "RIGHT";
            Debug.Log($"<b><color=green>[XR Auto-Grab]</color></b> 2 seconds passed! Racket snapped to the {handName} hand.");
        }
    }
}