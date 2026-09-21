using System;

public struct JoinStatus
{
    public bool isSuccess;
    public string detail;
}

public enum NetworkRoll
{
    None,
    Host,
    Client
}

public enum SessionState
{
    Idle,
    Creating,
    Joining,
    InLobby,
    Error
}

[Serializable]
public enum SceneName
{
    Main,
    Game,
    Clear,
}