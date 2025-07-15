using Genesyslab.Platform.Voice.Protocols;

namespace ServerCRM.Models
{
    public class AgentSession
    {
        public string AgentId { get; set; }
        public string DN { get; set; }
        public TServerProtocol TServerProtocol { get; set; }
        public bool IsRunning { get; set; }
        public ConnectionId ConnID { get; set; }
        public ConnectionId IVRConnID { get; set; }

        public string ConforenceNumber { get; set; }
        public bool isOnCall { get; set; }
        public bool isConforence { get; set; }
        public string CurrentStatus { get; set; }
        public object LockObj { get; } = new object();
    }
}
