using System;
using System.Collections.Generic;
using System.Text;
using MPS.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MPS.Blockchain.Equihash.DaemonResponses
{
    public class ZCashAsyncOperationStatus
    {
        [JsonProperty("id")]
        public string OperationId { get; set; }

        public string Status { get; set; }
        public JToken Result { get; set; }
        public JsonRpcException Error { get; set; }
    }
}
