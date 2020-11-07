using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;

namespace WebAPi.Cache
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            //�Ƴ�������ͼ����
            ViewEngines.Engines.Clear();

            //���Razor��ͼ����
            ViewEngines.Engines.Add(new RazorViewEngine());

            GlobalConfiguration.Configure(WebApiConfig.Register);

            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);

            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }
    }
}
