using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CapsLang
{
    static class Program
    {
        const string AppMutexName = "Local\\CapsLang_SingleInstance_Mutex";
        internal const string QuitEventName = "Local\\CapsLang_Quit_Event";
        internal const string SettingsEventName = "Local\\CapsLang_Settings_Event";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                string arg = args[0].Trim().ToLowerInvariant().TrimStart('-', '/');
                if (arg == "quit")
                {
                    SignalEvent(QuitEventName);
                    return 0;
                }
                return 1;
            }

            bool createdNew;
            Mutex mutex;
            try
            {
                mutex = new Mutex(true, AppMutexName, out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                SignalEvent(SettingsEventName);
                return 0;
            }

            using (mutex)
            {
                if (!createdNew)
                {
                    SignalEvent(SettingsEventName);
                    return 0;
                }

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                    using (AppTrayContext context = new AppTrayContext())
                    {
                        Application.Run(context);
                    }
                }
                finally
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                    }
                }
            }

            return 0;
        }

        static bool SignalEvent(string eventName)
        {
            try
            {
                using (EventWaitHandle evt = EventWaitHandle.OpenExisting(eventName))
                {
                    return evt.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
