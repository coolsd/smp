using MPS.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MPS.Blockchain.Equihash.DaemonResponses
{
    public class ZCashShieldingResponse
    {
        [JsonProperty("opid")]
        public string OperationId { get; set; }
    }
}
