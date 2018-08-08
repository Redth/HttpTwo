using System;

namespace HttpTwo.Internal
{
    public static class Log
    {
        static Log ()
        {
            Logger = new DefaultLogger { Level = LogLevel.Info };
        }

        public static ILogger Logger { get;set; }

        public static void Info(string format, params object[] args) => Logger.Info(format, args);

        public static void Debug(string format, params object[] args) => Logger.Debug(format, args);

        public static void Warn(string format, params object[] args) => Logger.Warn(format, args);

        public static void Error(string format, params object[] args) => Logger.Error(format, args);
    }

    public class DefaultLogger : ILogger
    {
        public LogLevel Level { get; set; }

        public void Info (string format, params object[] args)
        {
            if (Level >= LogLevel.Info)
                Write (string.Format (format, args));
        }

        public void Debug (string format, params object[] args)
        {
            if (Level >= LogLevel.Debug)
                Write (string.Format (format, args));
        }

        public void Warn (string format, params object[] args)
        {
            if (Level >= LogLevel.Warn)
                Write (string.Format (format, args));
        }

        public void Error (string format, params object[] args)
        {
            if (Level >= LogLevel.Error)
                Write (string.Format (format, args));
        }

        void Write(string format, params object[] args) => Console.WriteLine(DateTime.Now.ToString("hh:MM:ss.fff tt") + ": " + string.Format(format, args));
    }

    public interface ILogger
    {
        LogLevel Level { get; set; }

        void Info (string format, params object[] args);
        void Debug (string format, params object[] args);
        void Warn (string format, params object[] args);
        void Error (string format, params object[] args);
    }

    public enum LogLevel
    {
        None = 0,
        Error = 1,
        Warn = 2,
        Debug = 3,
        Info = 4,
    }
}
