namespace PlayEveryWare.EpicOnlineServices.Samples.Network
{
    using System;
    using UnityEngine;
    using Unity.Netcode;
    using Epic.OnlineServices;
    using System.Collections.Generic;
    using PlayEveryWare.EpicOnlineServices.Utility;

    public class EOSTransport : NetworkTransport
    {
        private EOSTransportManager P2PManager;
        private string P2PSocketName = "EOSP2PTransport";

        private bool IsInitialized = false;

        private ProductUserId OurUserId { get => LocalUserIdOverride ?? EOSManager.Instance?.GetProductUserId(); }

        public const ulong InvalidClientId = ulong.MaxValue;

        private ulong NextTransportId = 1;
        private Dictionary<ulong, ProductUserId> TransportIdToUserId = null;
        private Dictionary<ProductUserId, ulong> UserIdToTransportId = null;

        private bool IsServer = false;

        public ProductUserId ServerUserIdToConnectTo = null;

        private ProductUserId ServerUserId = null;          

        public ProductUserId LocalUserIdOverride = null;

#if UNITY_EDITOR
        public String ServerUserIdToConnectToInput = null;

        public string ServerUserIDForCopying = null;
#endif

        public override ulong ServerClientId => 0;

        public override bool IsSupported => true;

        private Queue<Tuple<ProductUserId, ulong, bool>> ConnectedDisconnectedUserEvents = null;

        [System.Diagnostics.Conditional("EOS_TRANSPORT_DEBUG")]
        private void Log(string msg)
        {
            Debug.Log(msg);
        }

        [System.Diagnostics.Conditional("EOS_TRANSPORT_DEBUG")]
        private void LogWarning(string msg)
        {
            Debug.LogWarning(msg);
        }

        [System.Diagnostics.Conditional("EOS_TRANSPORT_DEBUG")]
        private void LogError(string msg)
        {
            Debug.LogError(msg);
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            Debug.Assert(IsInitialized);

            ProductUserId userId = GetUserId(clientId);

            Log($"EOSP2PTransport.Send: [ClientId='{clientId}', UserId='{LoggingUtils.Redact(userId)}', PayloadBytes='{payload.Count}', SendTimeSec='{Time.realtimeSinceStartup}']");

            Epic.OnlineServices.P2P.PacketReliability reliability = Epic.OnlineServices.P2P.PacketReliability.ReliableOrdered;
            if (networkDelivery == NetworkDelivery.Unreliable)
            {
                reliability = Epic.OnlineServices.P2P.PacketReliability.UnreliableUnordered;
            }
            else if (networkDelivery == NetworkDelivery.Reliable)
            {
                reliability = Epic.OnlineServices.P2P.PacketReliability.ReliableUnordered;
            }
            else if(networkDelivery == NetworkDelivery.ReliableFragmentedSequenced || networkDelivery == NetworkDelivery.ReliableSequenced)
            {
                reliability = Epic.OnlineServices.P2P.PacketReliability.ReliableOrdered;
            }

            if (payload.Count > EOSTransportManager.MaxPacketSize)
            {
                if (reliability != Epic.OnlineServices.P2P.PacketReliability.ReliableOrdered)
                {
                    LogError($"EOSP2PTransport.Send: Unable to send payload - The payload size ({payload.Count} bytes) exceeds the maxmimum packet size supported by EOS P2P ({EOSTransportManager.MaxPacketSize} bytes).");
                    return;
                }
            }

            P2PManager.SendPacket(userId, P2PSocketName, payload, 0, false, reliability);
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            Debug.Assert(IsInitialized);

            if (ConnectedDisconnectedUserEvents.Count > 0)
            {
                Tuple<ProductUserId, ulong, bool> evnt = ConnectedDisconnectedUserEvents.Dequeue();
                ProductUserId evntUserId = evnt.Item1;
                ulong evntClientId = evnt.Item2;
                bool evntIsConnectionEvent = evnt.Item3;

                clientId = evntClientId;
                
                payload = new ArraySegment<byte>();
                receiveTime = Time.realtimeSinceStartup;
                NetworkEvent networkEventType = evntIsConnectionEvent ? NetworkEvent.Connect : NetworkEvent.Disconnect;
                Log($"EOSP2PTransport.PollEvent: [{networkEventType}, ClientId='{clientId}', UserId='{LoggingUtils.Redact(evntUserId)}', PayloadBytes='{payload.Count}', RecvTimeSec='{receiveTime}']");
                return networkEventType;
            }

            if (P2PManager.TryReceivePacket(out ProductUserId userId, out string socketName, out byte channel, out byte[] packet))
            {
                Debug.Assert(socketName == P2PSocketName);

                clientId = GetTransportId(userId);
                payload = new ArraySegment<byte>(packet);
                receiveTime = Time.realtimeSinceStartup;
                Log($"EOSP2PTransport.PollEvent: [{NetworkEvent.Data}, ClientId='{clientId}', UserId='{LoggingUtils.Redact(userId)}', PayloadBytes='{payload.Count}', RecvTimeSec='{receiveTime}']");
                return NetworkEvent.Data;
            }

            clientId = InvalidClientId;
            payload = new ArraySegment<byte>();
            receiveTime = 0;
            Log("EOSP2PTransport.PollEvent: []");
            return NetworkEvent.Nothing;
        }

        public override bool StartClient()
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(ServerUserIdToConnectToInput))
            {
                ServerUserIdToConnectTo = ProductUserId.FromString(ServerUserIdToConnectToInput);
            }
#endif
            if (ServerUserIdToConnectTo == null)
            {
                Log("EOSP2PTransport.StartClient: No ServerUserIDToConnectTo set!");
                return false;
            }

            Debug.Assert(IsInitialized);

            bool result;

            IsServer = false;

            if (result = (ServerUserIdToConnectTo != null && ServerUserIdToConnectTo.IsValid()))
            {
                ServerUserId = ServerUserIdToConnectTo;

                if (result = P2PManager.OpenConnection(ServerUserId, P2PSocketName))
                {
                    Log($"EOSP2PTransport.StartClient: Successful Client start up - REQUESTED outgoing '{P2PSocketName}' socket connection with Server UserId Server UserId='{LoggingUtils.Redact(ServerUserId)}'.");
                    result = true;
                }
                else
                {
                    LogError($"EOSP2PTransport.StartClient: Failed Client start up - Unable to initiate a connect request with Server UserId='{LoggingUtils.Redact(ServerUserId)}'.");
                }
            }
            else
            {
                LogError("EOSP2PTransport.StartClient: Failed Client start up - 'ServerUserIdToConnectTo' is null or invalid."
                    + " Please set a valid EOS ProductUserId of the Server host this Client should try connecting to in the 'ServerUserIdToConnectTo' property before calling StartClient"
                    + $" (ServerUserIdToConnectTo='{ServerUserIdToConnectTo}').");
            }

            return result;
        }

        public override bool StartServer()
        {
            Debug.Assert(IsInitialized);
            Log($"EOSP2PTransport.StartServer: Entering Server mode with EOS UserId='{LoggingUtils.Redact(OurUserId)}'.");
#if UNITY_EDITOR
            ServerUserIDForCopying = OurUserId.ToString();
#endif
            IsServer = true;

            return true;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            Debug.Assert(IsInitialized);
            Debug.Assert(IsServer);

            ProductUserId userId = GetUserId(clientId);

            Log($"EOSP2PTransport.DisconnectRemoteClient: Disconnecting ClientId='{clientId}' (UserId='{LoggingUtils.Redact(userId)}') from our Server.");
            P2PManager.CloseConnection(userId, P2PSocketName, true);
        }

        public override void DisconnectLocalClient()
        {
            Debug.Assert(IsInitialized);
            Debug.Assert(IsServer == false);

            Log($"EOSP2PTransport.DisconnectLocalClient: Disconnecting our Client from the Server (UserId='{LoggingUtils.Redact(ServerUserId)}').");
            P2PManager.CloseConnection(ServerUserId, P2PSocketName, true);
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            Debug.Assert(IsInitialized);
            return 0;
        }

        public override void Shutdown()
        {
            Debug.Assert(IsInitialized);
            Log("EOSP2PTransport.Shutdown: Shutting down Epic Online Services Peer-2-Peer NetworkTransport.");
            IsInitialized = false;

            if (P2PManager != null)
            {
                P2PManager.Shutdown();
                P2PManager.OnIncomingConnectionRequestedCb = null;
                P2PManager.OnConnectionOpenedCb = null;
                P2PManager.OnConnectionClosedCb = null;
                P2PManager = null;
            }

            ConnectedDisconnectedUserEvents = null;

            TransportIdToUserId = null;
            UserIdToTransportId = null;

            ServerUserId = null;
        }

        public override void Initialize(NetworkManager networkManager)
        {
            if (IsInitialized) return;
            Log("EOSP2PTransport.Initialize: Initializing Epic Online Services Peer-2-Peer NetworkTransport.");

            if (EOSManager.Instance == null)
            {
                LogError("EOSP2PTransport.Initialize: Unable to initialize - EOSManager singleton is null (has the EOSManager component been added to an object in your initial scene?)");
                return;
            }

            NextTransportId = 1;
            TransportIdToUserId = new Dictionary<ulong, ProductUserId>();
            UserIdToTransportId = new Dictionary<ProductUserId, ulong>();

            ConnectedDisconnectedUserEvents = new Queue<Tuple<ProductUserId, ulong, bool>>();

            Debug.Assert(P2PManager == null);

            if (LocalUserIdOverride?.IsValid() == true)
            {
                P2PManager = new EOSTransportManager(LocalUserIdOverride);
            }
            else
            {
                P2PManager = EOSManager.Instance.GetOrCreateManager<EOSTransportManager>();
            }
            
            P2PManager.OnIncomingConnectionRequestedCb = OnIncomingConnectionRequestedCallback;
            P2PManager.OnConnectionOpenedCb = OnConnectionOpenedCallback;
            P2PManager.OnConnectionClosedCb = OnConnectionClosedCallback;
            if (P2PManager.Initialize() == false)
            {
                LogError("EOSP2PTransport.Initialize: Unable to initialize - EOSP2PManager failed to initialize.");
                P2PManager.OnIncomingConnectionRequestedCb = null;
                P2PManager.OnConnectionOpenedCb = null;
                P2PManager.OnConnectionClosedCb = null;
                P2PManager = null;
                return;
            }

            IsInitialized = true;
        }

        public void OnIncomingConnectionRequestedCallback(ProductUserId userId, string socketName)
        {
            Debug.Assert(IsInitialized);

            if (IsServer && socketName == P2PSocketName)
            {
                Log($"EOSP2PTransport.OnIncomingConnectionRequestedCallback: ACCEPTING incoming '{socketName}' socket connection request from UserId='{LoggingUtils.Redact(userId)}'.");
                P2PManager.OpenConnection(userId, socketName);
            }
            else
            {
                Log($"EOSP2PTransport.OnIncomingConnectionRequestedCallback: REJECTING incoming '{socketName}' socket connection request from UserId='{LoggingUtils.Redact(userId)}'.");
                P2PManager.CloseConnection(userId, socketName);
            }
        }

        public void OnConnectionOpenedCallback(ProductUserId userId, string socketName)
        {
            Debug.Assert(IsInitialized);

            Log($"EOSP2PTransport.OnConnectionOpenedCallback: '{socketName}' socket connection OPENED with UserId='{LoggingUtils.Redact(userId)}'.");
            if (socketName == P2PSocketName)
            {
                if (IsServer)
                {
                    if (UserIdToTransportId.ContainsKey(userId) == false)
                    {
                        ulong newClientId = NextTransportId++;
                        TransportIdToUserId.Add(newClientId, userId);
                        UserIdToTransportId.Add(userId, newClientId);
                    }
                }

                ulong clientId = GetTransportId(userId);

                ConnectedDisconnectedUserEvents.Enqueue(new Tuple<ProductUserId, ulong, bool>(userId, clientId, true));
            }
        }

        public void OnConnectionClosedCallback(ProductUserId userId, string socketName)
        {
            Debug.Assert(IsInitialized);

            Log($"EOSP2PTransport.OnConnectionClosedCallback: '{socketName}' socket connection CLOSED with UserId='{LoggingUtils.Redact(userId)}'.");
            if (socketName == P2PSocketName)
            {
                if (IsServer)
                {
                    Debug.Assert(UserIdToTransportId.ContainsKey(userId) == true);
                }

                ulong clientId = GetTransportId(userId);

                ConnectedDisconnectedUserEvents.Enqueue(new Tuple<ProductUserId, ulong, bool>(userId, clientId, false));
            }
        }

        public ProductUserId GetUserId(ulong transportId)
        {
            Debug.Assert(IsInitialized);

            if (IsServer == false)
            {
                Debug.AssertFormat(transportId == ServerClientId, "EOSP2PTransport.GetUserId: Unexpected ClientId='{0}' given - We're a Client so we should only be dealing with the Server by definition (Server ClientId='{1}').",
                                   transportId, ServerClientId);
                return ServerUserId;
            }
            else
            {
                return TransportIdToUserId[transportId];
            }
        }

        public ulong GetTransportId(ProductUserId userId)
        {
            Debug.Assert(IsInitialized);

            if (IsServer == false)
            {
                Debug.AssertFormat(userId == ServerUserId, "EOSP2PTransport.GetClientId: Unexpected UserId='{0}' given - We're a Client so we should only be dealing with the Server by definition (Server UserId='{1}').",
                                   LoggingUtils.Redact(userId), LoggingUtils.Redact(ServerUserId));
                return ServerClientId;
            }
            else
            {
                return UserIdToTransportId[userId];
            }
        }
    }
}