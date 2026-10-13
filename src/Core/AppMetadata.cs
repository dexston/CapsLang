using System;
using System.Linq;
using System.Reflection;

namespace CapsLang
{
    public static class AppMetadata
    {
        public static readonly string Title =
            typeof(AppMetadata).Assembly
                .GetCustomAttribute<AssemblyProductAttribute>()
                .Product;

        public static readonly string Version =
            typeof(AppMetadata).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;

        public static readonly string RepositoryUrl =
            typeof(AppMetadata).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(a => string.Equals(a.Key, "RepositoryUrl", StringComparison.OrdinalIgnoreCase))
                .Value;

        public static readonly string Copyright =
            typeof(AppMetadata).Assembly
                .GetCustomAttribute<AssemblyCopyrightAttribute>()
                .Copyright;
    }
}
