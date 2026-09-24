using System;
using UnityEngine;
using UnityEngine.UI;

public class MainManager : MonoBehaviour
{
    [SerializeField] private InputField inputJoinCode;
    [SerializeField] private InputField nameInput;

    public void CreateRoom()
    {
        string name = string.IsNullOrWhiteSpace(nameInput.text) ? ((Text)nameInput.placeholder).text : nameInput.text;
        GameSystem.Instance.PlayerName = name;
        GameSystem.Instance.NetworkRoll = NetworkRoll.Host;
    }

    public void JoinRoom()
    {
        string name = string.IsNullOrWhiteSpace(nameInput.text) ? ((Text)nameInput.placeholder).text : nameInput.text;
        string joinCode = string.IsNullOrWhiteSpace(inputJoinCode.text) ? ((Text)inputJoinCode.placeholder).text : inputJoinCode.text;

        GameSystem.Instance.PlayerName = name;
        GameSystem.Instance.JoinCode = joinCode;
        GameSystem.Instance.NetworkRoll = NetworkRoll.Client;
    }
}
