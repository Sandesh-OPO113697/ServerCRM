using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ServerCRM.Models;
using ServerCRM.Models.CTI;
using ServerCRM.Models.LogIn;
using ServerCRM.Services;

namespace ServerCRM.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GenesysController : ControllerBase
    {
        private readonly ApiService _apiService;

        public GenesysController(ApiService apiService)
        {
            _apiService = apiService;
        }

        [HttpPost("dialer")]
        public async Task<IActionResult> Dialer([FromBody] LoginRequest request)
        {
            if (string.IsNullOrEmpty(request.empCode))
                return BadRequest("empCode is required");

            CL_AgentDet agent = await _apiService.GetAgentDetailsAsync(request.empCode);

            if (agent == null)
                return NotFound("Agent not found");

            HttpContext.Session.SetString("login_code", agent.login_code.ToString());
            HttpContext.Session.SetString("dn", agent.dn ?? "");
            HttpContext.Session.SetString("Prefix", agent.Prefix ?? "");

            string error;
            bool success = CTIConnectionManager.LoginAgent(agent ,
                agent.login_code.ToString(), agent.dn, agent.TserverIP_OFFICE, agent.TserverPort, out error
            );

            if (!success)
                return StatusCode(500, "CTI login failed: " + error);

            return Ok(new { message = "Agent logged in successfully" , logincode = agent.login_code });
        }

        [HttpPost("makecall")]
        public IActionResult MakeCall([FromBody] CallRequest request)
        {
            
            string dn = HttpContext.Session.GetString("dn");
            string login_code = HttpContext.Session.GetString("login_code");
            string Prefix = HttpContext.Session.GetString("Prefix");

       

            CTIConnectionManager.MakeCall(dn, login_code, Prefix + request.Phone);
            return Ok("Call initiated");
        }

        [HttpPost("hold")]
        public IActionResult Hold()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.Hold(login_code);
            return Ok("Call held");
        }

        [HttpPost("unhold")]
        public IActionResult Unhold()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.Unhold(login_code);
            return Ok("Call unheld");
        }

        [HttpPost("merge")]
        public IActionResult Merge()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.MergeConference(login_code);
            return Ok("Conference merged");
        }

        [HttpPost("party")]
        public IActionResult Party()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.PartyDelete(login_code);
            return Ok("Party deleted");
        }

        [HttpPost("ready")]
        public IActionResult AgentReady()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.AgentReady(login_code);
            return Ok("Agent marked ready");
        }

        [HttpPost("break")]
        public IActionResult Break([FromBody] BreakRequest request)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.AgentBreak(login_code, request.ReasonCode.ToString());
            return Ok("Break requested");
        }

        [HttpPost("transfer")]
        public IActionResult Transfer([FromBody] TransferRequest request)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.transferCall(login_code, request.Route.ToString());
            return Ok("Call transferred");
        }

        [HttpPost("conference")]
        public IActionResult Conference([FromBody] ConferenceRequest request)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string prefix = HttpContext.Session.GetString("Prefix");
           
            CTIConnectionManager.Conference(login_code, prefix + request.Number);
            return Ok("Conference started");
        }

        [HttpPost("disconnect")]
        public IActionResult Disconnect()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.Disconnect(login_code);
            return Ok("Call disconnected");
        }
    }
}
