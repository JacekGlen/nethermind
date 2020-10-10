//  Copyright (c) 2018 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Find;
using Nethermind.Blockchain.Processing;
using Nethermind.Blockchain.Producers;
using Nethermind.Blockchain.Receipts;
using Nethermind.Blockchain.Rewards;
using Nethermind.Blockchain.Validators;
using Nethermind.Consensus;
using Nethermind.Consensus.Transactions;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.Crypto;
using Nethermind.Db;
using Nethermind.Int256;
using Nethermind.Evm;
using Nethermind.Logging;
using Nethermind.Serialization.Json;
using Nethermind.Specs;
using Nethermind.State;
using Nethermind.State.Repositories;
using Nethermind.Db.Blooms;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;
using Nethermind.TxPool;
using Nethermind.TxPool.Storages;
using BlockTree = Nethermind.Blockchain.BlockTree;

namespace Nethermind.Core.Test.Blockchain
{
    public class TestBlockchain : IDisposable
    {
        private readonly SealEngineType _sealEngineType;
        public IStateReader StateReader { get; private set; }
        public IEthereumEcdsa EthereumEcdsa { get; private set; }
        public TransactionProcessor TxProcessor { get; set; }
        public IStorageProvider Storage { get; set; }
        public IReceiptStorage ReceiptStorage { get; set; }
        public ITxPool TxPool { get; set; }
        public IDb CodeDb => DbProvider.CodeDb;
        public IBlockProcessor BlockProcessor { get; set; }
        public IBlockTree BlockTree { get; set; }
        public IBlockFinder BlockFinder { get; set; }
        public IJsonSerializer JsonSerializer { get; set; }
        public IStateProvider State { get; set; }
        public ISnapshotableDb StateDb => DbProvider.StateDb;
        public TrieStore TrieStore { get; set; }
        public TestBlockProducer BlockProducer { get; private set; }
        public MemDbProvider DbProvider { get; set; }
        public ISpecProvider SpecProvider { get; set; }

        protected TestBlockchain(SealEngineType sealEngineType)
        {
            _sealEngineType = sealEngineType;
        }

        public static Address AccountA = TestItem.AddressA;
        public static Address AccountB = TestItem.AddressB;
        public static Address AccountC = TestItem.AddressC;
        private SemaphoreSlim _resetEvent;
        private AutoResetEvent _oneAtATime = new AutoResetEvent(true);

        public ManualTimestamper Timestamper { get; private set; }

        public static TransactionBuilder<Transaction> BuildSimpleTransaction => Core.Test.Builders.Build.A.Transaction.SignedAndResolved(TestItem.PrivateKeyA).To(AccountB);

        protected virtual async Task<TestBlockchain> Build(ISpecProvider specProvider = null, UInt256? initialValues = null)
        {
            Timestamper = new ManualTimestamper(new DateTime(2020, 2, 15, 12, 50, 30, DateTimeKind.Utc));
            JsonSerializer = new EthereumJsonSerializer();
            SpecProvider = specProvider ?? MainnetSpecProvider.Instance;
            EthereumEcdsa = new EthereumEcdsa(ChainId.Mainnet, LimboLogs.Instance);
            ITxStorage txStorage = new InMemoryTxStorage();
            DbProvider = new MemDbProvider();
            TrieStore = new TrieStore(StateDb.Innermost, LimboLogs.Instance);
            State = new StateProvider(TrieStore, DbProvider.CodeDb, LimboLogs.Instance);
            State.CreateAccount(TestItem.AddressA, (initialValues ?? 1000.Ether()));
            State.CreateAccount(TestItem.AddressB, (initialValues ?? 1000.Ether()));
            State.CreateAccount(TestItem.AddressC, (initialValues ?? 1000.Ether()));
            byte[] code = Bytes.FromHexString("0xabcd");
            Keccak codeHash = Keccak.Compute(code);
            State.UpdateCode(code);
            State.UpdateCodeHash(TestItem.AddressA, codeHash, SpecProvider.GenesisSpec);

            Storage = new StorageProvider(TrieStore, State, LimboLogs.Instance);
            Storage.Set(new StorageCell(TestItem.AddressA, UInt256.One), Bytes.FromHexString("0xabcdef"));
            Storage.Commit();

            State.Commit(SpecProvider.GenesisSpec);
            State.CommitTree(0);

            TxPool = new TxPool.TxPool(
                txStorage,
                EthereumEcdsa,
                SpecProvider,
                new TxPoolConfig(),
                State,
                LimboLogs.Instance);

            IDb blockDb = new MemDb();
            IDb headerDb = new MemDb();
            IDb blockInfoDb = new MemDb();
            BlockTree = new BlockTree(blockDb, headerDb, blockInfoDb, new ChainLevelInfoRepository(blockDb), SpecProvider, TxPool, NullBloomStorage.Instance, LimboLogs.Instance);

            ReceiptStorage = new InMemoryReceiptStorage();
            VirtualMachine virtualMachine = new VirtualMachine(State, Storage, new BlockhashProvider(BlockTree, LimboLogs.Instance), SpecProvider, LimboLogs.Instance);
            TxProcessor = new TransactionProcessor(SpecProvider, State, Storage, virtualMachine, LimboLogs.Instance);
            BlockProcessor = CreateBlockProcessor();

            BlockchainProcessor chainProcessor = new BlockchainProcessor(BlockTree, BlockProcessor, new TxSignaturesRecoveryStep(EthereumEcdsa, TxPool, SpecProvider, LimboLogs.Instance), LimboLogs.Instance, BlockchainProcessor.Options.Default);
            chainProcessor.Start();
            
            StateReader = new StateReader(new ReadOnlyTrieStore(TrieStore), CodeDb, LimboLogs.Instance);
            TxPoolTxSource txPoolTxSource = CreateTxPoolTxSource();
            ISealer sealer = new NethDevSealEngine(TestItem.AddressD);
            IStateProvider producerStateProvider = new StateProvider(new ReadOnlyTrieStore(TrieStore), CodeDb, LimboLogs.Instance);
            BlockProducer = new TestBlockProducer(txPoolTxSource, chainProcessor, producerStateProvider, sealer, BlockTree, chainProcessor, Timestamper, LimboLogs.Instance);
            BlockProducer.Start();

            _resetEvent = new SemaphoreSlim(0);
            BlockTree.NewHeadBlock += (s, e) =>
            {
                _resetEvent.Release(1);
            };

            var genesis = GetGenesisBlock();
            BlockTree.SuggestBlock(genesis);
            if (!await _resetEvent.WaitAsync(1000))
            {
                throw new InvalidOperationException("Failed to process genesis in 1s.");
            }

            await AddBlocksOnStart();
            return this;
        }

        protected virtual TxPoolTxSource CreateTxPoolTxSource()
        {
            return new TxPoolTxSource(TxPool, StateReader, LimboLogs.Instance);
        }

        protected virtual Block GetGenesisBlock()
        {
            var genesisBlockBuilder = Core.Test.Builders.Build.A.Block.Genesis.WithStateRoot(State.StateRoot);
            if (_sealEngineType == SealEngineType.AuRa)
            {
                genesisBlockBuilder.WithAura(0, new byte[65]);
            }

            Block genesis = genesisBlockBuilder.TestObject;
            return genesis;
        }

        protected virtual async Task AddBlocksOnStart()
        {
            await AddBlock();
            await AddBlock(BuildSimpleTransaction.WithNonce(0).TestObject);
            await AddBlock(BuildSimpleTransaction.WithNonce(1).TestObject, BuildSimpleTransaction.WithNonce(2).TestObject);
        }

        protected virtual BlockProcessor CreateBlockProcessor() =>
            new BlockProcessor(SpecProvider, Always.Valid, new RewardCalculator(SpecProvider), TxProcessor, State, Storage, TxPool, ReceiptStorage, LimboLogs.Instance);

        public async Task AddBlock(params Transaction[] transactions)
        {
            await _oneAtATime.WaitOneAsync(CancellationToken.None);
            foreach (Transaction transaction in transactions)
            {
                TxPool.AddTransaction(transaction, TxHandlingOptions.None);
            }

            Timestamper.Add(TimeSpan.FromSeconds(1));
            BlockProducer.BuildNewBlock();

            await _resetEvent.WaitAsync(CancellationToken.None);
            _oneAtATime.Set();
        }

        public void AddTransaction(Transaction testObject)
        {
            TxPool.AddTransaction(testObject, TxHandlingOptions.None);
        }

        public void Dispose()
        {
            BlockProducer?.StopAsync();
            CodeDb?.Dispose();
            StateDb?.Dispose();
        }

        /// <summary>
        /// Creates a simple transfer transaction with value defined by <paramref name="ether"/>
        /// from a rich account to <paramref name="address"/>
        /// </summary>
        /// <param name="address">Address to add funds to</param>
        /// <param name="ether">Value of ether to add to the account</param>
        /// <returns></returns>
        public async Task AddFunds(Address address, UInt256 ether)
        {
            var nonce = StateReader.GetNonce(BlockTree.Head.StateRoot, TestItem.AddressA);
            Transaction tx = Builders.Build.A.Transaction
                .SignedAndResolved(TestItem.PrivateKeyA)
                .To(address)
                .WithNonce(nonce)
                .WithValue(ether)
                .TestObject;

            await AddBlock(tx);
        }
    }
}
