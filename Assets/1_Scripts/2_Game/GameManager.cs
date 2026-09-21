using UnityEngine;

public class GameManager : MonoBehaviour
{
    private void Start()
    {
        GameSystem.Instance.InitializeRoom(StartGame);
    }

    private void StartGame()
    {
        
    }
}
