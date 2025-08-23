using Microsoft.AspNetCore.Mvc;
using ServerCRM.FreeSwitchSer;
using ServerCRM.Models;
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

        [HttpGet]
        public async Task<IActionResult> MakeCall(string empcode)
        {
            var agent = await _apiService.GetAgentDetailsAsync(empcode);
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
            string userId = HttpContext.Session.GetString("login_code") ?? "";
            string callerId = HttpContext.Session.GetString("dn") ?? "";
            string Prefix = HttpContext.Session.GetString("Prefix") ?? "";
            var uuid = await _fsManager.MakeCallAsync(userId, _gatewayUuid, callerId, Prefix + phoneNumber);

            ViewBag.Message = string.IsNullOrEmpty(uuid) ? "Call initiated (waiting for UUID via events)..." : $"Call initiated (UUID: {uuid})";
            ViewBag.LoginCode = userId;
            ViewBag.DN = callerId;

            return View("MakeCall");
        }

        [HttpPost]
        public async Task<IActionResult> HoldCall([FromBody] CallRequestfreeswitch request)
        {
            string userId = HttpContext.Session.GetString("login_code") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            await _fsManager.HoldCallAsync(userId, request.Uuid);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> UnholdCall([FromBody] CallRequestfreeswitch request)
        {
            string userId = HttpContext.Session.GetString("login_code") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            await _fsManager.UnholdCallAsync(userId, request.Uuid);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> HangupCall([FromBody] CallRequestfreeswitch request)
        {
            if (string.IsNullOrEmpty(request.Uuid))
            {
                return BadRequest("No UUID provided.");
            }

            bool success = await _fsManager.HangupCallAsync(request.Uuid);
            if (success)
            {
                return Ok();
            }
            else
            {
                return BadRequest("Failed to send hangup command. It may have already ended.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddNumberToConference([FromBody] AddConfDto dto)
        {
            string userId = HttpContext.Session.GetString("login_code") ?? "";
            string callerId = HttpContext.Session.GetString("dn") ?? "";

            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            await _fsManager.CreateConferenceWithNumberAsync(userId, dto.ConferenceName, dto.PhoneNumber, callerId, _gatewayUuid);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> RemoveFromConference([FromBody] RemoveConfDto dto)
        {
            string userId = HttpContext.Session.GetString("login_code") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            await _fsManager.RemoveFromConferenceAsync(userId, dto.ConferenceName, dto.CallUuid);
            return Ok();
        }
      
      
        [HttpPost]
        public async Task<IActionResult> MergeToConference([FromBody] MergeConfDto dto)
        {
            string userId = HttpContext.Session.GetString("login_code") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            if (string.IsNullOrEmpty(dto.CallUuid) || string.IsNullOrEmpty(dto.ConferenceName))
            {
                return BadRequest("Call UUID and Conference Name are required.");
            }

            bool success = await _fsManager.MergeToConferenceAsync(userId, dto.CallUuid, dto.ConferenceName);
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
            string userId = HttpContext.Session.GetString("login_code") ?? "";
            if (string.IsNullOrEmpty(userId)) return BadRequest("No session user");

            _fsManager.SetAgentStatus(userId, dto.Status);
            return Ok();
        }

        public class AgentStatusDto { public string Status { get; set; } = ""; }
        public class AddConfDto { public string ConferenceName { get; set; } = ""; public string PhoneNumber { get; set; } = ""; }
        public class RemoveConfDto { public string ConferenceName { get; set; } = ""; public string CallUuid { get; set; } = ""; }
        public class MergeConfDto { public string CallUuid { get; set; } = ""; public string ConferenceName { get; set; } = ""; }

    }
}