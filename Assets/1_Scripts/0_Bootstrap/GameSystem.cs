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
        if (clientId != NetworkManager.Singleton.LocalClientId || networkRoll == NetworkRoll.Client)
        {
            StartTime = Time.time;
        }
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

    private string playerName = "";
    public string PlayerName
    {
        get => playerName;
        set => playerName = value;
    }

    public float StartTime { get; private set; }
    public float ClearTime { get; private set; }

    private SceneName currentScene = SceneName.Main;

    public void RecordClearTime()
    {
        ClearTime = Time.time - StartTime;
    }

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
        Application.targetFrameRate = 60;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientCountChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        ChangeScene(SceneName.Main);
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientCountChanged;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        OnNetworkStateChanged?.Invoke();
        if (currentScene == SceneName.Game)
        {
            if (!NetworkManager.Singleton.IsServer || clientId == NetworkManager.Singleton.LocalClientId)
            {
                NetworkRoll = NetworkRoll.None;
            }
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

    public void ChangeScene(SceneName sceneName)
    {
        currentScene = sceneName;
        sceneController.ChangeScene(sceneName);
    }
}