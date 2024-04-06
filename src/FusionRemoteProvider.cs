using AnotherECS.Core;
using AnotherECS.Core.Remote;
using Fusion;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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
        private Cache<Player[]> _players;
        private Cache<NetworkRunner> _networkRunner;
        private Dictionary<long, Player> _playerDetails;

        private Queue<PlayerRef> _callbackBuffer;

        private ClientRole _lastRole;


        private void Awake()
        {
            _players = new Cache<Player[]>(() => GetRunner().ActivePlayers.Select(p => ConvertToPlayer(p)).ToArray());
            _networkRunner = new Cache<NetworkRunner>(() => GetComponent<NetworkRunner>());
            _playerDetails = new Dictionary<long, Player>();
            _callbackBuffer = new Queue<PlayerRef>();
            _networkTransfer = new NetworkTransfer(this);
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
            => _players.Get();


        internal void OnConnectPlayer(PlayerRef player)
        {
            _players.Drop();
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

        internal void OnDisconnectPlayer(PlayerRef player)
        {
            _players.Drop();
            
            DisconnectPlayer.Invoke(ConvertToPlayer(player));
            _playerDetails.Remove(player.PlayerId);
        }

        internal void OnReceivePlayerData(PlayerRef sender, PlayerData playerData)
        {
            _players.Drop();
            _playerDetails[sender.PlayerId] = new Player(sender.PlayerId, sender == GetLocalPlayerRef(), playerData.role, playerData.performanceTiming);
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
            => _playerDetails.TryGetValue(player.PlayerId, out var result)
                ? result
                : new Player(player.PlayerId, false, ClientRole.Unknow, -1);

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
                var player = _callbackBuffer.Peek();
                if (_playerDetails.ContainsKey(player.PlayerId))
                {
                    _callbackBuffer.Dequeue();
                    ConnectPlayer.Invoke(ConvertToPlayer(player));
                }
                else
                {
                    break;
                }
            }
        }
    }
}
