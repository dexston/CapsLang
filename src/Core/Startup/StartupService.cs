namespace CapsLang
{
    public static class StartupService
    {
#if PORTABLE
        public static readonly IStartupService Current = new ShortcutStartupService();
#else
        public static readonly IStartupService Current = new RegistryStartupService();
#endif
    }
}
