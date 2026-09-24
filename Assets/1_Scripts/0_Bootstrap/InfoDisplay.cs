using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class InfoDisplay : MonoBehaviour
{
    [SerializeField] private Text infoText;
    private string currentJoinCode;
    private NetworkRoll currentRoll;
    private float timer;

    private void Update()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            timer += Time.deltaTime;
            if (timer >= 1f)
            {
                timer = 0f;
                RefreshUI();
            }
        }
    }

    public void SetGameInfo(string joinCode, NetworkRoll roll)
    {
        currentJoinCode = joinCode;
        currentRoll = roll;
        RefreshUI();
    }
    
    public void RegisterNetworkEvents()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientChanged;
        RefreshUI();
    }

    public void UnregisterNetworkEvents()
    {
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientChanged;
        infoText.text = "";
    }

    private void OnClientChanged(ulong clientId)
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        int clientCount = 0;
        if (NetworkManager.Singleton.IsListening)
        {
            clientCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        }

        float elapsedTime = Time.time - GameSystem.Instance.StartTime;
        int minutes = Mathf.FloorToInt(elapsedTime / 60f);
        int seconds = Mathf.FloorToInt(elapsedTime % 60f);

        infoText.text = $"\nRoll: {currentRoll}\nJoin Code: {currentJoinCode}\nPlayers: {clientCount}\nTime: {minutes:D2}:{seconds:D2}";
    }
}