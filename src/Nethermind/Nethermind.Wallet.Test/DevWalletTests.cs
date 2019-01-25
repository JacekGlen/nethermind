using System;
using System.IO;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Json;
using Nethermind.Core.Logging;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.KeyStore;
using Nethermind.KeyStore.Config;
using NUnit.Framework;

namespace Nethermind.Wallet.Test
{
    [TestFixture]
    public class DevWalletTests
    {
        public enum DevWalletType
        {
            KeyStore,
            Memory
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_keyStorePath))
            {
                Directory.Delete(_keyStorePath, true);
            }
        }

        private string _keyStorePath = Path.Combine(Path.GetTempPath(), "DevWalletTests_keystore");

        private IWallet SetupWallet(DevWalletType devWalletType)
        {
            switch (devWalletType)
            {
                case DevWalletType.KeyStore:
                    IKeyStoreConfig config = new KeyStoreConfig();
                    config.KeyStoreDirectory = _keyStorePath;
                    ISymmetricEncrypter encrypter = new AesEncrypter(config, LimboLogs.Instance);
                    return new DevKeyStoreWallet(
                        new FileKeyStore(config, new EthereumJsonSerializer(), encrypter, new CryptoRandom(), LimboLogs.Instance),
                        LimboLogs.Instance);
                case DevWalletType.Memory:
                    return new DevWallet(LimboLogs.Instance);
                default:
                    throw new ArgumentOutOfRangeException(nameof(devWalletType), devWalletType, null);
            }
        }

        [TestCase(DevWalletType.KeyStore)]
        [TestCase(DevWalletType.Memory)]
        public void Can_setup_wallet_twice(DevWalletType walletType)
        {
            IWallet wallet1 = SetupWallet(walletType);
            IWallet wallet2 = SetupWallet(walletType);
        }
        
        [TestCase(DevWalletType.KeyStore)]
        [TestCase(DevWalletType.Memory)]
        public void Has_10_dev_accounts(DevWalletType walletType)
        {
            IWallet wallet = SetupWallet(walletType);
            Assert.AreEqual(10, wallet.GetAccounts().Length);
        }
        
        [TestCase(DevWalletType.KeyStore)]
        [TestCase(DevWalletType.Memory)]
        public void Each_account_can_sign_with_simple_key(DevWalletType walletType)
        {
            IWallet wallet = SetupWallet(walletType);

            for (int i = 1; i <= 10; i++)
            {
                byte[] keyBytes = new byte[32];
                keyBytes[31] = (byte) i;
                PrivateKey key = new PrivateKey(keyBytes);
                Assert.AreEqual(key.Address, wallet.GetAccounts()[i - 1], $"{i}");
            }
        }

        [TestCase(DevWalletType.KeyStore)]
        [TestCase(DevWalletType.Memory)]
        public void Can_sign_on_networks_with_chain_id(DevWalletType walletType)
        {
            const int networkId = 40000;
            EthereumSigner signer = new EthereumSigner(new SingleReleaseSpecProvider(LatestRelease.Instance, networkId), NullLogManager.Instance);
            IWallet wallet = SetupWallet(walletType);

            for (int i = 1; i <= 10; i++)
            {
                Address signerAddress = wallet.GetAccounts()[0];
                Transaction tx = new Transaction();
                tx.SenderAddress = signerAddress;
                
                wallet.Sign(tx, networkId);
                Address recovered = signer.RecoverAddress(tx, networkId);
                Assert.AreEqual(signerAddress, recovered, $"{i}");
                Assert.AreEqual(networkId, tx.Signature.GetChainId, "chainId");
            }
        }
    }
}