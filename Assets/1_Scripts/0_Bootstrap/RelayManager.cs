using System;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using PlayEveryWare.EpicOnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples.Network;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using UnityEngine;
using Unity.Netcode.Transports.UTP;

public class RelayManager : MonoBehaviour
{
    private readonly UniTaskCompletionSource loginTaskSource = new();

    private void Start()
    {
        EnsureEOSLogin().Forget();
    }

    private async UniTaskVoid EnsureEOSLogin()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.GetComponent<UnityTransport>() != null)
        {
            loginTaskSource.TrySetResult();
            return;
        }

        await UniTask.WaitUntil(() => EOSManager.Instance != null);

        while (true)
        {
            ProductUserId existingUserId = EOSManager.Instance.GetProductUserId();
            if (existingUserId != null && existingUserId.IsValid())
            {
                loginTaskSource.TrySetResult();
                return;
            }

            bool isDone = false;
            Result loginResult = Result.UnexpectedError;

            EOSManager.Instance.StartConnectLoginWithDeviceToken("GuestUser", (loginCallbackInfo) =>
            {
                loginResult = loginCallbackInfo.ResultCode;
                if (loginCallbackInfo.ResultCode == Result.Success)
                {
                    loginTaskSource.TrySetResult();
                }
                isDone = true;
            });

            await UniTask.WaitUntil(() => isDone);

            if (loginTaskSource.Task.Status == UniTaskStatus.Succeeded)
            {
                return;
            }
            
            if (loginResult == Result.NotFound || loginResult == Result.InvalidUser)
            {
                var connectInterface = EOSManager.Instance.GetEOSConnectInterface();
                if (connectInterface != null)
                {
                    var createDeviceIdOptions = new CreateDeviceIdOptions
                    {
                        DeviceModel = SystemInfo.deviceModel
                    };

                    bool createDeviceDone = false;
                    connectInterface.CreateDeviceId(ref createDeviceIdOptions, null, (ref CreateDeviceIdCallbackInfo createResultInfo) =>
                    {
                        createDeviceDone = true;
                    });

                    await UniTask.WaitUntil(() => createDeviceDone);
                }
            }

            await UniTask.Delay(1000);
        }
    }

    public async UniTask<string> CreateRelay()
    {
        await loginTaskSource.Task;

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            await UniTask.WaitUntil(() => !NetworkManager.Singleton.IsListening);
        }

        EOSTransport eosTransport = NetworkManager.Singleton.GetComponent<EOSTransport>();
        eosTransport.Initialize(NetworkManager.Singleton);

        string joinCode = EOSManager.Instance.GetProductUserId().ToString();

        NetworkManager.Singleton.StartHost();

        return joinCode;
    }

    public async UniTask<JoinStatus> JoinRelay(string joinCode)
    {
        await loginTaskSource.Task;

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            await UniTask.WaitUntil(() => !NetworkManager.Singleton.IsListening);
        }

        EOSTransport eosTransport = NetworkManager.Singleton.GetComponent<EOSTransport>();
        eosTransport.Initialize(NetworkManager.Singleton);
        eosTransport.ServerUserIdToConnectTo = ProductUserId.FromString(joinCode);

        NetworkManager.Singleton.StartClient();

        return new JoinStatus
        {
            isSuccess = true,
            detail = joinCode
        };
    }

    public void ShutdownRelay()
    {
        NetworkManager.Singleton.Shutdown();
    }

    public void DisconnectClient(ulong clientId)
    {
        NetworkManager.Singleton.DisconnectClient(clientId);
    }
}