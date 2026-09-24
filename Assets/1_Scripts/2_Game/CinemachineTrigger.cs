using Unity.Cinemachine;
using UnityEngine;

public class CinemachineTrigger : MonoBehaviour
{
    private CinemachineCamera cineCam;

    private void Awake()
    {
        cineCam = GetComponent<CinemachineCamera>();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerController player = collision.GetComponent<PlayerController>();
        if (player.IsOwner)
        {
            cineCam.Priority = 20;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        PlayerController player = collision.GetComponent<PlayerController>();
        if (player.IsOwner)
        {
            cineCam.Priority = 10;
        }
    }
}