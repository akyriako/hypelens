using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Hypelens.Controllers
{
    public class LayoutController : Controller
    {
        public IActionResult StarterPage()
        {
            return View();
        }

        public IActionResult FixedFooter()
        {
            return View();
        }

        public IActionResult FullHeight()
        {
            return View();
        }

        public IActionResult FullWidth()
        {
            return View();
        }

        public IActionResult BoxedLayout()
        {
            return View();
        }

        public IActionResult MinifiedSidebar()
        {
            return View();
        }
    }
}
