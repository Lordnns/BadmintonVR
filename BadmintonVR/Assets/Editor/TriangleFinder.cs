using UnityEngine;
using UnityEditor;
using System.Linq;

public class TriangleFinder
{
    // Crée un nouveau bouton dans le menu en haut de Unity
    [MenuItem("Tools/Trouver les objets les plus lourds (Triangles)")]
    public static void FindHeavyObjects()
    {
        // 1. Récupère tous les MeshFilters de la scène active
        MeshFilter[] allMeshes = Object.FindObjectsOfType<MeshFilter>();

        // 2. Trie la liste du plus grand nombre de triangles au plus petit
        var topMeshes = allMeshes
            .Where(m => m.sharedMesh != null)
            .OrderByDescending(m => m.sharedMesh.triangles.Length / 3) // Un triangle = 3 points
            .Take(10); // Garde seulement le Top 10

        Debug.Log("<b>--- TOP 10 DES OBJETS LES PLUS LOURDS ---</b>");

        // 3. Affiche les résultats dans la console
        foreach (MeshFilter mf in topMeshes)
        {
            int triCount = mf.sharedMesh.triangles.Length / 3;
            
            // Le deuxième paramètre 'mf.gameObject' est magique : 
            // il permet de cliquer sur le message dans la console pour "ping" l'objet dans la hiérarchie !
            Debug.Log($"<color=orange>[ {triCount:N0} triangles ]</color> - {mf.gameObject.name}", mf.gameObject);
        }
    }
}