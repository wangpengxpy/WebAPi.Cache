using System;

namespace WebAPi.CacheOutput.V2
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class IgnoreCacheOutputAttribute : Attribute
    {
    }
}
