using ServerCRM.Models;
using System.Text.Json;
using System.Text;

namespace ServerCRM.Services
{
    public class ApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public ApiService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<CL_AgentDet> GetAgentDetailsAsync(string opoId)
        {
            var client = _httpClientFactory.CreateClient();
            var requestBody = new StringContent(
                JsonSerializer.Serialize(new { Opoid = opoId }),
                Encoding.UTF8,
                "application/json"
            );

            var response = await client.PostAsync("http://192.168.0.91:8088/API/AgentDetailAPI_NewSetup/Api/AgentDetailsNew", requestBody);
            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadAsStringAsync();
            var wrappedJson = JsonSerializer.Deserialize<string>(result);
            var agents = JsonSerializer.Deserialize<List<CL_AgentDet>>(wrappedJson);
            
            return agents?.FirstOrDefault();
        }

       

    }
}
