using System;
using UnityEngine;
using UnityEngine.UI;

public class MainManager : MonoBehaviour
{
    [SerializeField] private InputField input;
    public void CreateRoom()
    {
        GameSystem.Instance.NetworkRoll = NetworkRoll.Host;
    }

    public void JoinRoom()
    {
        GameSystem.Instance.JoinCode = input.text;
        GameSystem.Instance.NetworkRoll = NetworkRoll.Client;
    }
}
