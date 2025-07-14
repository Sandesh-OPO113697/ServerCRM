using Microsoft.AspNetCore.Mvc;

namespace ServerCRM.Controllers
{
    public class LogInController : Controller
    {
        public IActionResult logInUser()
        {
            return View();
        }
    }
}
