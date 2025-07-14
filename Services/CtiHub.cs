using Microsoft.AspNetCore.SignalR;

namespace ServerCRM.Services
{
    public class CtiHub : Hub
    {
        public async Task SendStatus(string message)
        {
            await Clients.All.SendAsync("ReceiveStatus", message);
        }
    }
}
