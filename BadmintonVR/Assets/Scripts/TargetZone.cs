using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class TargetZone : MonoBehaviour
{
    [Header("Difficulté")]
    [Tooltip("Taille de la zone")]
    [Range(0.2f, 3f)]
    public float zoneRadius = 1f;
    
    [Header("Retour visuel")]
    [Tooltip("Couleur quand le volant atterrit dedans")]
    public Color successColor = Color.green;
    public Color baseColor = Color.red;
    
    public UnityEvent OnTargetReached;
    
    private string colorPropertyName = "_ZoneColor";
    private Renderer meshRenderer;
    private bool active = true;


    public GameObject spawner;
    
    void OnValidate()
    {
        ApplySize();
    }
    
    public void Start()
    {
        meshRenderer = GetComponent<Renderer>();
        if (meshRenderer != null)
        {
            meshRenderer.material.SetColor(colorPropertyName, baseColor);
        }
        ApplySize();
    }
    
    public void SetDifficultySize(float newRadius)
    {
        zoneRadius = newRadius;
        ApplySize();
    }

    private void ApplySize()
    {
        // On modifie l'échelle sur X et Z pour la largeur, 
        // mais on conserve l'échelle Y d'origine pour la hauteur du rayon lumineux.
        transform.localScale = new Vector3(zoneRadius, transform.localScale.y, zoneRadius);
    }
    
    void OnTriggerEnter(Collider other)
    {

    }

    private IEnumerator DestroyItself()
    {
        yield return new WaitForSeconds(0.75f);
        Destroy(this.gameObject);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (active)
        {
            
            if (collision.gameObject.CompareTag("Volant"))
            {
                active = false; 
                if (meshRenderer != null)
                {
                    meshRenderer.material.SetColor(colorPropertyName, successColor);
                }
                
                spawner.GetComponent<RandomTargetZoneSpawner>().spawnNewZone();
                Destroy(collision.gameObject.gameObject);
                OnTargetReached?.Invoke();

                StartCoroutine(DestroyItself());
            }
        }
        Debug.Log("Collision enter");
        foreach (ContactPoint contact in collision.contacts)
        {
            Debug.DrawRay(contact.point, contact.normal, Color.white);
        }
    }
}
