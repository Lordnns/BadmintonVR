using System;
using System.Collections;
using System.Collections.Generic;
using BadmintonPoseTracking;
using UnityEngine;
using System.IO;
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

    void OnEnable()
    {
        coordinator.OnSwingScored += OnPoseScored;
        
        if (skipRoundAction != null)
        {
            skipRoundAction.action.Enable();
            skipRoundAction.action.performed += OnSkipRound;
        }
    }
    
    private void BindContinue()
    {
        continueNextSwingAction.action.performed -= OnContinueNextRound;
        continueNextSwingAction.action.performed -= OnRestart;
        continueNextSwingAction.action.performed += OnContinueNextRound;
    }

    private void BindRestart()
    {
        continueNextSwingAction.action.performed -= OnContinueNextRound;
        continueNextSwingAction.action.performed -= OnRestart;
        continueNextSwingAction.action.performed += OnRestart;
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
        isLeftHanded = GameSettings.isLeftHanded;
        swingsAndRelativePos = isLeftHanded ? swingsAndRelativePosLeftHand : swingsAndRelativePosRightHand;
        startTime = Time.time;
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
        Debug.Log("swings and relative pos :" + swingsAndRelativePos.Count.ToString());
        Debug.Log("currentSwingIndex :" +  currentSwingIndex.ToString());
        coordinator.ShowReferencePreview(swingsAndRelativePos[currentSwingIndex].name);
        ui?.Show();
        BindContinue();
    }

    void StartLaunch()
    {
        ui?.Hide();
        coordinator.HideReferencePreview();
        launcher.target.position = swingsAndRelativePos[currentSwingIndex].relativePos.position;

        StartCoroutine(Delay(0.03f));
    }


    public IEnumerator Delay(float duration)
    {
        yield return new WaitForSeconds(duration);
        launcher.LaunchShuttlecock();
        coordinator.OnLaunch(swingsAndRelativePos[currentSwingIndex].name);
    }
    
    public void OnShuttlecockLanded()
    {
        coordinator.OnShuttlecockLanded();
    }

    public void OnTimeOut()
    {
        // GameTimer runs for the whole session — no per-round logic.
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

    private void EndGameProcess()
    {
        // COMPLETED CHALLENGE LOGIC
        GameSettings.duration = Time.time - startTime;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }

    private void OnPoseScored(SwingScore score)
    {
        poseScore = score.Score;

        // Combined score for this attempt
        float roundScore = precisionScore;

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

        coordinator.ShowReplay();
        coordinator.ShowReferencePreview(swingsAndRelativePos[currentSwingIndex].name);
        ui?.SetPoseScore(poseScore);
        ui?.SetTargetScore(precisionScore);
        ui?.SetTotalScore(roundScore);
        ui?.Show();
        BindRestart();
    }

    public void OnTargetZoneReached(float score)
    {
        precisionScore = score;
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
}