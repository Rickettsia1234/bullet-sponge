using Unity.Netcode;
using UnityEngine;

public class ClearTrigger : NetworkBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerController player = collision.GetComponent<PlayerController>();
        if (player.IsOwner)
        {
            GameClearServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void GameClearServerRpc()
    {
        GameClearClientRpc();
    }

    [ClientRpc]
    private void GameClearClientRpc()
    {
        GameSystem.Instance.RecordClearTime();
        GameSystem.Instance.ChangeScene(SceneName.Clear);
    }
}