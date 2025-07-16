using Microsoft.AspNetCore.SignalR;
using ServerCRM.Models;
using System.Collections.Concurrent;

namespace ServerCRM.Services
{
    public static class AgentStatusMapper
    {
        public static readonly Dictionary<int, string> StatusMap = new()
        {
            [0] = "",
            [1] = "WAITING",
            [2] = "DIALING",
            [3] = "TALKING",
            [4] = "WRAPING",
            [5] = "TEA BREAK",
            [6] = "LUNCH BREAK",
            [7] = "TRAINING BREAK",
            [8] = "QUALITY BREAK",
            [9] = "BIO BREAK",
            [10] = "HOLD",
            [11] = "LOGOUT",
            [12] = "Emergency",
            [13] = "MANUAL DIALING",
            [14] = "Backend_Work BREAK",
            [15] = "Back_to_School BREAK",
            [16] = "CM_Feedback BREAK",
            [17] = "Dialer_NonTech_DownTime BREAK",
            [18] = "Dailer_Tech_DownTime BREAK",
            [19] = "Floor_Help BREAK",
            [20] = "Health_Activities BREAK",
            [21] = "Scheduled BREAK",
            [22] = "Team_Huddle BREAK",
            [23] = "Tech_DownTime BREAK",
            [24] = "Townhall BREAK",
            [25] = "Unwell BREAK",
            [26] = "TL Feedback BREAK",
            [27] = "Vat BREAK"
        };

        public static void UpdateAgentStatus(int statusId, AgentSession session, IHubContext<CtiHub> hubContext)
        {
            session.CurrentStatusID = statusId;
            if (hubContext != null && StatusMap.TryGetValue(statusId, out var statusLabel))
            {
                 hubContext.Clients.Group(session.AgentId).SendAsync("ReceiveStatus", statusLabel);
            }
        }
    }


}
