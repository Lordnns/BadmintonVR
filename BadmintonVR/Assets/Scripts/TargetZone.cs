using System;
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
        if (active)
        {
            if (other.CompareTag("Volant"))
            {
                active = false;
                if (meshRenderer != null)
                {
                    meshRenderer.material.SetColor(colorPropertyName, successColor);
                }
                //Destroy(this);
                //Destroy(other.gameObject);
                OnTargetReached?.Invoke();
            }
        }
    }
}
