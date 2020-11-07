using System.Net.Http.Headers;
using System.Web.Http.Controllers;

namespace WebAPi.CacheOutput.V2
{
    public class PerUserCacheKeyGenerator : DefaultCacheKeyGenerator
    {
        public override string MakeCacheKey(HttpActionContext context, MediaTypeHeaderValue mediaType, bool excludeQueryString = false)
        {
            var baseKey = MakeBaseKey(context);
            var parameters = FormatParameters(context, excludeQueryString);
            var userIdentity = FormatUserIdentity(context);

            return $"{baseKey}{parameters}:{userIdentity}:{mediaType}";
        }

        protected virtual string FormatUserIdentity(HttpActionContext context)
        {
            return context.RequestContext.Principal.Identity.Name.ToLower();
        }
    }
}
