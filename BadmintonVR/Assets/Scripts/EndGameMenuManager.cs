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
        {
            int minutes = Mathf.FloorToInt(duration / 60);
            int seconds = Mathf.FloorToInt(duration % 60);
            timeText.text = "Time: " + string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    public void SetScoreText()
    {
        float score = GameSettings.score;
        Debug.Log("Score end game menu : " + score);
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