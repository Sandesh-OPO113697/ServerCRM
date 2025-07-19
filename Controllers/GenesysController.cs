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
            bool success = CTIConnectionManager.LoginAgent(agent,
                agent.login_code.ToString(), agent.dn, agent.TserverIP_OFFICE, agent.TserverPort, out error
            );

            if (!success)
                return StatusCode(500, "CTI login failed: " + error);

            return Ok(new { message = "Agent logged in successfully", logincode = agent.login_code });
        }

        [HttpPost("makecall")]
        public async Task<IActionResult> MakeCall([FromBody] CallRequest request)
        {
            string dn = HttpContext.Session.GetString("dn");
            string login_code = HttpContext.Session.GetString("login_code");
            string Prefix = HttpContext.Session.GetString("Prefix");
            string returnStatus = await CTIConnectionManager.MakeCall(dn, login_code, Prefix + request.Phone);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Call initiated");
            }
        }

        [HttpPost("hold")]
        public async Task<IActionResult> Hold()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.Hold(login_code);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Call held");
            }
        }

        [HttpPost("unhold")]
        public async Task<IActionResult> Unhold()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.Unhold(login_code);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Call unheld");
            }
        }

        [HttpPost("merge")]
        public async Task<IActionResult> Merge()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.MergeConference(login_code);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Conference merged");
            }
        }

        [HttpPost("party")]
        public async Task<IActionResult> Party()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.PartyDelete(login_code);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Party deleted");
            }
        }

        [HttpPost("ready")]
        public async Task<IActionResult> AgentReady()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.AgentReady(login_code);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Agent marked ready");
            }
        }


        [HttpPost("GetNext")]
        public IActionResult GetNext()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.GetNextCall(login_code);
            return Ok("Agent marked ready");
        }

        [HttpPost("break")]
        public async Task<IActionResult> Break([FromBody] BreakRequest request)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.AgentBreak(login_code, request.ReasonCode.ToString());
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Break requested");
            }
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] TransferRequest request)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.transferCall(login_code, request.Route.ToString());
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Call transferred");
            }
        }

        [HttpPost("conference")]
        public async Task<IActionResult> Conference([FromBody] ConferenceRequest request)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string prefix = HttpContext.Session.GetString("Prefix");
            string returnStatus = await CTIConnectionManager.Conference(login_code, prefix + request.Number);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Conference started");
            }
        }

        [HttpPost("disconnect")]
        public async Task<IActionResult> Disconnect()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.Disconnect(login_code);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Call disconnected");
            }
        }

        [HttpPost("status")]
        public async Task<IActionResult> GetAgentStatus([FromBody] LoginRequest agentId)
        {
            if (string.IsNullOrEmpty(agentId.empCode))
            {
                return BadRequest("Agent ID is required.");
            }
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.AgentReady(login_code);
            string status = $"Agent {agentId} is ready";
            return Ok(status);
        }

    }
}
