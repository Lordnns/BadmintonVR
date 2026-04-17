using System;
using System.Collections;
using System.Collections.Generic;
using BadmintonPoseTracking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class Gamemode : MonoBehaviour
{
    
    public static Gamemode Instance { get; private set; }
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Difficulté")] public int difficultyLevel = 1;

    [Header("Scores")]
    [Tooltip("Score minimum que le joueur doit atteindre sur ces 3 derniers tirs pour valider ce niveau")]
    public float scoreThreshold = 80.0f;

    private float playerScore = 0;
    private float precisionScore = 0;
    private float poseScore = 0;
    private Queue<float> scores = new Queue<float>();  // circular buffer, max 3

    [Header("Launcher")] [Tooltip("Launcher de volants")]
    public ShuttlecockLauncher launcher;

    public RailsMovement rails;

    private int currentSwingIndex = 0;

    [System.Serializable]
    public class SwingData
    {
        public string name;
        public Transform relativePos;
    }

    [SerializeField] public List<SwingData> swingsAndRelativePosRightHand = new List<SwingData>();
    [SerializeField] public List<SwingData> swingsAndRelativePosLeftHand = new List<SwingData>();

    List<SwingData> swingsAndRelativePos;
    
    public Transform shuttlecockTarget;

    [Header("Pose previewer")] public SwingCoordinator coordinator;

    // Input actions & UI
    [Header("XRI Input Actions")] public InputActionReference continueNextSwingAction;
    public InputActionReference skipRoundAction;

    private bool isLeftHanded;


    [Tooltip("Timer")] private float startTime;
    public GameTimer timer;
    
    [Header("UI")]
    public GameModeUI ui;

    private float roundScore = 0;
    
    IEnumerator WaitAndExecute(float waitTime, Action callback)
    {
        yield return new WaitForSeconds(waitTime);
        callback?.Invoke(); 
    }
    
    void OnEnable()
    {
        coordinator.OnSwingScored += OnPoseScored;
        
        if (skipRoundAction != null)
        {
            skipRoundAction.action.Enable();
            skipRoundAction.action.performed += OnSkipRound;
        }
    }
    
    public void OnContinueNextRound(InputAction.CallbackContext ctx)
    {
        continueNextSwingAction.action.performed -= OnContinueNextRound;
        StartLaunch();
    }

    public void OnSkipRound(InputAction.CallbackContext ctx)
    {
        scores.Clear();
        currentSwingIndex++;
 
        if (currentSwingIndex >= swingsAndRelativePos.Count)
        {
            EndGameProcess();
            return;
        }
 
        coordinator.HidePlayerReplay();
        coordinator.HideReferencePreview();
        SetLauncherPosition(Random.Range(-1f, 1f));
    }

    public void OnRestart(InputAction.CallbackContext ctx)
    {
        continueNextSwingAction.action.performed -= OnRestart;
        PrepareForNextRound();
    }

    void OnDisable()
    {
        coordinator.OnSwingScored -= OnPoseScored;
    
        if (continueNextSwingAction != null)
            continueNextSwingAction.action.performed -= OnContinueNextRound;
    
        if (skipRoundAction != null)
            skipRoundAction.action.performed -= OnSkipRound;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
            Destroy(gameObject);
        else
            Instance = this;
    }

    void Start()
    {
        // Setup correct relative targets
        isLeftHanded = GameSettings.isLeftHanded;
        swingsAndRelativePos = isLeftHanded ? swingsAndRelativePosLeftHand : swingsAndRelativePosRightHand;
        startTime = Time.time;
        
        // Wait for input to start pre-swing phase
        continueNextSwingAction.action.performed += StartTrial;
    }

    private void StartTrial(InputAction.CallbackContext ctx)
    {
        continueNextSwingAction.action.performed -= StartTrial;
        SetLauncherPosition(0);
    }
    
    private void SetLauncherPosition(float alpha)
    {
        rails.alpha = alpha;
        StartCoroutine(WaitAndExecute(1.0f, PreSwing));
    }
    
    void PreSwing()
    {
        // Reset the score values
        precisionScore = 0;
        poseScore = 0;

        // Start the launch routine
        coordinator.ShowReferencePreview(swingsAndRelativePos[currentSwingIndex].name);
        ui?.Show();
        
        // Wait for input to launch the shuttlecock
        continueNextSwingAction.action.performed += HideUIAndPreProcess;
    }

    void HideUIAndPreProcess(InputAction.CallbackContext ctx)
    {
        ui?.Hide();
        coordinator.HideReferencePreview();
        launcher.target.position = swingsAndRelativePos[currentSwingIndex].relativePos.position;
        StartCoroutine(WaitAndExecute(0.03f,StartLaunch));
    }

    void StartLaunch()
    {
        launcher.LaunchShuttlecock();
        coordinator.OnLaunch(swingsAndRelativePos[currentSwingIndex].name);
    }
    
    // After launch events
    public void OnShuttlecockLanded()
    {
        coordinator.OnShuttlecockLanded();
    }

    private void OnPoseScored(SwingScore score)
    {
        poseScore = score.Score;

        // Combined score for this attempt
        roundScore = precisionScore;
        
        coordinator.ShowReplay();
        coordinator.ShowReferencePreview(swingsAndRelativePos[currentSwingIndex].name);
        timer.Pause();
        ui?.SetPoseScore(poseScore);
        ui?.SetTargetScore(precisionScore);
        ui?.SetTotalScore(roundScore);
        ui?.SetTimeLeft(timer.timeLeft);
        ui?.SetShotsValidated(3);
        ui?.SetReferenceImage(roundScore >= scoreThreshold);
        ui?.Show();
        
        StartCoroutine(WaitAndExecute(3.0f, ProcessScore));
    }

    void ProcessScore()
    {
        // Push into circular buffer — keep last 3 only
        scores.Enqueue(roundScore);
        if (scores.Count > 3) scores.Dequeue();

        if (CheckLastThreeScores())
        {
            scores.Clear();
            currentSwingIndex++;

            if (currentSwingIndex >= swingsAndRelativePos.Count)
            {
                EndGameProcess();
                return;
            }
        }
        continueNextSwingAction.action.performed += HideUIAndPreProcess;
    }
    
    public void OnTargetZoneReached(float score)
    {
        precisionScore = score;
    }
    
    public void OnTimeOut()
    {
        // GameTimer runs for the whole session — no per-round logic.
        PreSwing();
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
        SetLauncherPosition(Random.Range(-1f, 1f));
    }
    
    // Check if last three scores are above threshold
    private bool CheckLastThreeScores()
    {
        if (scores.Count < 3)
        {
            return false;
        }
        foreach (var score in scores)
        {
            if (score < scoreThreshold)
            {
                return false;
            }
        }

        return true;
    }
    
    private void EndGameProcess()
    {
        // COMPLETED CHALLENGE LOGIC
        GameSettings.duration = Time.time - startTime;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }
}