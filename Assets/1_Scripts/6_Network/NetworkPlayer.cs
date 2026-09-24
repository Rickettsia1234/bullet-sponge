using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

public class NetworkPlayer : NetworkBehaviour
{
    [SerializeField] private Text nameText;
    [SerializeField] private Vector3 textOffset = new Vector3(0f, 0.5f, 0f);

    private readonly NetworkVariable<FixedString64Bytes> playerName = new(
        writePerm: NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        playerName.OnValueChanged += OnPlayerNameChanged;

        if (IsOwner)
        {
            GameSystem.Instance.LocalPlayer = this;
            SetPlayerNameServerRpc(GameSystem.Instance.PlayerName);
        }
        else
        {
            UpdateNameText(playerName.Value.ToString());
        }
    }

    public override void OnNetworkDespawn()
    {
        playerName.OnValueChanged -= OnPlayerNameChanged;
    }

    private void LateUpdate()
    {
        float topY = GetComponent<Collider2D>().bounds.max.y;
        nameText.transform.position = new Vector3(transform.position.x, topY + textOffset.y, transform.position.z);
        nameText.transform.rotation = Quaternion.identity;
    }

    private void OnPlayerNameChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        UpdateNameText(newValue.ToString());
    }

    private void UpdateNameText(string newName)
    {
        nameText.text = newName;
    }

    [ServerRpc]
    private void SetPlayerNameServerRpc(string name)
    {
        playerName.Value = name;
    }

    public void LeaveRoom()
    {
        GameSystem.Instance.RelayManager.ShutdownRelay();
    }
}