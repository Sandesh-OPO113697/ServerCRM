using Genesyslab.InteropServices;
using Microsoft.AspNetCore.Mvc;
using ServerCRM.FreeSwitchSer;
using ServerCRM.Models;
using ServerCRM.Models.Freeswitch;
using ServerCRM.Models.LogIn;
using ServerCRM.Services;

namespace ServerCRM.Controllers
{
    public class FreeSwitchController : Controller
    {
        private readonly FreeSwitchManager _fsManager;
        private readonly ApiService _apiService;
        private readonly string _gatewayUuid = "b7a78ca2-2234-48d2-9281-33f58dfb1e4d";

        public FreeSwitchController(FreeSwitchManager fsManager, ApiService apiService)
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

            HttpContext.Session.SetString("login_code", agent.login_code.ToString());
            HttpContext.Session.SetString("dn", agent.dn ?? "");
            HttpContext.Session.SetString("Prefix", agent.Prefix ?? "");

            string error;
          await _fsManager.GetOrCreateConnectionAsync("22");

           

            return Ok(new { message = "Agent logged in successfully", logincode = agent.login_code });

        }

        [HttpGet]
        public async Task<IActionResult> Status()
        {
            string userId = HttpContext.Session.GetString("login_code") ?? "";
            await _fsManager.GetFreeSwitchStatusAsync(userId);
            return Json(new { status = true });
        }
        [HttpGet]
        public async Task<IActionResult> MakeCall([FromBody] LoginRequest res)
        {
            var agent = await _apiService.GetAgentDetailsAsync(res.empCode);
            if (agent == null) return NotFound("Agent not found");

            HttpContext.Session.SetString("login_code", agent.login_code.ToString());
            HttpContext.Session.SetString("dn", "22" ?? "");
            HttpContext.Session.SetString("Prefix", "7530" ?? "");
            ViewBag.LoginCode = agent.login_code.ToString();
            ViewBag.DN = agent.dn ?? "";

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> onCall(string phoneNumber)
        {
            string userId = HttpContext.Session.GetString("dn") ?? "";
            string callerId = HttpContext.Session.GetString("dn") ?? "";
            string Prefix = HttpContext.Session.GetString("Prefix") ?? "";
            var uuid = await _fsManager.MakeCallAsync(userId, _gatewayUuid, callerId, Prefix + phoneNumber);

            ViewBag.Message = string.IsNullOrEmpty(uuid) ? "Call initiated (waiting for UUID via events)..." : $"Call initiated (UUID: {uuid})";
            ViewBag.LoginCode = userId;
            ViewBag.DN = callerId;

            return View("MakeCall");
        }

        [HttpPost]
        public async Task<IActionResult> HoldCall()
        {
            string loginCode = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(loginCode)) return BadRequest("No session user");

            await _fsManager.HoldCallAsync(loginCode);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> UnholdCall()
        {
            string loginCode = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(loginCode)) return BadRequest("No session user");

            await _fsManager.UnholdCallAsync(loginCode);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> HangupCall()
        {
            string loginCode = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(loginCode)) return BadRequest("No session user");

            await _fsManager.HangupCallAsync(loginCode);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> AddNumberToConference([FromBody] AddConfDto dto)
        {
            string userId = HttpContext.Session.GetString("dn") ?? "";
            string callerId = HttpContext.Session.GetString("dn") ?? "";

            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            await _fsManager.CreateConferenceWithNumberAsync(userId, dto.ConferenceName, dto.PhoneNumber, callerId, _gatewayUuid);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> RemoveFromConference([FromBody] RemoveConfDto dto)
        {
            string userId = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            //await _fsManager.RemoveFromConferenceAsync(userId, dto.ConferenceName, dto.CallUuid);
            return Ok();
        }
      
      
        [HttpPost]
        public async Task<IActionResult> MergeToConference([FromBody] MergeConfDto dto)
        {
            string userId = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            if (string.IsNullOrEmpty(dto.CallUuid) || string.IsNullOrEmpty(dto.ConferenceName))
            {
                return BadRequest("Call UUID and Conference Name are required.");
            }

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
        [HttpPost]
        public async Task<IActionResult> SetAgentStatus([FromBody] AgentStatusDto dto)
        {
            CL_AgentDet agent = await _apiService.GetAgentDetailsAsync(dto.Status);
            string userId = HttpContext.Session.GetString("dn") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");
            
            _fsManager.SetAgentStatus(userId, dto.Status);
            _fsManager.SetuserName(dto.Status);
            return Ok();
        }

    }
}