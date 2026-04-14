using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class EndGameMenuManager : MonoBehaviour
{
    public TMP_Text scoreText;
    public TMP_Text timeText;

    public void SetTimeText()
    {
        float duration = GameSettings.duration;
        if (timeText != null)
            timeText.text = "Time: " + duration.ToString("F2");
    }

    public void SetScoreText()
    {
        float score = GameSettings.score;
        if (scoreText != null)
            scoreText.text = "Score: " + score.ToString("F2");
    }

    public void GoBackToMenu()
    {
        SceneManager.LoadScene(0);
    }

    public void Start()
    {
        SetTimeText();
        SetScoreText();
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