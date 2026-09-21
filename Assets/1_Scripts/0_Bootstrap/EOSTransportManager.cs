/*
* Copyright (c) 2026 Epic Games Inc
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/

//#define EOS_TRANSPORTMANAGER_DEBUG
namespace PlayEveryWare.EpicOnlineServices.Samples.Network
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    using UnityEngine;
    using Unity.Netcode;

    using Epic.OnlineServices;
    using Epic.OnlineServices.P2P;
    using PlayEveryWare.EpicOnlineServices.Utility;
    using System.Text.RegularExpressions;

    public class EOSTransportManager : IEOSSubManager
    {
        private const ushort MaxFragments = short.MaxValue + 1;
        private const ushort FragmentHeaderSize = 4;

        public const int MaxPacketSize = P2PInterface.MAX_PACKET_SIZE;
        public const int MaxConnections = P2PInterface.MAX_CONNECTIONS;

        public class Connection : IEquatable<Connection>
        {
            public SocketId SocketId = new SocketId();
            public string SocketName { get => SocketId.SocketName; set => SocketId.SocketName = value; }

            public bool OpenedOutgoing = false;
            public bool OpenedIncoming = false;

            public bool IsPendingOutgoing { get => IsValid && (OpenedOutgoing && !OpenedIncoming); }
            public bool IsPendingIncoming { get => IsValid && (!OpenedOutgoing && OpenedIncoming); }

            public bool IsHalfOpened { get => IsValid && (OpenedOutgoing || OpenedIncoming); }
            public bool IsFullyOpened { get => IsValid && (OpenedOutgoing && OpenedIncoming); }

            public bool ConnectionOpenedHandled = false;
            public bool ConnectionClosedHandled = false;

            private ushort CurrentPacketIndex = 0;
            public bool IsValid = false;

            public Connection() { }
            public Connection(string socketName) { SocketName = socketName; }

            public ushort GetNextMessageIndex() { return CurrentPacketIndex++; }

            public override int GetHashCode()
            {
                return SocketName.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as Connection);
            }

            public bool Equals(string socketName)
            {
                return SocketName == socketName;
            }

            public bool Equals(Connection connection)
            {
                return SocketName == connection.SocketName;
            }

            public string DebugStringJSON()
            {
                return string.Format("{{\"SocketName\": {1}\"}}", SocketName);
            }
        }

        public delegate void OnIncomingConnectionRequestedCallback(ProductUserId remoteUserId, string socketName);
        public delegate void OnConnectionOpenedCallback(ProductUserId remoteUserId, string socketName);
        public delegate void OnConnectionClosedCallback(ProductUserId remoteUserId, string socketName);

        public OnIncomingConnectionRequestedCallback OnIncomingConnectionRequestedCb = null;
        public OnConnectionOpenedCallback OnConnectionOpenedCb = null;
        public OnConnectionClosedCallback OnConnectionClosedCb = null;

        public P2PInterface P2PHandle;
        public NATType NATType;
        public ProductUserId LocalUserId;
        private ProductUserId LocalUserIdOverride = null;

        private Dictionary<ProductUserId, List<Connection>> Connections;
        private Dictionary<ushort, SortedList<ushort, byte[]> > InProgressPackets;

        private bool IsInitialized = false;

        private static readonly byte[] ConnectionConfirmationPacket = Encoding.ASCII.GetBytes("READY");
        private const byte ConnectionConfirmationChannel = byte.MaxValue;

        private ulong ConnectionEstablishedNotificationsId = 0;
        private ulong ConnectionInterruptedNotificationsId = 0;

        [System.Diagnostics.Conditional("EOS_TRANSPORTMANAGER_DEBUG")]
        private void Log(string msg)
        {
            Debug.Log(msg);
        }

        [System.Diagnostics.Conditional("EOS_TRANSPORTMANAGER_DEBUG")]
        private void LogWarning(string msg)
        {
            Debug.LogWarning(msg);
        }

        [System.Diagnostics.Conditional("EOS_TRANSPORTMANAGER_DEBUG")]
        private void LogError(string msg)
        {
            Debug.LogError(msg);
        }

        public string GetDebugString(bool includeConnections = false)
        {
            if (IsInitialized == false)
            {
                return "{}";
            }
            else
            {
                string connectionString = includeConnections ? ConnectionsJSONFormatString() : "";
                return $"{{\"LocalUserId\": {LocalUserId}, \"NATType\": {NATType}{connectionString}}}";
            }
        }

        private string ConnectionsJSONFormatString()
        {
            string res = "";

            foreach (KeyValuePair<ProductUserId, List<Connection>> entry in Connections)
            {
                ProductUserId user = entry.Key;
                res += string.Format("{{\"RemoteUserId\": {0}, \"Connections\": [", LoggingUtils.Redact(user));
                for (int j = 0; j < entry.Value.Count; ++j)
                {
                    res += entry.Value[j].DebugStringJSON();

                    if (j + 1 < entry.Value.Count)
                        res += ", ";
                }
                res += "]}";
            }
            return string.Format(", \"Remote Users\": [{0}]", res);
        }

        public EOSTransportManager()
        {
            Clear();
        }

        public EOSTransportManager(ProductUserId overrideId)
        {
            Clear();
            LocalUserIdOverride = overrideId;
        }

        private void Clear()
        {
            P2PHandle = null;
            NATType = NATType.Unknown;
            LocalUserId = null;
            Connections = null;

            IsInitialized = false;
        }

#if UNITY_EDITOR
        void OnPlayModeChanged(UnityEditor.PlayModeStateChange modeChange)
        {
            if (modeChange == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                P2PHandle = null;
                Shutdown();
            }
        }
#endif

        public bool Initialize()
        {
            if (IsInitialized)
            {
                LogWarning("EOSTransportManager.Initialize: Already initialized - Shutting down EOSTransportManager first before proceeding.");
                Shutdown();
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif

            Debug.Assert(IsInitialized == false);
            Log("EOSTransportManager.Initialize: Initializing EOSTransportManager...");
            bool result;

            if (EOSManager.Instance == null)
            {
                LogError("EOSTransportManager.Initialize: Failed to initialize EOSTransportManager - Unable to get EOSManager singleton instance.");
                result = false;
            }
            else
            {
                P2PHandle = EOSManager.Instance.GetEOSP2PInterface();
                NATType = NATType.Unknown;
                if (LocalUserIdOverride?.IsValid() == true)
                {
                    LocalUserId = LocalUserIdOverride;
                }
                else
                {
                    LocalUserId = EOSManager.Instance.GetProductUserId();
                }
                Connections = new Dictionary<ProductUserId, List<Connection>>();
                InProgressPackets = new Dictionary<ushort, SortedList<ushort, byte[]>>();

                EOSManager.Instance.AddApplicationCloseListener(Shutdown);
            }

            if (P2PHandle == null)
            {
                LogError("EOSTransportManager.Initialize: Failed to initialize EOSTransportManager - Unable to get EOS P2PInterface handle.");
                result = false;
            }
            else if (LocalUserId == null || LocalUserId.IsValid() == false)
            {
                LogError("EOSTransportManager.Initialize: Failed to initialize EOSTransportManager - Invalid local ProductUserId.");
                result = false;
            }
            else
            {
                result = true;
            }

            if (result == false)
            {
                Clear();
            }
            else
            {
                IsInitialized = true;
                SubscribeToConnectionRequestNotifications();
                SubscribeToConnectionClosedNotifications();
                SubscribeToConnectionEstablishedNotifications();
                SubscribeToConnectionInterruptedNotifications();
                QueryNATType();
            }

            return result;
        }

        public void Shutdown()
        {
            if (IsInitialized == false)
            {
                LogWarning("EOSTransportManager.Shutdown: EOSTransportManager is already shutdown or was never initialized.");
                return;
            }

            Log($"EOSTransportManager.Shutdown: Shutting down EOSTransportManager... | EOSTransportManager={GetDebugString()}");
            CloseAllConnections();
            UnsubscribeFromConnectionInterruptedNotifications();
            UnsubscribeFromConnectionEstablishedNotifications();
            UnsubscribeFromConnectionClosedNotifications();
            UnsubscribeFromConnectionRequestNotifications();
            Clear();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
#endif
        }

        public void OnLoggedIn()
        {
            Log($"EOSTransportManager.OnLoggedIn: Logged in with LocalUserId '{LoggingUtils.Redact(EOSManager.Instance.GetProductUserId())}' - Initializing EOSTransportManager.");
            Initialize();
        }

        public void OnLoggedOut()
        {
            Log($"EOSTransportManager.OnLoggedOut: Logging out with LocalUserId '{LoggingUtils.Redact(EOSManager.Instance.GetProductUserId())}' - Shutting down EOSTransportManager.");
            Shutdown();
        }

        public void QueryNATType()
        {
            Log("EOSTransportManager.QueryNATType: Querying our NAT type...");
            var options = new QueryNATTypeOptions();
            P2PHandle.QueryNATType(ref options, null, OnQueryNATTypeCompleted);
        }

        private void OnQueryNATTypeCompleted(ref OnQueryNATTypeCompleteInfo data)
        {
            if (data.ResultCode != Result.Success)
            {
                if (data.ResultCode != Result.NoConnection)
                {
                    LogWarning($"EOSTransportManager.OnQueryNATTypeCompleted: Error result, {data.ResultCode}");
                }
                return;
            }
            Log($"EOSTransportManager.OnQueryNATTypeCompleted: Successfully retrieved NATType '{data.NATType}' (previous value was '{NATType}').");
            NATType = data.NATType;
        }

        public NATType GetNATType()
        {
            var options = new GetNATTypeOptions();
            Result result = P2PHandle.GetNATType(ref options, out NATType natType);

            if (result == Result.NotFound)
            {
                return NATType.Unknown;
            }

            if (result != Result.Success)
            {
                LogWarning($"EOSTransportManager.GetNATType: Error while retrieving NATType, {result}.");
                return NATType.Unknown;
            }
            Log($"EOSTransportManager.GetNATType: Successfully retrieved NATType '{natType}'.");
            return natType;
        }

        public static bool IsValidSocketName(string name)
        {
            return (name != null)
                && (name.Length > 0)
                && (name.Length <= 32)
                && Regex.IsMatch(name, "^[a-zA-Z0-9]+$");
        }

        public int AllConnectionsCount { get => Connections.Values.Sum(connections => connections.Count); }
        public int PendingOutgoingConnectionsCount { get => Connections.Values.Sum(connections => connections.Where(connection => connection.IsPendingOutgoing).Count()); }
        public int PendingIncomingConnectionsCount { get => Connections.Values.Sum(connections => connections.Where(connection => connection.IsPendingIncoming).Count()); }
        public int FullyOpenConnectionsCount { get => Connections.Values.Sum(connections => connections.Where(connection => connection.IsFullyOpened).Count()); }

        public bool HasConnection(ProductUserId remoteUserId, string socketName)
        {
            return TryGetConnection(remoteUserId, socketName, out _);
        }

        public bool TryGetConnection(ProductUserId remoteUserId, string socketName, out Connection connection)
        {
            if (Connections.TryGetValue(remoteUserId, out List<Connection> connections))
                connection = connections.Find(x => x.SocketName == socketName);
            else
                connection = null;

            return (connection != null);
        }

        public bool OpenConnection(ProductUserId remoteUserId, string socketName)
        {
            Log($"EOSTransportManager.OpenConnection: Attempting to locally open (outgoing) socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'...");
            return Internal_OpenConnection(remoteUserId, socketName, true, out Connection _);
        }

        private bool Internal_OpenConnection(ProductUserId remoteUserId, string socketName, bool openOutgoing, out Connection connection)
        {
            connection = null;

            if (IsInitialized == false)
            {
                LogError("EOSTransportManager.Internal_OpenConnection: Failed to open remote peer connection - EOSTransportManager is uninitialized (has OnLoggedIn been called?).");
                return false;
            }

            if (IsValidSocketName(socketName) == false)
            {
                LogError($"EOSTransportManager.Internal_OpenConnection: Failed to open remote peer connection - Socket name '{socketName}' is invalid (EOS socket names may only contain 1-32 alphanumeric characters).");
                return false;
            }

            List<Connection> connections;

            if (Connections.TryGetValue(remoteUserId, out List<Connection> foundConnections))
            {
                connections = foundConnections;

                if (connections.Count >= MaxConnections)
                {
                    LogError($"EOSTransportManager.Internal_OpenConnection: Failed to open remote peer connection - Reached maximum number of open connections ({MaxConnections}) with the specified remote peer.");
                    return false;
                }
            }
            else
            {
                connections = new List<Connection>();
                Connections.Add(remoteUserId, connections);
            }

            connection = connections.Find(x => x.SocketName == socketName);

            if (connection == null)
            {
                connection = new Connection(socketName);
                connections.Add(connection);
            }
            else
            {
                Debug.Assert(connection.ConnectionClosedHandled == false);

                if (connection.IsFullyOpened)
                {
                    LogWarning($"EOSTransportManager.Internal_OpenConnection: Already have a fully opened socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'.");
                    return true;
                }

                if (openOutgoing)
                {
                    if (connection.OpenedOutgoing)
                    {
                        LogWarning($"EOSTransportManager.Internal_OpenConnection: Already have a locally opened socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'. Now we're just awaiting a response to our connect request.");
                        return true;
                    }

                    Debug.Assert(connection.IsPendingIncoming);
                }
                else
                {
                    if (connection.OpenedIncoming)
                    {
                        LogWarning($"EOSTransportManager.Internal_OpenConnection: Already have a remotely opened socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'. Now we just need to respond to their connect request.");
                        return true;
                    }

                    Debug.Assert(connection.IsPendingOutgoing);
                }
            }

            if (openOutgoing)
            {
                var options = new AcceptConnectionOptions()
                {
                    LocalUserId = LocalUserId,
                    RemoteUserId = remoteUserId,
                    SocketId = connection.SocketId,
                };

                Result result = P2PHandle.AcceptConnection(ref options);
                if (result != Result.Success)
                {
                    LogError($"EOSTransportManager.Internal_OpenConnection: Failed to open remote peer connection - P2PInterface.AcceptConnection error result '{result}'.");

                    if (connection.IsValid == false)
                    {
                        connections.Remove(connection);
                    }
                    connection = null;

                    return false;
                }
            }

            connection.IsValid = true;

            if (openOutgoing)
            {
                connection.OpenedOutgoing = true;
                Debug.Assert(connection.IsPendingOutgoing || connection.IsFullyOpened);
            }
            else
            {
                connection.OpenedIncoming = true;
                Debug.Assert(connection.IsPendingIncoming || connection.IsFullyOpened);
            }

            if (connection.IsFullyOpened)
            {
                SendPacket(remoteUserId, connection.SocketName, ConnectionConfirmationPacket, ConnectionConfirmationChannel, true);
            }

            TryHandleConnectionOpened(remoteUserId, socketName, connection);

            return true;
        }

        public bool CloseConnection(ProductUserId remoteUserId, string socketName, bool forceClose = true)
        {
            Log($"EOSTransportManager.CloseConnection: Attempting to close (cancel or reject) a socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'...");

            if (IsInitialized == false)
            {
                LogError("EOSTransportManager.CloseConnection: Failed to close remote peer connection - EOSTransportManager is uninitialized (has OnLoggedIn been called?).");
                return false;
            }

            if (remoteUserId == null)
            {
                LogError("EOSTransportManager.CloseConnection: Failed to close remote peer connection - remoteUserId is null.");
                return false;
            }

            bool success;

            if (Connections.TryGetValue(remoteUserId, out List<Connection> connections))
            {
                Connection connection = connections.Find(x => x.SocketName == socketName);
                if (connection != null)
                {
                    var options = new CloseConnectionOptions()
                    {
                        LocalUserId = LocalUserId,
                        RemoteUserId = remoteUserId,
                        SocketId = connection.SocketId,
                    };

                    Result result = P2PHandle?.CloseConnection(ref options) ?? Result.NetworkDisconnected;
                    if (result != Result.Success)
                    {
                        LogError($"EOSTransportManager.CloseConnection: Failed to close remote peer connection - P2PInterface.CloseConnection error result '{result}'.");
                        if (forceClose)
                        {
                            success = false;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        success = true;
                    }

                    if (forceClose)
                    {
                        connection.ConnectionOpenedHandled = true;
                    }
                    TryHandleConnectionClosed(remoteUserId, socketName, connection);

                    connection.IsValid = false;
                    connections.Remove(connection);

                    if (connections.Count <= 0)
                    {
                        Connections.Remove(remoteUserId);
                    }

                    return success;
                }
            }

            success = false;
            LogError($"EOSTransportManager.CloseConnection: Failed to close remote peer connection - Unable to find a socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'.");
            return false;
        }

        public bool CloseAllConnections(bool forceClose = true)
        {
            if (IsInitialized == false)
            {
                LogError($"EOSTransportManager.CloseAllConnections: Failed to close remote peer connections - EOSTransportManager is uninitialized (has OnLoggedIn been called?).");
                return false;
            }

            bool success = true;

            var remoteUserIdsCopy = Connections.Keys.ToList();
            foreach (var remoteUserId in remoteUserIdsCopy)
            {
                if (CloseAllConnectionsWithRemotePeer(remoteUserId, forceClose) == false)
                    success = false;
            }

            return success;
        }

        public bool CloseAllConnectionsWithSocketName(string socketName, bool forceClose = true)
        {
            if (IsInitialized == false)
            {
                LogError("EOSTransportManager.CloseAllConnectionsWithSocketName: Failed to close remote peer connections - EOSTransportManager is uninitialized (has OnLoggedIn been called?).");
                return false;
            }

            bool success = true;

            var remoteUserIdsCopy = Connections.Keys.ToList();
            foreach (var remoteUserId in remoteUserIdsCopy)
            {
                bool foundAndRemoved = Connections.TryGetValue(remoteUserId, out List<Connection> connections)
                                    && connections.Remove(new Connection(socketName));

                if (foundAndRemoved == false)
                    success = false;
            }

            return success;
        }

        public bool CloseAllConnectionsWithRemotePeer(ProductUserId remoteUserId, bool forceClose = true)
        {
            if (IsInitialized == false)
            {
                LogError("EOSTransportManager.CloseAllConnectionsWithRemotePeer: Failed to close remote peer connections - EOSTransportManager is uninitialized (has OnLoggedIn been called?).");
                return false;
            }

            bool success = true;

            if (Connections.TryGetValue(remoteUserId, out List<Connection> connections))
            {
                var connectionsCopy = new List<Connection>(connections);
                foreach (var connection in connectionsCopy)
                {
                    if (CloseConnection(remoteUserId, connection.SocketName, forceClose) == false)
                    {
                        success = false;
                    }
                }
            }

            return success;
        }

        private void TryHandleConnectionOpened(ProductUserId remoteUserId, string socketName, Connection connection)
        {
            if (connection.IsFullyOpened)
            {
                if (OnConnectionOpenedCb != null && connection.ConnectionOpenedHandled == false)
                {
                    OnConnectionOpenedCb(remoteUserId, socketName);
                }

                connection.ConnectionOpenedHandled = true;
            }
        }

        private void TryHandleConnectionClosed(ProductUserId remoteUserId, string socketName, Connection connection)
        {
            if (OnConnectionClosedCb != null && connection.ConnectionOpenedHandled == true && connection.ConnectionClosedHandled == false)
            {
                OnConnectionClosedCb(remoteUserId, socketName);
            }

            connection.ConnectionClosedHandled = true;
        }

        public void SendPacket(ProductUserId remoteUserId, string socketName, byte[] packet, byte channel = 0, bool allowDelayedDelivery = false, PacketReliability reliability = PacketReliability.ReliableOrdered)
        {
            var packetAsArraySegment = new ArraySegment<byte>(packet);
            SendPacket(remoteUserId, socketName, packetAsArraySegment, channel, allowDelayedDelivery, reliability);
        }

        public void SendPacket(ProductUserId remoteUserId, string socketName, ArraySegment<byte> packet, byte channel = 0, bool allowDelayedDelivery = false, PacketReliability reliability = PacketReliability.ReliableOrdered)
        {
            if (remoteUserId.IsValid() == false)
            {
                LogError($"EOSTransportManager.SendPacket: Invalid parameters, RemoteUserId '{LoggingUtils.Redact(remoteUserId)}' is invalid.");
                return;
            }

            if (packet.Count <= 0)
            {
                LogError("EOSTransportManager.SendPacket: Invalid parameters, packet is empty.");
                return;
            }
            if (packet.Count > (MaxPacketSize - FragmentHeaderSize) * MaxFragments)
            {
                LogError($"EOSTransportManager.SendPacket: Fragmenting packet of size {packet.Count} would require more than {MaxFragments} fragments and cannot be sent.");
                return;
            }

            Connection connection = null;
            if (!Connections.TryGetValue(remoteUserId, out List<Connection> userConnections))
            {
                LogError($"EOSTransportManager.SendPacket: Connection not found to remote user {LoggingUtils.Redact(remoteUserId)}.");
                return;
            }
            connection = userConnections.Find(x => x.SocketName == socketName);
            if (connection == null)
            {
                LogError($"EOSTransportManager.SendPacket: Connection not found on socket {socketName} to remote user {LoggingUtils.Redact(remoteUserId)}.");
                return;
            }

            int numFragments = (packet.Count / (MaxPacketSize - FragmentHeaderSize)) + 1;
            int lastPacketSize = packet.Count - ((numFragments - 1) * (MaxPacketSize - FragmentHeaderSize));

            if(lastPacketSize == 0)
            {
                --numFragments;
                lastPacketSize = MaxPacketSize-FragmentHeaderSize;
            }

            int currentOffset = 0;

            ushort OutgoingFragmentedIndex = connection.GetNextMessageIndex();
            byte[] fragmentBuffer = new byte[MaxPacketSize];
            for (ushort i = 0; i < numFragments; ++i)
            {
                var fragment = new ArraySegment<byte>(fragmentBuffer, 0, Mathf.Min((packet.Count - currentOffset) + FragmentHeaderSize, MaxPacketSize));
                fragment.Array[fragment.Offset + 0] = (byte)(OutgoingFragmentedIndex >> 8);
                fragment.Array[fragment.Offset + 1] = (byte)OutgoingFragmentedIndex;
                fragment.Array[fragment.Offset + 2] = (byte)(((i & short.MaxValue) >> 8) | (byte)(i == numFragments - 1 ? 128 : 0));
                fragment.Array[fragment.Offset + 3] = (byte)(i & short.MaxValue);

                int length = i == numFragments - 1 ? lastPacketSize : MaxPacketSize - FragmentHeaderSize;

                ArraySegment<byte> packetSegment = packet.Slice(currentOffset, length);
                packetSegment.CopyTo(fragment.Slice(FragmentHeaderSize));

                currentOffset += fragment.Count - FragmentHeaderSize;

                SocketId socketId = new SocketId()
                {
                    SocketName = socketName
                };

                SendPacketOptions options = new SendPacketOptions()
                {
                    LocalUserId = LocalUserId,
                    RemoteUserId = remoteUserId,
                    SocketId = socketId,
                    AllowDelayedDelivery = allowDelayedDelivery,
                    Channel = channel,
                    Reliability = reliability,
                    Data = fragment,
                };

                Result result = P2PHandle.SendPacket(ref options);
                if (result != Result.Success)
                {
                    LogError($"EOSTransportManager.SendPacket: Unable to send {options.Data.Count} byte packet to RemoteUserId '{LoggingUtils.Redact(options.RemoteUserId)}' - Error result, {result}.");
                    return;
                }
            }
        }

        public bool TryReceivePacket(out ProductUserId remoteUserId, out string socketName, out byte channel, out byte[] packet)
        {
            if (P2PHandle == null)
            {
                remoteUserId = null;
                socketName = null;
                channel = 0;
                packet = null;
                return false;
            }

            ReceivePacketOptions receivePacketOptions = new ReceivePacketOptions()
            {
                LocalUserId = LocalUserId,
                MaxDataSizeBytes = 4096,
                RequestedChannel = null
            };

            var getNextReceivedPacketSizeOptions = new GetNextReceivedPacketSizeOptions
            {
                LocalUserId = LocalUserId,
                RequestedChannel = null
            };
            P2PHandle.GetNextReceivedPacketSize(ref getNextReceivedPacketSizeOptions, out uint nextPacketSizeBytes);

            if (nextPacketSizeBytes == 0)
            {
                remoteUserId = null;
                socketName = null;
                channel = 0;
                packet = null;
                return false;
            }
            
            packet = new byte[nextPacketSizeBytes];
            var dataSegment = new ArraySegment<byte>(packet);

            remoteUserId = null;
            SocketId socketId = default;
            Result result = P2PHandle.ReceivePacket(ref receivePacketOptions, ref remoteUserId, ref socketId, out channel, dataSegment, out uint bytesWritten);
            socketName = socketId.SocketName;

            if (result == Result.NotFound)
            {
                remoteUserId = null;
                socketName = null;
                channel = 0;
                packet = null;
                return false;
            }

            if (packet.Length < FragmentHeaderSize)
            {
                LogError($"EOSTransportManager.TryReceivePacket: Received {packet.Length} byte packet. Should be at least {FragmentHeaderSize} bytes.");
                remoteUserId = null;
                socketName = null;
                channel = 0;
                packet = null;
                return false;
            }

            if (result != Result.Success)
            {
                LogError($"EOSTransportManager.TryReceivePacket: Error result, {result}.");
                remoteUserId = null;
                socketName = null;
                channel = 0;
                packet = null;
                return false;
            }

            if (remoteUserId.IsValid() == false)
            {
                LogError($"EOSTransportManager.TryReceivePacket: Received {packet.Length} byte packet from invalid RemoteUserId '{LoggingUtils.Redact(remoteUserId)}'.");
                remoteUserId = null;
                socketName = null;
                channel = 0;
                packet = null;
                return false;
            }

            ArraySegment<byte> header = new ArraySegment<byte>(packet, 0, FragmentHeaderSize);
            ArraySegment<byte> payload = new ArraySegment<byte>(packet, FragmentHeaderSize, packet.Length - FragmentHeaderSize);

            ushort index = (ushort)((ushort)(header.Array[0] << 8) | header.Array[1]);
            ushort fragmentInfo = (ushort)((ushort)(header.Array[2] << 8) | header.Array[3]);
            ushort fragmentPos = (ushort)(fragmentInfo & short.MaxValue);

            if (TryGetConnection(remoteUserId, socketName, out Connection connection))
            {
                if (channel == ConnectionConfirmationChannel && payload.SequenceEqual(ConnectionConfirmationPacket))
                {
                    Log($"EOSTransportManager.TryReceivePacket: Connection confirmation packet received for socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'.");

                    if (connection.IsPendingOutgoing)
                    {
                        Log($"EOSTransportManager.TryReceivePacket: Attempting to remotely open (incoming) socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'...");
                        bool success = Internal_OpenConnection(remoteUserId, socketName, false, out _);

                        Debug.Assert(success && connection.IsFullyOpened);
                    }

                    remoteUserId = null;
                    socketName = null;
                    channel = 0;
                    packet = null;
                    return false;
                }
            }
            else
            {
                LogWarning($"EOSTransportManager.TryReceivePacket: Received a {packet.Length} byte packet from unknown RemoteUserId '{LoggingUtils.Redact(remoteUserId)}', discarding packet.");
            }

            if (connection == null || connection.IsFullyOpened == false)
            {
                LogWarning($"EOSTransportManager.TryReceivePacket: Received a {packet.Length} byte packet from RemoteUserId '{LoggingUtils.Redact(remoteUserId)}', discarding packet.");

                remoteUserId = null;
                socketName = null;
                channel = 0;
                packet = null;
                return false;
            }

            if (!InProgressPackets.ContainsKey(index))
            {
                InProgressPackets[index] = new SortedList<ushort, byte[]>();
            }
            InProgressPackets[index].Add(fragmentPos,payload.ToArray());

            if(((ushort)(fragmentInfo & (MaxFragments)) >> 15) != 1)
            {
                packet = null;
                return false;
            }

            int totalSize = 0;
            for (ushort i = 0; i < InProgressPackets[index].Count; ++i)
            {
                totalSize += InProgressPackets[index][i].Length;
            }

            byte[] finalPacket = new byte[totalSize];
            int offset = 0;
            for(ushort i = 0; i < InProgressPackets[index].Count; ++i)
            {
                Array.Copy(InProgressPackets[index][i], 0, finalPacket, offset, InProgressPackets[index][i].Length);
                offset += InProgressPackets[index][i].Length;
            }
            packet = finalPacket;

            InProgressPackets[index].Clear();
            InProgressPackets.Remove(index);
            
            Log($"EOSTransportManager.TryReceivePacket: Successfully received {packet.Length} byte packet from RemoteUserId '{LoggingUtils.Redact(remoteUserId)}'.");
            return true;
        }

        private ulong ConnectionRequestNotificationsId = 0;

        private void SubscribeToConnectionRequestNotifications()
        {
            AddNotifyPeerConnectionRequestOptions options = new AddNotifyPeerConnectionRequestOptions()
            {
                LocalUserId = LocalUserId,
                SocketId = null,
            };

            ConnectionRequestNotificationsId = P2PHandle.AddNotifyPeerConnectionRequest(ref options, null, OnConnectionRequestNotification);
        }

        private void UnsubscribeFromConnectionRequestNotifications()
        {
            P2PHandle?.RemoveNotifyPeerConnectionRequest(ConnectionRequestNotificationsId);
        }

        private void OnConnectionRequestNotification(ref OnIncomingConnectionRequestInfo data)
        {
            Debug.Assert(data.LocalUserId == LocalUserId);

            var socketName = data.SocketId?.SocketName;
            var remoteUserId = data.RemoteUserId;

            Log($"EOSTransportManager.OnConnectionRequestNotification: Attempting to remotely open (incoming) socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'...");
            bool success = Internal_OpenConnection(remoteUserId, socketName, false, out Connection connection);

            if (success && connection.IsPendingIncoming)
            {
                if (OnIncomingConnectionRequestedCb != null)
                {
                    OnIncomingConnectionRequestedCb(remoteUserId, socketName);
                }
            }
            else
            {
                LogError($"EOSTransportManager.OnConnectionRequestNotification: Failed to process connection request notification for socket connection named '{socketName}' with remote peer '{LoggingUtils.Redact(remoteUserId)}'...");
            }
        }

        private ulong ConnectionClosedNotificationsId = 0;

        private void SubscribeToConnectionClosedNotifications()
        {
            AddNotifyPeerConnectionClosedOptions options = new AddNotifyPeerConnectionClosedOptions()
            {
                LocalUserId = LocalUserId,
                SocketId = null,
            };

            ConnectionClosedNotificationsId = P2PHandle.AddNotifyPeerConnectionClosed(ref options, null, OnConnectionClosedNotification);
        }

        private void UnsubscribeFromConnectionClosedNotifications()
        {
            P2PHandle?.RemoveNotifyPeerConnectionClosed(ConnectionClosedNotificationsId);
        }

        private void OnConnectionClosedNotification(ref OnRemoteConnectionClosedInfo data)
        {
            Debug.Assert(data.LocalUserId == LocalUserId);

            var socketName = data.SocketId?.SocketName;
            var remoteUserId = data.RemoteUserId;

            CloseConnection(remoteUserId, socketName, true);
        }

        private void SubscribeToConnectionEstablishedNotifications()
        {
            var options = new AddNotifyPeerConnectionEstablishedOptions()
            {
                LocalUserId = LocalUserId,
                SocketId = null,
            };

            ConnectionEstablishedNotificationsId = P2PHandle.AddNotifyPeerConnectionEstablished(ref options, null, OnConnectionEstablishedNotification);
        }

        private void UnsubscribeFromConnectionEstablishedNotifications()
        {
            if (ConnectionEstablishedNotificationsId != 0)
            {
                P2PHandle?.RemoveNotifyPeerConnectionEstablished(ConnectionEstablishedNotificationsId);
                ConnectionEstablishedNotificationsId = 0;
            }
        }

        private void OnConnectionEstablishedNotification(ref OnPeerConnectionEstablishedInfo data)
        {
            Debug.Assert(data.LocalUserId == LocalUserId);
            Log($"EOSTransportManager.OnConnectionEstablishedNotification: Connection established with remote peer '{LoggingUtils.Redact(data.RemoteUserId)}' " +
                $"on socket '{data.SocketId?.SocketName}' | type={data.ConnectionType} network={data.NetworkType}");
        }

        private void SubscribeToConnectionInterruptedNotifications()
        {
            var options = new AddNotifyPeerConnectionInterruptedOptions()
            {
                LocalUserId = LocalUserId,
                SocketId = null,
            };

            ConnectionInterruptedNotificationsId = P2PHandle.AddNotifyPeerConnectionInterrupted(ref options, null, OnConnectionInterruptedNotification);
        }

        private void UnsubscribeFromConnectionInterruptedNotifications()
        {
            if (ConnectionInterruptedNotificationsId != 0)
            {
                P2PHandle?.RemoveNotifyPeerConnectionInterrupted(ConnectionInterruptedNotificationsId);
                ConnectionInterruptedNotificationsId = 0;
            }
        }

        private void OnConnectionInterruptedNotification(ref OnPeerConnectionInterruptedInfo data)
        {
            Debug.Assert(data.LocalUserId == LocalUserId);
            LogWarning($"EOSTransportManager.OnConnectionInterruptedNotification: Connection interrupted with remote peer '{LoggingUtils.Redact(data.RemoteUserId)}' " +
                       $"on socket '{data.SocketId?.SocketName}' - EOS will attempt auto-recovery");
        }

        public bool StartHost()
        {
            return NetworkManager.Singleton.StartHost();
        }

        public bool StartServer()
        {
            return NetworkManager.Singleton.StartServer();
        }

        public bool StartClient()
        {
            return NetworkManager.Singleton.StartClient();
        }

        public void Disconnect(bool discardMessageQueue = false)
        {
            NetworkManager.Singleton?.Shutdown(discardMessageQueue);
        }
    }
}