namespace CapsLang
{
    public static class SettingsStorage
    {
#if PORTABLE
        public static readonly ISettingsStorage Current = new FileSettingsStorage();
#else
        public static readonly ISettingsStorage Current = new RegistrySettingsStorage();
#endif
    }
}
