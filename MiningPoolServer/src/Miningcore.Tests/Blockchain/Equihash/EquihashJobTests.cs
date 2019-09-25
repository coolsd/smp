using System;
using MPS.Blockchain.Bitcoin;
using MPS.Blockchain.Bitcoin.DaemonResponses;
using MPS.Blockchain.Equihash;
using MPS.Blockchain.Equihash.DaemonResponses;
using MPS.Configuration;
using MPS.Crypto;
using MPS.Crypto.Hashing.Algorithms;
using MPS.Crypto.Hashing.Equihash;
using MPS.Tests.Util;
using NBitcoin;
using NBitcoin.Zcash;
using Xunit;

namespace MPS.Tests.Blockchain.Equihash
{
    public class EquihashJobTests : TestBase
    {
        public EquihashJobTests()
        {
            ZcashNetworks.Instance.EnsureRegistered();

            poolConfig = new PoolConfig
            {
                Coin = "zcash",
                Template = ModuleInitializer.CoinTemplates["zcash"]
            };
        }

        readonly PoolConfig poolConfig;

        readonly ClusterConfig clusterConfig = new ClusterConfig();
        private readonly IDestination poolAddressDestination = BitcoinUtils.AddressToDestination("tmUEUSYYGQY3G5KMNkxAqkYYNfstaCsRCM5", ZcashNetworks.Instance.Mainnet);

        protected readonly IHashAlgorithm sha256d = new Sha256D();
        protected readonly IHashAlgorithm sha256dReverse = new DigestReverser(new Sha256D());

        [Fact]
        public void ZCashUtils_EncodeTarget()
        {
            var equihashCoin = poolConfig.Template.As<EquihashCoinTemplate>();
            var chainConfig = equihashCoin.GetNetwork(ZcashNetworks.Instance.Mainnet.NetworkType);

            var result = EquihashUtils.EncodeTarget(0.5, chainConfig);
            Assert.Equal(result, "0010102040810204081020408102040810204081020408102040810204080fe0");

            result = EquihashUtils.EncodeTarget(10000, chainConfig);
            Assert.Equal(result, "000000346dc5d63886594af4f0d844d013a92a305532617c1bda5119ce075e7a");
        }

        [Fact]
        public void ZCashJob_Testnet_Validate_FoundersRewardAddress_At_Height()
        {
            var job = new EquihashJob();

            var bt = new EquihashBlockTemplate
            {
                Target = "0000407f43000000000000000000000000000000000000000000000000000000",
                PreviousBlockhash = "000003be5873fc64b1b784318e3226a1ab2a1805bebba5a0d670be54ff7772e8",
                Bits = "003355",
                Transactions = new BitcoinBlockTransaction[0],
                Subsidy = new ZCashBlockSubsidy
                {
                    Founders = 2.5m,
                    Miner = 10m,
                }
            };

            var clock = new MockMasterClock { CurrentTime = DateTimeOffset.FromUnixTimeSeconds(1508869874).UtcDateTime };

            var equihashCoin = poolConfig.Template.As<EquihashCoinTemplate>();
            var chainConfig = equihashCoin.GetNetwork(ZcashNetworks.Instance.Mainnet.NetworkType);
            var solver = EquihashSolverFactory.GetSolver(ModuleInitializer.Container, chainConfig.Solver);

            job.Init(bt, "1", poolConfig, clusterConfig, clock, poolAddressDestination, ZcashNetworks.Instance.Testnet, solver);

            bt.Height = 1;
            Assert.Equal(job.GetFoundersRewardAddress(), "t2UNzUUx8mWBCRYPRezvA363EYXyEpHokyi");
            bt.Height = 53126;
            Assert.Equal(job.GetFoundersRewardAddress(), "t2NGQjYMQhFndDHguvUw4wZdNdsssA6K7x2");
            bt.Height = 53127;
            Assert.Equal(job.GetFoundersRewardAddress(), "t2ENg7hHVqqs9JwU5cgjvSbxnT2a9USNfhy");
        }
    }
}
