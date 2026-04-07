using UnityEngine;
using TMPro;

public class GameTimer : MonoBehaviour
{
    public float timeLeft = 60f; // Starts with 60 seconds
    public TMP_Text timerText;
    private bool isTimerRunning = true;

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
            // Game over logic
        }
    }

    void UpdateTimerDisplay()
    {
        int seconds = Mathf.CeilToInt(timeLeft);
        timerText.text = "Time: " + seconds.ToString();
    }
}