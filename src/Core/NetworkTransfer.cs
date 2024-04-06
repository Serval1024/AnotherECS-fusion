using AnotherECS.Core.Remote;
using Fusion;
using System;

namespace AnotherECS.Remote.Fusion
{
    public struct NetworkTransfer
    {
        private static FusionRemoteProvider[] _fusionRemoteProvider = Array.Empty<FusionRemoteProvider>();
        private readonly NetworkRunner _runner;

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ReloadDomainOptimizationHack()
        {
            if (_fusionRemoteProvider != null)
            {
                Array.Clear(_fusionRemoteProvider, 0, _fusionRemoteProvider.Length);
            }
        }
#endif

        public NetworkTransfer(FusionRemoteProvider fusionRemoteProvider)
        {
            Array.Resize(ref _fusionRemoteProvider, _fusionRemoteProvider.Length + 1);   
            _fusionRemoteProvider[^1] = fusionRemoteProvider;

            _runner = fusionRemoteProvider.GetRunner();
        }

        public void SendTargetPlayerData(PlayerRef target, PlayerRef sender, PlayerData playerData)
        {
            RPC_SendTargetPlayerData(_runner, target, sender, playerData);
        }

        public void SendPlayerData(PlayerRef sender, PlayerData playerData)
        {
            RPC_SendPlayerData(_runner, sender, playerData);
        }

        public void SendOther(PlayerRef sender, byte[] bytes)
        {
            RPC_SendOther(_runner, sender, bytes);
        }

        public void SendTarget([RpcTarget] PlayerRef target, PlayerRef sender, byte[] bytes)
        {
            RPC_SendTarget(_runner, target, sender, bytes);
        }


        [Rpc(RpcSources.All, RpcTargets.All, InvokeLocal = false)]
        private static void RPC_SendTargetPlayerData(NetworkRunner runner, [RpcTarget] PlayerRef target, PlayerRef sender, PlayerData playerData)
        {
            Find(runner).OnReceivePlayerData(sender, playerData);
        }

        [Rpc(RpcSources.All, RpcTargets.All, InvokeLocal = true)]
        private static void RPC_SendPlayerData(NetworkRunner runner, PlayerRef sender, PlayerData playerData)
        {
            Find(runner).OnReceivePlayerData(sender, playerData);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, InvokeLocal = false)]
        private static void RPC_SendOther(NetworkRunner runner, PlayerRef sender, byte[] bytes)
        {
            Find(runner).OnReceive(sender, bytes);
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        private static void RPC_SendTarget(NetworkRunner runner, [RpcTarget] PlayerRef target, PlayerRef sender, byte[] bytes)
        {
            Find(runner).OnReceive(sender, bytes);
        }

        private static FusionRemoteProvider Find(NetworkRunner runner)
        {
            for (int i = 0; i < _fusionRemoteProvider.Length; ++i)
            {
                if (_fusionRemoteProvider[i].GetRunner() == runner)
                {
                    return _fusionRemoteProvider[i];
                }
            }
            throw new ArgumentException();
        }
    }

    public struct PlayerData : INetworkStruct
    {
        public ClientRole role;
        public long performanceTiming;
    }
}
