using System;
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
    [System.Serializable]
    public class SwingData
    {
        public string name;
        public Transform relativePos;
    }
    [SerializeField] 
    public List<SwingData> swingsAndRelativePos = new List<SwingData>();
    public Transform shuttlecockTarget;
    
    [Header("Pose previewer")]
    public SwingCoordinator coordinator;
    
    // Input actions & UI
    [Header("XRI Input Actions")]
    public InputActionReference continueNextSwingAction;
    public InputActionReference skipRoundAction;
    public InputActionReference restartAction;

    public Action continueNextSwingBtn;
    public Action skipRoundBtn;
    
    
    private bool isLeftHanded;
    
    void OnEnable()
    {
        coordinator.OnSwingScored += OnPoseScored;
        if (continueNextSwingAction != null)
        {
            continueNextSwingAction.action.Enable();
            continueNextSwingAction.action.performed += OnContinueNextRound;
        }

        if (skipRoundAction != null)
        {
            skipRoundAction.action.Enable();
            skipRoundAction.action.performed += OnSkipRound;
        }

        if (restartAction != null)
        {
            restartAction.action.Enable();
            restartAction.action.performed += OnRestart;
        }

    }

    public void OnContinueNextRound(InputAction.CallbackContext ctx)
    {
        StartLaunch();
    }

    public void OnSkipRound(InputAction.CallbackContext ctx)
    {
        
    }

    public void OnRestart(InputAction.CallbackContext ctx)
    {
        PrepareForNextRound();
    }

    void OnDisable()
    {
        coordinator.OnSwingScored -= OnPoseScored;
        continueNextSwingAction.action.performed -= OnContinueNextRound;
        skipRoundAction.action.performed -= OnSkipRound;
        restartAction.action.performed -= OnRestart;
    }
    
    void Start()
    {
        isLeftHanded = GameSettings.isLeftHanded;
        SetLauncherPosition(0);
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
        coordinator.ShowReferencePreview(swingsAndRelativePos[currentSwingIndex].name);
    }

    void StartLaunch()
    {
        coordinator.HideReferencePreview();
        launcher.target.position = swingsAndRelativePos[currentSwingIndex].relativePos.position;
        launcher.LaunchShuttlecock();
        coordinator.OnLaunch(swingsAndRelativePos[currentSwingIndex].name);
    }
    
    public void OnShuttlecockLanded()
    {
        coordinator.OnShuttlecockLanded();
    }

    public void OnGameEnd()
    {
        
    }
    
    public void ShowScoreUI()
    {
        // Show score UI
        coordinator.ShowReplay();
        coordinator.ShowReferencePreview(swingsAndRelativePos[currentSwingIndex].name);
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

