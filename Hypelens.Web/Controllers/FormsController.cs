using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Hypelens.Web.Controllers
{
    public class FormsController : Controller
    {
        public IActionResult FormElements()
        {
            return View();
        }

        public IActionResult FormPlugins()
        {
            return View();
        }

        public IActionResult Wizards()
        {
            return View();
        }
    }
}
