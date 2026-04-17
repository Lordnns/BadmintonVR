using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameModeUI : MonoBehaviour
{
    [Header("Scores")]
    public TMP_Text poseScoreText;
    public TMP_Text targetScoreText;
    public TMP_Text totalScoreText;

    public TMP_Text timeLeftText;

    public TMP_Text shotsValidatedText;
    
    public RawImage referenceImage;
    [SerializeField] public  Texture2D textureValidated;
    [SerializeField] public Texture2D textureNotValidated;
    
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

    public void SetTimeLeft(float time)
    {
        if (timeLeftText != null)
            timeLeftText.text = "Time Left: " + time.ToString("F0");
    }
    
    public void SetShotsValidated(int shots)
    {
        if (shotsValidatedText != null)
            shotsValidatedText.text = shots.ToString() + " / " + "3";
    }

    public void SetReferenceImage(bool validated)
    {
        referenceImage.texture = validated ? textureValidated : textureNotValidated;
    }
    
    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}