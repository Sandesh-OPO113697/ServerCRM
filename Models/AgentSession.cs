using Genesyslab.Platform.Voice.Protocols;

namespace ServerCRM.Models
{
    public class AgentSession
    {
        public string AgentId { get; set; }
        public string DN { get; set; }
        public TServerProtocol TServerProtocol { get; set; }
        public bool IsRunning { get; set; }
        public ConnectionId? ConnID { get; set; }
        public ConnectionId? IVRConnID { get; set; }

        public string? ConforenceNumber { get; set; }
        public string? CampaignPhone { get; set; }
        public string? CampaignMode { get; set; }
        public string? CampaignName { get; set; }
        public int? ocsApplicationID { get; set; }
        public  int requestID = 1;

        public string? HoldMusic_Path { get; set; }
        public bool? isbreak { get; set; }
        public string? MasterPhone { get; set; }
        public string? Prifix { get; set; }
        public  double MyCode = 0;
        public bool isOnCall { get; set; }
        public bool isMarge { get; set; }
        public bool isConforence { get; set; }
        public int CurrentStatusID { get; set; }
        public  string? DialAccess { get; set; }
        public object LockObj { get; } = new object();
    }
}
