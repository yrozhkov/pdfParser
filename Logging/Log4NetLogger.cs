using System;
using System.Diagnostics;
using log4net;

namespace Logging
{
   

    public class Log4NetLogger : ILogger
    {
        private readonly ILog _logger;
        private string _userName = "[User]-";



        public Log4NetLogger(string loggerName)
        {
            _logger = log4net.LogManager.GetLogger(loggerName);

            var windowsIdentity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (windowsIdentity != null)
                _userName = string.Format("[{0}]-", windowsIdentity.Name);

        }

        #region ILogger Members

        public void Debug(string message, params object[] args)
        {
            if (IsDebugEnabled)
                _logger.Debug(FormatArguments(message, args));
        }

        public void Debug(string message)
        {
            if (IsDebugEnabled)
                _logger.Debug(string.Format("{0}{1}", _userName, message));
        }

        public bool IsDebugEnabled
        {
            get { return _logger.IsDebugEnabled; }
        }

        public void Info(string message, params object[] args)
        {
            if (IsInfoEnabled)
                _logger.Info(FormatArguments(message, args));
        }

        public void Error(string message)
        {
            if (IsErrorEnabled)
                _logger.Error(string.Format("{0}{1}", _userName, message));
        }

        public void Error(string message, params object[] args)
        {
            if (IsErrorEnabled)
                _logger.Error(FormatArguments(message, args));
        }

        public void Error(Exception ex)
        {
            if (IsErrorEnabled)
                _logger.Error(string.Format("{0}{1}", _userName, ex.Message));
        }

        public bool IsErrorEnabled
        {
            get { return _logger.IsErrorEnabled; }
        }

        public void Info(string message)
        {
            if (IsInfoEnabled)
                _logger.Info(string.Format("{0}{1}", _userName, message));
        }

        public void Warn(string message, params object[] args)
        {
            if (IsWarnEnabled)
                _logger.Warn(FormatArguments(message, args));
        }

        public bool IsInfoEnabled
        {
            get { return _logger.IsInfoEnabled; }
        }

        public void Warn(string message)
        {
            if (IsWarnEnabled)
                _logger.Warn(string.Format("{0}{1}", _userName, message));
        }

        public bool IsWarnEnabled
        {
            get { return _logger.IsWarnEnabled; }
        }

        private string FormatArguments(string message, params object[] args)
        {

            message = string.Format("{0}{1}", _userName, message);
            if (args.Length == 0)
            {
                return message;
            }

            try
            {
                return string.Format(message, args);
            }
            catch (FormatException ex)
            {
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                return message + Environment.NewLine +
                       "WARNING: COULD NOT FORMAT MESSAGE PARAMETERS: " + ex.ToString();
            }
        }

        #endregion
    }

}
