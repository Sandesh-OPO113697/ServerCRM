using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace ServerCRM.FreeSwitchService
{
    public class CallEventsHub : Hub
    {
        public async Task JoinGroup(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            await Clients.Group(userId).SendAsync("OnFsEvent", new { status = "Waiting"  });
        }

        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();
            string userId = http?.Request.Query["userId"].FirstOrDefault() ?? Context.ConnectionId;
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var http = Context.GetHttpContext();
            string userId = http?.Request.Query["userId"].FirstOrDefault() ?? Context.ConnectionId;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
