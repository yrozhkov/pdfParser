using System;
using System.Configuration;
using System.Diagnostics;
using log4net.Appender;
using log4net.Config;
using log4net.Layout;

namespace Logging
{
    public static class LogManager
    {

        static LogManager()
        {
            Configure();

        }

        private static void Configure()
        {

            object configuration = null;
            try
            {
                configuration = ConfigurationManager.GetSection("log4net");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            if (configuration == null)
            {
                TraceAppender appender = new TraceAppender();
                appender.ImmediateFlush = true;
                appender.Name = "defaultAppender";
                appender.Layout = new PatternLayout(PatternLayout.DefaultConversionPattern);
                BasicConfigurator.Configure(appender);
            }
            else
            {
                XmlConfigurator.Configure();
            }



        }


        public static ILogger GetLoggerForCallingClass()
        {
            var stack = new StackTrace();
            StackFrame frame = stack.GetFrame(1);
            return new Log4NetLogger(frame.GetMethod().DeclaringType.FullName);
        }
    }
}
