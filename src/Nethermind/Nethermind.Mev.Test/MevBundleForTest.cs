//  Copyright (c) 2021 Demerzel Solutions Limited
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

using Nethermind.Core;
using Nethermind.Core.Crypto;

namespace Nethermind.Mev.Test
{
    public class MevBundleForTest
    {
        public MevBundleForTest(Keccak[] txs, long? blockNumber = null, long? minTimestamp = null, long? maxTimestamp = null)
        {
            Txs = txs;
            BlockNumber = blockNumber;
            MinTimestamp = minTimestamp;
            MaxTimestamp = maxTimestamp;
        }

        public Keccak[] Txs { get; }

        public long? BlockNumber { get; set; }
        
        public long? MaxTimestamp { get; set; }
        
        public long? MinTimestamp { get; set; }
    }
}
