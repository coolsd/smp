using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MPS.Extensions;

namespace MPS.Blockchain.Ethereum
{
    public class EthereumExtraNonceProvider : ExtraNonceProviderBase
    {
        public EthereumExtraNonceProvider() : base(2)
        {
        }
    }
}
