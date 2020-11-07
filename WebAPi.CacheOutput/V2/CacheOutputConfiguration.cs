using System;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Web.Http;
using WebAPi.CacheOutput.Core;

namespace WebAPi.CacheOutput.V2
{
    public class CacheOutputConfiguration
    {
        private readonly HttpConfiguration _configuration;

        public CacheOutputConfiguration(HttpConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void RegisterCacheOutputProvider(Func<IApiOutputCache> provider)
        {
            _configuration.Properties.GetOrAdd(typeof(IApiOutputCache), x => provider);
        }

        public void RegisterCacheKeyGeneratorProvider<T>(Func<T> provider)
            where T : ICacheKeyGenerator
        {
            _configuration.Properties.GetOrAdd(typeof(T), x => provider);
        }

        public void RegisterDefaultCacheKeyGeneratorProvider(Func<ICacheKeyGenerator> provider)
        {
            RegisterCacheKeyGeneratorProvider(provider);
        }

        public string MakeBaseCachekey(string controller, string action)
        {
            return string.Format("{0}-{1}", controller.ToLower(), action.ToLower());
        }

        public string MakeBaseCachekey<T, U>(Expression<Func<T, U>> expression)
        {
            if (!(expression.Body is MethodCallExpression method))
            {
                throw new ArgumentException("Expression is wrong");
            }

            var methodName = method.Method.Name;
            var nameAttribs = method.Method.GetCustomAttributes(typeof(ActionNameAttribute), false);
            if (nameAttribs.Any())
            {
                var actionNameAttrib = (ActionNameAttribute)nameAttribs.FirstOrDefault();
                if (actionNameAttrib != null)
                {
                    methodName = actionNameAttrib.Name;
                }
            }

            return string.Format("{0}-{1}", typeof(T).FullName.ToLower(), methodName.ToLower());
        }

        private static ICacheKeyGenerator TryActivateCacheKeyGenerator(Type generatorType)
        {
            var hasEmptyOrDefaultConstructor =
                generatorType.GetConstructor(Type.EmptyTypes) != null ||
                generatorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .Any(x => x.GetParameters().All(p => p.IsOptional));
            return hasEmptyOrDefaultConstructor
                ? Activator.CreateInstance(generatorType) as ICacheKeyGenerator
                : null;
        }

        public ICacheKeyGenerator GetCacheKeyGenerator(HttpRequestMessage request, Type generatorType)
        {
            generatorType = generatorType ?? typeof(ICacheKeyGenerator);
            _configuration.Properties.TryGetValue(generatorType, out object cache);

            var generator = cache is Func<ICacheKeyGenerator> cacheFunc
                ? cacheFunc()
                : request.GetDependencyScope().GetService(generatorType) as ICacheKeyGenerator;

            return generator
                ?? TryActivateCacheKeyGenerator(generatorType)
                ?? new DefaultCacheKeyGenerator();
        }

        public IApiOutputCache GetCacheOutputProvider(HttpRequestMessage request)
        {
            _configuration.Properties.TryGetValue(typeof(IApiOutputCache), out object cache);

            var cacheOutputProvider = cache is Func<IApiOutputCache> cacheFunc ? cacheFunc() : request.GetDependencyScope().GetService(typeof(IApiOutputCache)) as IApiOutputCache ?? new MemoryCacheDefault();
            
            return cacheOutputProvider;
        }
    }
}
