namespace ServerCRM.Models.Freeswitch
{
    public class UserCallInfo
    {
        public string LoginCode { get; set; } 
        public string CallerId { get; set; } 
        public string Leg1Uuid { get; set; } 
        public string Leg2Uuid { get; set; } 
        public string ConfLeg1Uuid { get; set; }
        public string ConfLeg2Uuid { get; set; }
        public string Status { get; set; } 
        public string conferenceName { get; set; }
        public string conferenceNumber { get; set; }
        public List<string> ConferenceLegs { get; set; } = new List<string>();
    }
}
