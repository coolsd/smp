using MPS.Persistence.Model;

namespace MPS.Notifications.Messages
{
    public class HashrateNotification
    {
        public string PoolId { get; set; }
        public double Hashrate { get; set; }
        public string Miner { get; set; }
        public string Worker { get; set; }
    }
}
