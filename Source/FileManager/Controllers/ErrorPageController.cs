using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace FileManager.Controllers
{
    public class ErrorPageController : Controller
    {
        public ActionResult Unauthorized()
        {
            return View();
        }

        public ActionResult PageNotFound()
        {
            return View();
        }

        public ActionResult SomethingWentWrong()
        {
            return View();
        }
    }
}