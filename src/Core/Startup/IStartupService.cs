namespace CapsLang
{
    public interface IStartupService
    {
        bool IsEnabled();
        void SetEnabled(bool enable);
    }
}
