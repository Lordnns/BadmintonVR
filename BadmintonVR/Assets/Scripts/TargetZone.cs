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
    public GameObject gamemode;
    
    [Header("Interface (UI)")]
    public GameObject floatingScorePrefab; // Glisse ton prefab ici dans l'inspecteur
    public float textSpawnHeight = 1.5f;
    
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
        if (active && other.CompareTag("Volant"))
        {
            Debug.Log("!other.GetComponent<ShuttlecockScript>().hasTouchedGround : " + !other.GetComponent<ShuttlecockScript>().hasTouchedGround);
            if (!other.GetComponent<ShuttlecockScript>().hasTouchedGround)
            {
                active = false; 
                if (meshRenderer != null)
                {
                    meshRenderer.material.SetColor(colorPropertyName, successColor);
                }
                
                // Calculate the distance between the center of the target zone and the center of the collider
                Vector3 centerPos = new Vector3(transform.position.x, 0, transform.position.z);
                Vector3 impactPos = new Vector3(other.transform.position.x, 0, other.transform.position.z);
                
                float distance = Vector3.Distance(centerPos, impactPos);
                float actualRadius = 0.5f * zoneRadius;
                float precision = 1f - Mathf.Clamp01(distance / actualRadius);
            
                int score = Mathf.RoundToInt(Mathf.Lerp(10f, 100f, precision));
                gamemode.GetComponent<Gamemode>().playerScore += score;
                
                // Spawn a floating text above the trigger zone to make it clearer which score you got for that shot
                
                if (floatingScorePrefab != null)
                {
                    Vector3 spawnPosition = centerPos + (Vector3.up * textSpawnHeight);
                    Quaternion rotationVoulue = Quaternion.Euler(0f, -90f, 0f);
                    GameObject popup = Instantiate(floatingScorePrefab, spawnPosition, rotationVoulue);
                    FloatingText floatingScript = popup.GetComponent<FloatingText>();
                    if (floatingScript != null)
                    {
                        floatingScript.Setup(score);
                    }
                }
                
                
                spawner.GetComponent<RandomTargetZoneSpawner>().spawnNewZone();
                Destroy(other.gameObject);
                OnTargetReached?.Invoke();

                StartCoroutine(DestroyItself());
            }
        }
    }

    private IEnumerator DestroyItself()
    {
        yield return new WaitForSeconds(0.75f);
        Destroy(this.gameObject);
    }
}
