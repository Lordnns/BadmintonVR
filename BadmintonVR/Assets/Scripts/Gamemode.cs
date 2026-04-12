using UnityEngine;

public class Gamemode : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Difficulté")] 
    public int difficultyLevel = 1;
    
    [Header("Score")]
    [Tooltip("Score du joueur")]
    public int playerScore = 0;
    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void OnGameEnd()
    {
        
    }
}
