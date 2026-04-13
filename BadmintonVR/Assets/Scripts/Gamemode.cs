using System.Collections;
using System.Collections.Generic;
using BadmintonPoseTracking;
using UnityEngine;
using System.IO;
using UnityEngine.InputSystem;

public class Gamemode : MonoBehaviour
{   
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Difficulté")] 
    public int difficultyLevel = 1;

    [Header("Scores")]
    [Tooltip("Score minimum que le joueur doit atteindre sur ces 3 derniers tirs pour valider ce niveau")]
    public float scoreThreshold = 80.0f;
    private float playerScore = 0;
    private float precisionScore = 0;
    private float poseScore = 0;
    private Queue<float> scores = new Queue<float>();
    
    [Header("Launcher")]
    [Tooltip("Launcher de volants")]
    public ShuttlecockLauncher launcher;
    
    public RailsMovement rails;
    public float launchInterval = 2f;
    
    private int currentSwingIndex = 0;
    public List<string> swings = new List<string>();
    
    public Transform shuttlecockTarget;
    
    [Header("Pose previewer")]
    public SwingCoordinator coordinator;

    private bool isLeftHanded;
    
    
    void Start()
    {
        isLeftHanded = GameSettings.isLeftHanded;
        Debug.Log("GAMEMODE :" + isLeftHanded);
        coordinator.OnSwingScored += OnPoseScored;
        InvokeRepeating("SetLauncherPosition", 2f, launchInterval);
    }

    private void SetLauncherPosition(float alpha)
    {
        rails.alpha = alpha;
        StartCoroutine(DelayForLauncherPosition(1.0f));
        
    }
    public IEnumerator DelayForLauncherPosition(float duration)  
    {
        yield return new WaitForSeconds(duration);
        PreSwing();
    }
    
    
    void PreSwing()
    {
        // Reset the score values
        precisionScore = 0;
        poseScore = 0;
        
        // Start the launch routine
        coordinator.ShowReferencePreview(swings[currentSwingIndex]);

        string path = File.ReadAllText(SwingReplayVisualizer.SwingPath(swings[currentSwingIndex]));
        var dto = JsonUtility.FromJson<SwingDto>(path);
        if (dto?.frames == null || dto.frames.Length == 0)
        {
            Debug.LogWarning($"[SwingReplayVisualizer] Empty or corrupt: {path}");
            return;
        }
        StartCoroutine(DelayForReferencePreview(dto.durationSeconds + 1.0f));

    }

    public IEnumerator DelayForReferencePreview(float duration)  
    {
        yield return new WaitForSeconds(duration);
        coordinator.HideReferencePreview();
        StartLaunch();

    }

    void StartLaunch()
    {
        launcher.target.position = shuttlecockTarget.transform.position;
        launcher.LaunchShuttlecock();
        coordinator.OnLaunch(swings[currentSwingIndex]);
    }
    
    public void OnShuttlecockLanded()
    {
        coordinator.OnShuttlecockLanded();
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




    public void ShowScoreUI()
    {
        // Show score UI
        coordinator.ShowReplay();
        coordinator.ShowReferencePreview(swings[currentSwingIndex]);
    }

    public void PrepareForNextRound()
    {
        coordinator.HidePlayerReplay();
        PreSwing();
    }

    // Gather the precision scores and pose scores from target zones and coordinator
    private void OnPoseScored(SwingScore score)
    {
        poseScore = score.Score;
    }
    
    public void OnTargetZoneReached(float score)
    {
        precisionScore = score;
    }
    
    // Check if last three scores are above threshold
    private bool CheckLastThreeScores()
    {
        foreach (var score in scores)
        {
            if (score < scoreThreshold)
            {
                return false;
            }
        }

        return true;
    }
}
