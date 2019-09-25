using System;
using System.Linq;
using System.Threading;
using MPS.Extensions;

namespace MPS.Blockchain.Equihash
{
    public class EquihashExtraNonceProvider : ExtraNonceProviderBase
    {
        public EquihashExtraNonceProvider() : base(3)
        {
        }
    }
}
