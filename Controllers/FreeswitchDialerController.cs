using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ServerCRM.FreeSwitchSer;
using ServerCRM.Models.LogIn;
using ServerCRM.Models;
using ServerCRM.Services;
using Genesyslab.InteropServices;
using ServerCRM.Models.Freeswitch;
using ServerCRM.Models.CTI;

namespace ServerCRM.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FreeswitchDialerController : ControllerBase
    {
        private readonly FreeSwitchManager _fsManager;
        private readonly ApiService _apiService;
        private readonly string _gatewayUuid = "b7a78ca2-2234-48d2-9281-33f58dfb1e4d";

        public FreeswitchDialerController(FreeSwitchManager fsManager, ApiService apiService)
        {
            _fsManager = fsManager;
            _apiService = apiService;
        }

        [HttpPost("dialer")]
        public async Task<IActionResult> Dialer([FromBody] LoginRequest request)
        {
            CL_AgentDet agent = await _apiService.GetAgentDetailsAsync(request.empCode);
            if (agent == null)
                return NotFound("Agent not found");
            HttpContext.Session.SetString("empcode", request.empCode);
            HttpContext.Session.SetString("username", agent.user_name);
            HttpContext.Session.SetString("login_code", agent.login_code.ToString());
            HttpContext.Session.SetString("dn", "22" ?? "");
            HttpContext.Session.SetString("Prefix", "7530" ?? "");
            _fsManager.SetuserName(agent.dn);

            await _fsManager.GetOrCreateConnectionAsync("22");



            return Ok(new { message = "Agent logged in successfully", logincode = agent.login_code });

        }
        [HttpPost("makecall")]
        public async Task<IActionResult> MakeCall([FromBody] CallRequest request)
        {
            string dn = HttpContext.Session.GetString("dn");
            string login_code = HttpContext.Session.GetString("login_code");
            string Prefix = HttpContext.Session.GetString("Prefix");
            string userId = HttpContext.Session.GetString("dn") ?? "";
            string callerId = HttpContext.Session.GetString("dn") ?? "";
            var uuid = await _fsManager.MakeCallAsync(userId, _gatewayUuid, callerId, Prefix + request.Phone);
            return Ok("Call initiated");

        }

        [HttpPost("SetAgentStatus")]
        public async Task<IActionResult> SetAgentStatus([FromBody] AgentStatusDto dto)
        {
            CL_AgentDet agent = await _apiService.GetAgentDetailsAsync(dto.Status);


            string userId = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(userId))
                return BadRequest("No session user");

            _fsManager.SetAgentStatus(userId, dto.Status);
            _fsManager.SetuserName(agent.user_name);
            return Ok();
        }


        [HttpPost("hold")]
        public async Task<IActionResult> Hold()
        {
            string loginCode = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(loginCode)) return BadRequest("No session user");

            await _fsManager.HoldCallAsync(loginCode);
            return Ok();
        }

        [HttpPost("unhold")]
        public async Task<IActionResult> Unhold()
        {
            string loginCode = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(loginCode)) return BadRequest("No session user");

            await _fsManager.UnholdCallAsync(loginCode);
            return Ok();
        }

      

        [HttpPost("merge")]
        public async Task<IActionResult> Merge()
        {
            string userId = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

           

            bool success = await _fsManager.MergeToConferenceAsync(userId);
            if (success)
            {
                return Ok();
            }
            else
            {
                return BadRequest("Failed to merge call to conference.");
            }
        }

        [HttpPost("party")]
        public async Task<IActionResult> Party()
        {
            string userId = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            await _fsManager.RemoveFromConferenceAsync(userId);
            return Ok();
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

        [HttpPost("LogOut")]
        public async Task<IActionResult> AgentLogOUT()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.LogOUT(login_code);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Agent log out");
            }
        }



        [HttpPost("GetNext")]
        public async Task<IActionResult> GetNext()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string returnStatus = await CTIConnectionManager.GetNextCall(login_code);
            if (returnStatus != "")
            {
                return BadRequest(returnStatus);
            }
            else
            {
                return Ok("Break requested");
            }
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
        public async Task<IActionResult> Conference([FromBody] CallRequest  dto)
        {
            string userId = HttpContext.Session.GetString("dn") ?? "";
            string Prefix = HttpContext.Session.GetString("Prefix") ?? "";

            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            await _fsManager.CreateConferenceWithNumberAsync(userId, dto.Phone, Prefix+ dto.Phone, userId, _gatewayUuid);
            return Ok();
        }

        [HttpPost("disconnect")]
        public async Task<IActionResult> Disconnect()
        {
            string loginCode = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(loginCode)) return BadRequest("No session user");

            await _fsManager.HangupCallAsync(loginCode);
            return Ok();
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetAgentStatus()
        {
            
            string loginCode = HttpContext.Session.GetString("empcode") ?? "";
            string username = HttpContext.Session.GetString("username") ?? "";
            
            string userId = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(userId))
                return BadRequest("No session user");

            _fsManager.SetAgentStatus(userId, userId);
            _fsManager.SetuserName(username);
            return Ok(new
            {
                empcode = loginCode,
                username = username,
                dn = userId,
                status = "active"
            });
        }
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitDisposition([FromBody] DispositionRequest request)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            var status = await CTIConnectionManager.DisposeCall(login_code, request.DispositionId, request.SubDispositionId);

            return Ok(new { message = status });


        }

    }
}
