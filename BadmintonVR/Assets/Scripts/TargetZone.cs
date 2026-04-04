using UnityEngine;

public class TargetZone : MonoBehaviour
{
    
    [Header("Retour visuel")]
    [Tooltip("Couleur quand le volant atterrit dedans")]
    public Color successColor = Color.green;
    public Color baseColor = Color.red;
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Volant"))
        {
            Debug.Log("Le volant a atterri dans la zone !");
        }
    }
}
