using UnityEngine;
using TMPro;
using UnityEngine.Events;


public class GameTimer : MonoBehaviour
{
    public float timeLeft = 60f; // Starts with 60 seconds
    private float initialDuration;
    public TMP_Text timerText;
    private bool isTimerRunning = true;
    
    
    public UnityEvent OnTimeOut;

    void Start()
    {
        initialDuration = timeLeft;
    }
    
    void Update()
    {
        if (isTimerRunning && timeLeft > 0)
        {
            timeLeft -= Time.deltaTime; 
            UpdateTimerDisplay();
        }
        else if (timeLeft <= 0 && isTimerRunning)
        {
            timeLeft = 0;
            isTimerRunning = false;
            timerText.text = "Time: 0";
            OnTimeOut?.Invoke();
        }
    }

    void UpdateTimerDisplay()
    {
        int seconds = Mathf.CeilToInt(timeLeft);
        timerText.text = "Time: " + seconds.ToString();
    }

    public void ResetTimer()
    {
        timeLeft = initialDuration;
        isTimerRunning = true;
    }
}