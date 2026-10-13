namespace CapsLang
{
    public interface ISettingsStorage
    {
        bool HasSettings();
        Config Load();
        void Save(Config config);
    }
}
