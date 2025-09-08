using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ServerCRM.Models;
using ServerCRM.Models.Freeswitch;
using ServerCRM.Models.InfoPage;
using ServerCRM.Models.LogIn;
using ServerCRM.Services;
using System.Data.SqlClient;

namespace ServerCRM.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InfoPageController : ControllerBase
    {
        private readonly ApiService _apiService;
       

        public InfoPageController(ApiService apiService, AuthService authService)
        {
            _apiService = apiService;
           
        }
        private  string connStr = "Data Source=192.168.0.57;Initial Catalog=Test;User ID=opodba;Password=opo@1234;";
        [HttpPost("ProcessName")]
        public async Task<IActionResult> GetProcess([FromBody] AgentStatusDto request)
        {
            CL_AgentDet agent = await _apiService.GetAgentDetailsAsync(request.Status);
            if (agent == null)
                return NotFound("Agent not found");

          

            return Ok(new { message = "ProcessName", processname = agent.ProcessName });

        }
        [HttpGet("processes")]
        public IActionResult GetProcesses()
        {
            var processes = new List<string>();
            using (var con = new SqlConnection(connStr))
            {
                string query = "SELECT DISTINCT ProcessName FROM Input_Master1 ORDER BY ProcessName";
                SqlCommand cmd = new SqlCommand(query, con);
                con.Open();
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    processes.Add(reader["ProcessName"].ToString());
                }
            }
            return Ok(processes);
        }

    
        [HttpGet("displayfields/{processName}")]
        public IActionResult GetDisplayFields(string processName)
        {
            var result = new List<object>();
            using (SqlConnection con = new SqlConnection(connStr))
            {
                string query = @"SELECT Label, FieldName, Type, Options, IsRequired 
                                 FROM Input_Master1 
                                 WHERE ProcessName = @ProcessName AND GroupName = 'Display' 
                                 ORDER BY [Order]";
                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@ProcessName", processName);
                con.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    result.Add(new
                    {
                        Label = reader["Label"].ToString(),
                        FieldName = reader["FieldName"].ToString(),
                        Type = reader["Type"].ToString(),
                        Options = reader["Options"]?.ToString(),
                        IsRequired = Convert.ToBoolean(reader["IsRequired"])
                    });
                }
            }
            return Ok(result);
        }


        [HttpGet("subdispositions/{disposition}")]
        public IActionResult GetSubDispositions(string disposition)
        {
            var subs = new List<string>();
            switch (disposition)
            {
                case "Interested":
                    subs.Add("Wants Demo");
                    subs.Add("Needs Info");
                    break;
                case "Not Interested":
                    subs.Add("Not Required");
                    subs.Add("Price Issue");
                    break;
                case "Callback":
                    subs.Add("Busy");
                    subs.Add("Call Later");
                    break;
            }
            return Ok(subs);
        }

     
        [HttpGet("history")]
        public IActionResult GetHistory()
        {
            var history = new List<object>
            {
                new { Disposition = "Interested", SubDisposition = "Wants Demo", Callback = "2025-07-22 15:00", Remarks = "Client requested a demo." },
                new { Disposition = "Callback", SubDisposition = "Busy", Callback = "2025-07-21 10:00", Remarks = "Will call later." }
            };
            return Ok(history);
        }

     
        [HttpPost("submit")]
        public IActionResult Submit([FromBody] CaptureRequest request)
        {
          
            return Ok(new { message = "Saved successfully", request });
        }
    }
}
