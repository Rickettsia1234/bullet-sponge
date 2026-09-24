using UnityEngine;
using UnityEngine.UI;

public class ClearManager : MonoBehaviour
{
    [SerializeField] private Text clearTimeText;

    private void Start()
    {
        float time = GameSystem.Instance.ClearTime;
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        clearTimeText.text = $"Clear Time: {minutes:D2}:{seconds:D2}";

        GameSystem.Instance.RelayManager.ShutdownRelay();
    }

    public void GoMain()
    {
        GameSystem.Instance.NetworkRoll = NetworkRoll.None;
    }
}