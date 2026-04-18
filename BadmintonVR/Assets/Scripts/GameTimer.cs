using UnityEngine;
using TMPro;
using UnityEngine.Events;


public class GameTimer : MonoBehaviour
{
    public float timeLeft = 60f; // Starts with 60 seconds
    private float initialDuration;
    public TMP_Text timerText;
    private bool isTimerRunning = true;
    public TMP_Text swingTypeText;
    
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
        int minutes = Mathf.FloorToInt(timeLeft / 60); 
        int seconds = Mathf.FloorToInt(timeLeft % 60); 
        timerText.text = "Time: " + string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    public void ResetTimer()
    {
        timeLeft = initialDuration;
        isTimerRunning = true;
    }

    public void Pause()
    {
        isTimerRunning = false;
    }
    
    public void Resume()
    {
        isTimerRunning = true;
    }
    
    private string getStringFromType(int swingType)
    {
        switch (swingType)
        {
            case 0:
                return "Smash";
            case 1:
                return "Coup droit";
            case 2:
                return "Revers";
        }
        return "None";
    }

    public void SetSwingType(int swingType)
    {
        swingTypeText.text = "Tir attendu : " + getStringFromType(swingType);
    }
}