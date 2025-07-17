using Microsoft.AspNetCore.SignalR;

namespace ServerCRM.Services
{
    public class CtiHub : Hub
    {
        public async Task JoinGroup(string agentId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, agentId);
        }

        public async Task LeaveGroup(string agentId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, agentId);
        }

        public async Task SendStatus(string message)
        {
            await Clients.All.SendAsync("ReceiveStatus", message);
        }

        public async Task SendStatusAttachData(string message)
        {
            await Clients.All.SendAsync("ReceiveAttachedData", message);
        }
    }
}
