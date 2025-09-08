using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServerCRM.Models.Freeswitch;

namespace ServerCRM.Controllers
{
    public class LogInController : Controller
    {
        private readonly CRMSettings _crmSettings;

        public LogInController(IOptions<CRMSettings> crmSettings)
        {
            _crmSettings = crmSettings.Value;
        }
        public IActionResult logInUser()
        {
            return View();
        }
        [HttpGet("TypeOfCRM")]
        public IActionResult GetTypeOfCRM()
        {
            int type = _crmSettings.TypeOfCRM;
            return Ok(type);  
        }

        public IActionResult SIP()
        {
            return View();
        }

        public IActionResult SIP_new()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetSipConfig()
        {
        
            var response = new
            {
                USERNAME = "22",
                PASSWORD = "22",
                SIPSERVER = "172.18.16.173:5066"
            };

            return Ok(response);
        }
    }
}
