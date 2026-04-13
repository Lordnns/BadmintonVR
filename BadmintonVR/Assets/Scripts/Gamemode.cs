using UnityEngine;

public class Gamemode : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Difficulté")] 
    public int difficultyLevel = 1;
    
    [Header("Score")]
    [Tooltip("Score du joueur")]
    public int playerScore = 0;
    
    [Header("Launcher")]
    public ShuttlecockLauncher launcher;

    public float launchInterval = 5;
    
    void Start()
    {
        InvokeRepeating("LaunchShuttleCock", 0, launchInterval);
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void LaunchShuttleCock()
    {
        launcher.LaunchShuttlecock();
    }


    public void OnGameEnd()
    {
        
    }
}
