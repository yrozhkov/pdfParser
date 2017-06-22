using System.Configuration;
using System.IO;
using log4net.Appender;

namespace Logging
{
    /// <summary>
    /// Logs to file specified in App.config
    /// </summary>
    public class UserRollingFileAppender : RollingFileAppender
    {
        public override string File
        {
            get { return base.File; }
            set
            {
                string path = ConfigurationManager.AppSettings["LogPath"] ?? "";
                base.File = Path.Combine(path, value != null ? Path.GetFileName(value) : "Output.log");
            }
        }
    }
}
