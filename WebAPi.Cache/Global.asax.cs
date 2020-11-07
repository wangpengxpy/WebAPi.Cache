using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;

namespace WebAPi.Cache
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            //移除所有视图引擎
            ViewEngines.Engines.Clear();

            //添加Razor视图引擎
            ViewEngines.Engines.Add(new RazorViewEngine());

            GlobalConfiguration.Configure(WebApiConfig.Register);

            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);

            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }
    }
}
