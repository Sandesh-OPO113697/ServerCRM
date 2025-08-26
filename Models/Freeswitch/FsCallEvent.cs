namespace ServerCRM.Models.Freeswitch
{
    public class FsCallEvent
    {
        public string EventName { get; set; } = "UNKNOWN";
        public string Uuid { get; set; } = "-";
        public string Status { get; set; } = "Unknown";
        public string ChannelState { get; set; } = "CS_NONE";
        public string CallDirection { get; set; } = "unknown";
        public string DestinationNumber { get; set; } = "unknown";
        public string HangupCause { get; set; } = string.Empty;
        public string Raw { get; set; } = string.Empty;
    }
}
