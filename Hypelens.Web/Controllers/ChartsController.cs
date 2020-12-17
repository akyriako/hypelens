using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Hypelens.Web.Controllers
{
    public class ChartsController : Controller
    {
        public IActionResult ChartJs()
        {
            return View();
        }

        public IActionResult ApexchartsJs()
        {
            return View();
        }
    }
}
