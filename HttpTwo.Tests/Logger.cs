using System;

namespace HttpTwo.Tests
{
    public class ConsoleLogger : ILogger
    {
        public LogLevel Level { get; set; }

        public void Info (string format, params object[] args)
        {
            if (Level >= LogLevel.Info)
                write (string.Format (format, args));
        }

        public void Debug (string format, params object[] args)
        {
            if (Level >= LogLevel.Debug)
                write (string.Format (format, args));
        }

        public void Warn (string format, params object[] args)
        {
            if (Level >= LogLevel.Warn)
                write (string.Format (format, args));
        }

        public void Error (string format, params object[] args)
        {
            if (Level >= LogLevel.Error)
                write (string.Format (format, args));
        }

        void write (string format, params object[] args)
        {
            Console.WriteLine (DateTime.Now.ToString ("hh:MM:ss.fff tt") + ": " + string.Format (format, args));
        }
    }
}

