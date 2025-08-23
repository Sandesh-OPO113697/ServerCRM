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
            return Ok(type);  // returns just the int value
        }
    }
}
