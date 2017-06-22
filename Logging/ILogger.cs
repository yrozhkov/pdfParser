using System;

namespace Logging
{
    public interface ILogger
    {
        bool IsDebugEnabled { get; }
        bool IsErrorEnabled { get; }
        bool IsInfoEnabled { get; }
        bool IsWarnEnabled { get; }

        void Debug(string message, params object[] args);
        void Debug(string message);

        void Info(string message);
        void Info(string message, params object[] args);

        void Error(string message);
        void Error(string message, params object[] args);
        void Error(Exception ex);


        void Warn(string message, params object[] args);
        void Warn(string message);
    }
}
