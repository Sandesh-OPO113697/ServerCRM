using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerCRM.Models;
using ServerCRM.Services;
using System.Numerics;

namespace ServerCRM.Controllers
{
    public class CTIController : Controller
    {
        private readonly ApiService _apiService;

        public CTIController(ApiService apiService)
        {
            _apiService = apiService;
        }

        [HttpPost]
        public async Task<IActionResult> dialer( string empCode)
        {
            if (string.IsNullOrEmpty(empCode))
                return View();
            var agent = await _apiService.GetAgentDetailsAsync(empCode);
            if (agent == null)
            {
                ViewBag.Message = "❌ Agent not found.";
                return View();
            }
            HttpContext.Session.SetString("login_code", Convert.ToString( agent.login_code));
            HttpContext.Session.SetString("dn", agent.dn ?? "");

            HttpContext.Session.SetString("Prefix", agent.Prefix ?? "");

            string error;
            bool success = CTIConnectionManager.LoginAgent(
                agent.login_code.ToString(), agent.dn, agent.TserverIP_OFFICE, agent.TserverPort, out error
            );

       
            return View();
        }

        [HttpPost]
        public IActionResult MakeCall(string phone)
        {
            string dn = HttpContext.Session.GetString("dn");
            string login_code = HttpContext.Session.GetString("login_code");
            string rawPrefix = HttpContext.Session.GetString("Prefix");
            string prefix = "";
            if (!string.IsNullOrWhiteSpace(rawPrefix))
            {
                var parts = rawPrefix.TrimEnd('\\').Split(',');
                prefix = parts[0].Trim();
            }
            if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(dn) || string.IsNullOrWhiteSpace(login_code) || string.IsNullOrWhiteSpace(prefix))
            {
                ViewBag.Message = "❌ Invalid session or input data.";
                return View("dialer");
            }
            CTIConnectionManager.MakeCall(dn, login_code, prefix + phone);
            return View("dialer");
        }

        [HttpPost]
        public IActionResult Hold()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.Hold(login_code);
            return View("dialer");
        }

      
        [HttpPost]
        public IActionResult Unhold()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.Unhold(login_code);
            return View("dialer");
        }

      

        [HttpPost]
        public IActionResult Merge()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.MergeConference(login_code);
            return View("dialer");
        }
        [HttpPost]
        public IActionResult Party()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.PartyDelete(login_code);
            return View("dialer");
        }

        [HttpPost]
        public IActionResult AgnetReady()
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.AgentReady(login_code);
            return View("dialer");
        }

        [HttpPost]
        public IActionResult Break(int reasonCode)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            CTIConnectionManager.AgentBreak(login_code , reasonCode.ToString());
            return View("dialer");
        }

        [HttpPost]
        public IActionResult Conference(string number)
        {
            string login_code = HttpContext.Session.GetString("login_code");
            string rawPrefix = HttpContext.Session.GetString("Prefix");
            string prefix = "";
            if (!string.IsNullOrWhiteSpace(rawPrefix))
            {
                var parts = rawPrefix.TrimEnd('\\').Split(',');
                prefix = parts[0].Trim();
            }
            CTIConnectionManager.Conference(login_code , prefix+ number);
            return View("dialer");
        }

        [HttpPost]
        public IActionResult Disconnect()
        {
            string login_code = HttpContext.Session.GetString("login_code");
                 CTIConnectionManager.Disconnect( login_code);
            return View("dialer");
        }
    }
}
