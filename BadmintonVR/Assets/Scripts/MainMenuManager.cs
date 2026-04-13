using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    
    public void SelectLeftHanded()
    {
        GameSettings.isLeftHanded = true;
        Debug.Log("Set to Left-Handed mode.");
        StartGame();
    }
    
    public void SelectRightHanded()
    {
        GameSettings.isLeftHanded = false;
        Debug.Log("Set to Right-Handed mode.");
        StartGame();
    }
    
    public void StartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
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