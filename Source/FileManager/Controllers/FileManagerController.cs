using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace FileManager.Controllers
{
    public class FileManagerController : Controller
    {
        // GET: FileManager
        public ActionResult Index()
        {
            Session["UserType"] = CurrentUser.UserType;

            ViewBag.Departments = _dataService.GetAll<Department>().Select(x => new SelectListItem { Text = x.Name, Value = x.ID.ToString() }).ToList();
            ViewBag.DocumentTypeCategorys = _dataService.GetAll<DocumentType>().Where(x => x.ParentID == -1).Select(x => new SelectListItem { Text = x.Name, Value = x.ID.ToString() }).ToList();
            ViewBag.DispatchStatus = _dataService.GetAll<Definition>().Where(x => x.DefineType == (short)Enums.DbDefineType.DispatchStatus).Select(x => new SelectListItem { Text = x.Name, Value = x.Value.ToString() }).ToList();

            return View();
        }

        [ValidateInput(false)]
        public ActionResult FileManagerPartial()
        {
            var manualFilter = Session["ManualFilter"] == null ? 0 : (int)Session["ManualFilter"];
            if (manualFilter == 1)
                DocProviderService.SearchModel = (DocumentSearchViewModel)Session["ManualData"];
            else
                DocProviderService.SearchModel = null;

            return PartialView("_FileManagerPartial", DocProviderService);
        }
    }
}