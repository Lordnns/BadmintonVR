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

    private float playerScore = 0; // Total score for the entire session
    private float roundScore = 0; // Score for this round
    private float precisionScore = 0; // Score for the precision on the target zone
    private float poseScore = 0; // Pose compared to our 'ideal' pose
    private Queue<float> scores = new Queue<float>(); // circular buffer, max 3
    private int nbSwings = 0;
    
    [Header("Launcher")] [Tooltip("Launcher de volants")]
    public ShuttlecockLauncher launcher;

    public RailsMovement rails;
    public Transform shuttlecockTarget;

    // Swings list
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

    [Header("Pose previewer")] public SwingCoordinator coordinator;

    // Input actions & UI
    [Header("XRI Input Actions")] public InputActionReference continueNextSwingAction;
    public InputActionReference skipRoundAction;

    private bool isLeftHanded;

    [Tooltip("Timer")] private float startTime;
    public GameTimer timer;

    [Header("UI")] public GameModeUI ui;

    private bool hasHitRacket = false;

    // Function that waits and then executes callback
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
    

    void OnDisable()
    {
        coordinator.OnSwingScored -= OnPoseScored;
        if (skipRoundAction != null)
            skipRoundAction.action.performed -= OnSkipRound;
    }

    public void OnSkipRound(InputAction.CallbackContext ctx)
    {
        timer.ResetTimer();
        scores.Clear();
        currentSwingIndex++;

        if (currentSwingIndex >= swingsAndRelativePos.Count)
        {
            EndGameProcess();
            return;
        }

        coordinator.HidePlayerReplay();
        coordinator.HideReferencePreview();
        ui?.Hide();
        timer.gameObject.SetActive(true);
        SetLauncherPosition(Random.Range(-1f, 1f));
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
        ui?.Hide();
        // Wait for input to start pre-swing phase
        continueNextSwingAction.action.performed += StartTrial;
    }

    private void StartTrial(InputAction.CallbackContext ctx)
    {
        ui?.Hide();
        timer.gameObject.SetActive(true);
        timer.Resume();
        continueNextSwingAction.action.performed -= StartTrial;
        SetLauncherPosition(Random.Range(-1f, 1f));
    }

    private void SetLauncherPosition(float alpha)
    {
        rails.alpha = alpha;
        timer.SetSwingType(currentSwingIndex);
        StartCoroutine(WaitAndExecute(1.0f, PreSwing));
    }

    void PreSwing()
    {
        // Reset the score values
        precisionScore = 0;
        poseScore = 0;
        hasHitRacket = false;
        // Start the launch routine
        coordinator.ShowReferencePreview(swingsAndRelativePos[currentSwingIndex].name);

        // Wait for input to launch the shuttlecock
        continueNextSwingAction.action.performed += HideUIAndPreProcess;
    }

    void HideUIAndPreProcess(InputAction.CallbackContext ctx)
    {
        continueNextSwingAction.action.performed -= HideUIAndPreProcess;
        coordinator.HideReferencePreview();
        coordinator.HidePlayerReplay();
        launcher.target.position = swingsAndRelativePos[currentSwingIndex].relativePos.position;
        StartCoroutine(WaitAndExecute(0.03f, StartLaunch));
    }

    void StartLaunch()
    {
        launcher.LaunchShuttlecock();
        coordinator.OnLaunch(swingsAndRelativePos[currentSwingIndex].name);
    }

    // After racket hit events
    public void OnShuttlecockLanded()
    {
        coordinator.OnShuttlecockLanded();
        if (!hasHitRacket)
        {
            continueNextSwingAction.action.performed += StartTrial;
        }
    }

    public void OnRacketHit()
    {
        hasHitRacket = true;
    }

    public void OnTargetZoneReached(float score)
    {
        precisionScore = score;
    }

    private void OnPoseScored(SwingScore score)
    {
        if (hasHitRacket)
        {
            nbSwings++;
            
            poseScore = score.Score;
    
            // Combined score for this attempt
            roundScore = (precisionScore + poseScore) / 2.0f;

            if (roundScore > scoreThreshold)
            {
                scores.Enqueue(roundScore);
                playerScore += roundScore;
            }
            coordinator.ShowReplay();
            coordinator.ShowReferencePreview(swingsAndRelativePos[currentSwingIndex].name);
            timer.Pause();
            ui?.SetPoseScore(poseScore);
            ui?.SetTargetScore(precisionScore);
            ui?.SetTotalScore(roundScore);
            ui?.SetTimeLeft(timer.timeLeft);
            ui?.SetShotsValidated(GetValidatedShots());
            ui?.SetReferenceImage(roundScore >= scoreThreshold);
            timer.gameObject.SetActive(false);
            ui?.Show();

            StartCoroutine(WaitAndExecute(3.0f, ProcessScore));
        }
    }

    void ProcessScore()
    {
        // Push into circular buffer — keep last 3 only
        if (roundScore < scoreThreshold)
        {
            continueNextSwingAction.action.performed += StartTrial;
            return;
        }
        if (scores.Count == 3)
        {
            OnSkipRound(default);
            return;
        }
        continueNextSwingAction.action.performed += StartTrial;
    }

    public void OnTimeOut()
    {
        OnSkipRound(default);
    }
    
    private int GetValidatedShots()
    {
        return scores.Count;
    }
    
    // COMPLETED CHALLENGE LOGIC
    private void EndGameProcess()
    {
        GameSettings.duration = Time.time - startTime;
        GameSettings.score = playerScore / nbSwings;
        Debug.Log("Score de fin de partie :" + GameSettings.score);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }
}