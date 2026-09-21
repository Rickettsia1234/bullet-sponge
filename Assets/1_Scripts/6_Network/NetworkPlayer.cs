using Unity.Netcode;

public class NetworkPlayer : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            GameSystem.Instance.LocalPlayer = this;
        }
    }

    public void LeaveRoom()
    {
        GameSystem.Instance.RelayManager.ShutdownRelay();
    }
}