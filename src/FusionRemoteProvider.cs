using AnotherECS.ArrayPool;
using AnotherECS.Core;
using AnotherECS.Core.Remote;
using Fusion;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AnotherECS.Remote.Fusion
{
    public class FusionRemoteProvider : MonoBehaviour, IRemoteProvider
    {
        public StartGameArgs Args 
        {
            get => _args;
            set => _args = value;
        }

        public event ReceiveBytesHandler ReceiveBytes;
        public event ConnectPlayerHandler ConnectPlayer;
        public event DisconnectPlayerHandler DisconnectPlayer;


        private StartGameArgs _args;
        private NetworkTransfer _networkTransfer;
        private SmallArrayPoolAccuracy<Player> _playerPool;
        private Player[] _players;
        private Cache<NetworkRunner> _networkRunner;
        private Dictionary<long, Player> _playerDetails;

        private Queue<PlayerRef> _callbackBuffer;

        private ClientRole _lastRole;


        private void Awake()
        {
            _networkRunner = new Cache<NetworkRunner>(() => GetComponent<NetworkRunner>());
            _playerDetails = new Dictionary<long, Player>();
            _callbackBuffer = new Queue<PlayerRef>();
            _networkTransfer = new NetworkTransfer(this);
            _playerPool = new SmallArrayPoolAccuracy<Player>(2);
            _players = _playerPool.Empty();
        }

        public async Task<ConnectResult> Connect()
            => new ConnectResult(
                await GetRunner().StartGame(Args)
                );

        public Task Disconnect()
            => (GetRunner() != null)
                ? GetRunner().Shutdown()
                : Task.CompletedTask;

        public void Send(Player target, byte[] bytes)
        {
            foreach (var player in GetRunner().ActivePlayers)
            {
                if (player.PlayerId == target.Id)
                {
                    _networkTransfer.SendTarget(player, GetLocalPlayerRef(), bytes);
                    return;
                }
            }
        }

        public void SendOther(byte[] bytes)
        {
            _networkTransfer.SendOther(GetLocalPlayerRef(), bytes);
        }

        public double GetGlobalTime()
            => GetRunner().RemoteRenderTime;

        public Player GetLocalPlayer()
            => ConvertToPlayer(GetLocalPlayerRef());

        public Player[] GetPlayers()
            => _players;

        public double GetPing()
            => GetRunner().GetPlayerRtt(GetRunner().LocalPlayer);

        internal void OnConnectPlayer(PlayerRef player)
        {
            _callbackBuffer.Enqueue(player);

            if (player == GetLocalPlayerRef())
            {
                _lastRole = GetSelfRole();
                _networkTransfer.SendPlayerData(player, GetPlayerData());
            }
            else
            {
                _networkTransfer.SendTargetPlayerData(player, GetLocalPlayerRef(), GetPlayerData());
            }
        }

        internal void OnDisconnectPlayer(PlayerRef sender)
        {
            var player = ConvertToPlayer(sender);
            DisconnectPlayer.Invoke(player);

            lock (_playerDetails)
            {
                _playerDetails.Remove(sender.PlayerId);
            }
            RemovePlayer(player);
        }

        internal void OnReceivePlayerData(PlayerRef sender, PlayerData playerData)
        {
            var player = new Player(sender.PlayerId, sender == GetLocalPlayerRef(), playerData.role, playerData.performanceTiming);
            lock (_playerDetails)
            {
                _playerDetails[sender.PlayerId] = player;
            }
            UpdatePlayer(player);
            CallbackBufferFlush();
        }

        internal void OnReceive(PlayerRef sender, byte[] bytes)
        {
            ReceiveBytes.Invoke(ConvertToPlayer(sender), bytes);
        }

        internal NetworkRunner GetRunner()
            => _networkRunner.Get();

        private void Update()
        {
            if (_lastRole != ClientRole.Unknow)
            {
                if (_lastRole != GetSelfRole())
                {
                    _lastRole = GetSelfRole();
                    _networkTransfer.SendPlayerData(GetLocalPlayerRef(), GetPlayerData());
                }
            }
        }

        private PlayerRef GetLocalPlayerRef()
            => GetRunner().LocalPlayer;

        private Player ConvertToPlayer(PlayerRef player)
        {
            lock (_playerDetails)
            {
                return _playerDetails.TryGetValue(player.PlayerId, out var result)
                    ? result
                    : new Player(player.PlayerId, false, ClientRole.Unknow, -1);
            }
        }

        private PlayerData GetPlayerData()
            => new()
            {
                role = GetSelfRole(), 
                performanceTiming = PerformanceTester.Do()
            };

        private ClientRole GetSelfRole()
            => GetRunner().IsSharedModeMasterClient ? ClientRole.Master : ClientRole.Client;

        private void CallbackBufferFlush()
        {
            while (_callbackBuffer.Count != 0)
            {
                var playerRef = _callbackBuffer.Peek();

                bool isHasPlayer = false;
                Player player = default;
                lock (_playerDetails)
                {
                    isHasPlayer = _playerDetails.TryGetValue(playerRef.PlayerId, out player);
                }
                if (isHasPlayer)
                {
                    _callbackBuffer.Dequeue();
                    ConnectPlayer.Invoke(player);
                }
                else
                {
                    break;
                }
            }
        }

        private void UpdatePlayer(Player player)
        {
            lock (_players)
            {
                for (int i = 0; i < _players.Length; ++i)
                {
                    if (_players[i].Id == player.Id)
                    {
                        _players[i] = player;
                        return;
                    }
                }
                var players = _playerPool.Get(_players.Length + 1);
                Array.Copy(_players, players, _players.Length);
                players[^1] = player;
                _players = players;
            }
        }

        private void RemovePlayer(Player player)
        {
            int index = -1;
            lock (_players)
            {
                for (int i = 0; i < _players.Length; ++i)
                {
                    if (_players[i].Id == player.Id)
                    {
                        index = i;
                    }
                }

                if (index != -1)
                {
                    var players = _playerPool.Get(_players.Length - 1);

                    Array.Copy(_players, 0, players, 0, index);
                    Array.Copy(_players, index + 1, players, index, _players.Length - index - 1);
                    _players = players;
                }
            }
        }
    }
}
