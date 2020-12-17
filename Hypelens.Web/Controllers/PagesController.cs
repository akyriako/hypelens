using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Hypelens.Web.Controllers
{
    public class PagesController : Controller
    {
        public IActionResult Gallery()
        {
            return View();
        }

        public IActionResult SearchResults()
        {
            return View();
        }

        public IActionResult ComingSoonPage()
        {
            return View();
        }

        public IActionResult ErrorPage()
        {
            return View();
        }

        public IActionResult Login()
        {
            return View();
        }

        public IActionResult Register()
        {
            return View();
        }
    }
}
