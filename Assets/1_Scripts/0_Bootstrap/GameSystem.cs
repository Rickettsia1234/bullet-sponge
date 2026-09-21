using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;

public class GameSystem : MonoBehaviour
{
    public static GameSystem Instance { get; private set; }
    [SerializeField] private SceneController sceneController;
    [SerializeField] private RelayManager relayManager;
    public RelayManager RelayManager => relayManager;
    [SerializeField] private InfoDisplay gameInfoDisplay;
    public NetworkPlayer LocalPlayer { get; set; }

    public event Action OnNetworkStateChanged;
    private void OnClientCountChanged(ulong clientId)
    {
        OnNetworkStateChanged?.Invoke();
    }

    private SessionState sessionState = SessionState.Idle;
    public SessionState SessionState
    {
        get => sessionState;
        private set
        {
            sessionState = value;
            OnNetworkStateChanged?.Invoke();
        }
    }

    [SerializeField] private NetworkRoll networkRoll = NetworkRoll.None;
    public NetworkRoll NetworkRoll
    {
        get => networkRoll;
        set
        {
            networkRoll = value;
            OnNetworkStateChanged?.Invoke();
            if (networkRoll == NetworkRoll.None)
            {
                gameInfoDisplay.UnregisterNetworkEvents();
                sceneController.ChangeScene(SceneName.Main);
            }
            else
            {
                sceneController.ChangeScene(SceneName.Game);
            }
        }
    }

    private string joinCode = "";
    public string JoinCode
    {
        get => joinCode;
        set
        {
            joinCode = value;
            OnNetworkStateChanged?.Invoke();
        }
    }

    private JoinStatus lastJoinStatus;
    public JoinStatus LastJoinStatus
    {
        get => lastJoinStatus;
        private set
        {
            lastJoinStatus = value;
            OnNetworkStateChanged?.Invoke();
        }
    }

    public int PlayerCount => NetworkManager.Singleton.ConnectedClientsIds.Count;
    public bool IsListening => NetworkManager.Singleton.IsListening;
    public ulong LocalClientId => NetworkManager.Singleton.LocalClientId;
    public bool IsHost => NetworkManager.Singleton.IsHost;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientCountChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientCountChanged;
        sceneController.ChangeScene(SceneName.Main);
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientCountChanged;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientCountChanged;
        }
    }

    private void CreateRoom(Action onComplete)
    {
        CreateRoomInternal(onComplete).Forget();
    }

    private async UniTaskVoid CreateRoomInternal(Action onComplete)
    {
        SessionState = SessionState.Creating;
        string joinCode = await relayManager.CreateRelay();
        JoinCode = joinCode;
        SessionState = SessionState.InLobby;
        gameInfoDisplay.SetGameInfo(JoinCode, networkRoll);
        gameInfoDisplay.RegisterNetworkEvents();
        onComplete?.Invoke();
    }

    private void JoinRoom(string joinCode, Action onComplete)
    {
        JoinRoomInternal(joinCode, onComplete).Forget();
    }

    private async UniTaskVoid JoinRoomInternal(string joinCode, Action onComplete)
    {
        SessionState = SessionState.Joining;
        JoinStatus joinStatus = await relayManager.JoinRelay(joinCode);
        LastJoinStatus = joinStatus;
        if (joinStatus.isSuccess)
        {
            SessionState = SessionState.InLobby;
            gameInfoDisplay.SetGameInfo(JoinCode, networkRoll);
            gameInfoDisplay.RegisterNetworkEvents();
        }
        else
        {
            SessionState = SessionState.Error;
        }
        onComplete?.Invoke();
    }

    public void InitializeRoom(Action onComplete)
    {
        if (networkRoll == NetworkRoll.Host)
        {
            CreateRoom(onComplete);
        }
        else if (networkRoll == NetworkRoll.Client)
        {
            JoinRoom(JoinCode, onComplete);
        }
    }
}
