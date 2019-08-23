using FileManager.Controllers;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace FileManager
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }

        protected void Application_Error()
        {
            var exception = Server.GetLastError();

            if (exception is HttpAntiForgeryException)
            {
                Response.Clear();
                Server.ClearError();
                Response.Redirect("/Admin/Error/Antiforgery", true);
            }

            var httpException = exception as HttpException;
            Response.Clear();
            Server.ClearError();

            var routeData = new RouteData();
            routeData.Values["controller"] = "ErrorPage";
            routeData.Values["action"] = "Unauthorized";
            routeData.Values["exception"] = exception;

            Response.StatusCode = (int)System.Net.HttpStatusCode.InternalServerError;
            Response.TrySkipIisCustomErrors = true;
            if (httpException != null)
            {
                Response.StatusCode = httpException.GetHttpCode();

                switch (Response.StatusCode)
                {
                    case (int)System.Net.HttpStatusCode.Unauthorized:
                        routeData.Values["action"] = "Unauthorized";
                        break;

                    case (int)System.Net.HttpStatusCode.NotFound:
                        routeData.Values["action"] = "PageNotFound";
                        break;

                    default:
                        routeData.Values["action"] = "SomethingWrong";
                        break;
                }
            }
            // Avoid IIS7 getting in the middle
            Response.TrySkipIisCustomErrors = true;
            IController errorsController = new ErrorPageController();
            HttpContextWrapper wrapper = new HttpContextWrapper(Context);
            var rc = new RequestContext(wrapper, routeData);
            errorsController.Execute(rc);
        }
    }
}