﻿//  Copyright (c) 2021 Demerzel Solutions Limited
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
// 

using System.Collections.Generic;
using Nethermind.Consensus.Transactions;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.TxPool;

namespace Nethermind.Blockchain.Comparers
{
    public class TransactionComparerProvider : ITransactionComparerProvider
    {
        private readonly ISpecProvider _specProvider;
        private readonly IBlockTree _blockTree;
        private readonly IGasPriceTxComparerByBaseFee _gasPriceTxComparerByBaseFee;
        private IComparer<Transaction>? _defaultComparer = null;

        public TransactionComparerProvider(ISpecProvider specProvider, IBlockTree blockTree)
        {
            _specProvider = specProvider;
            _blockTree = blockTree;
            _gasPriceTxComparerByBaseFee = new GasPriceTxComparerByBaseFee(specProvider);

        }

        public IComparer<Transaction> GetDefaultComparer()
        {
            if (_defaultComparer == null)
            {
                IComparer<Transaction> gasPriceComparer = new GasPriceTxComparer(_blockTree, _gasPriceTxComparerByBaseFee);
                _defaultComparer = gasPriceComparer
                    .ThenBy(CompareTxByTimestamp.Instance)
                    .ThenBy(CompareTxByPoolIndex.Instance)
                    .ThenBy(CompareTxByGasLimit.Instance);
            }

            return _defaultComparer;
        }

        public IComparer<Transaction> GetDefaultProducerComparer(IPreparingBlockContext preparingBlockContext)
        {
            IComparer<Transaction> gasPriceComparer =
                new GasPriceTxComparerForProducers(preparingBlockContext, _gasPriceTxComparerByBaseFee);
            return gasPriceComparer
                .ThenBy(CompareTxByTimestamp.Instance)
                .ThenBy(CompareTxByPoolIndex.Instance)
                .ThenBy(CompareTxByGasLimit.Instance);
        }
    }
}
