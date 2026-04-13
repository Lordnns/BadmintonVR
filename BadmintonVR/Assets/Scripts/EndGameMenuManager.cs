using UnityEngine;
using TMPro;

public class EndGameMenuManager : MonoBehaviour
{
    public TMP_Text scoreText;
    public TMP_Text timeText;

    public void SetTimeText()
    {
        float duration = GameSettings.duration;
        timeText.text = "Time: " + duration.ToString("F2");
    }

    public void SetScoreText()
    {
        float score = GameSettings.score;
        scoreText.text = "Score: " + score.ToString("F2");
    }

    public void Start()
    {
        SetTimeText();
    }
    
    public void QuitGame()
    {
        Debug.Log("Game Quit!");
        Application.Quit();
    #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
    #endif
    }
}