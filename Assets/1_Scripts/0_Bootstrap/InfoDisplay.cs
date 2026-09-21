using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class InfoDisplay : MonoBehaviour
{
    [SerializeField] private Text infoText;
    private string currentJoinCode;
    private NetworkRoll currentRoll;

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

        infoText.text = $"Roll: {currentRoll}\nJoin Code: {currentJoinCode}\nPlayers: {clientCount}";
    }
}