using TMPro;
using UnityEngine;

public class GameModeUI : MonoBehaviour
{
    [Header("Scores")]
    public TMP_Text poseScoreText;
    public TMP_Text targetScoreText;
    public TMP_Text totalScoreText;

    // ── Called by Gamemode ──────────────────────────────────────────────

    public void SetPoseScore(float score)
    {
        if (poseScoreText != null)
            poseScoreText.text = "Pose Score: " + score.ToString("F0");
    }

    public void SetTargetScore(float score)
    {
        if (targetScoreText != null)
            targetScoreText.text = "Target Score: " + score.ToString("F0");
    }
    
    public void SetTotalScore(float score)
    {
        if (totalScoreText != null)
            totalScoreText.text = "Total Score: " + score.ToString("F0");
    }
    
    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}