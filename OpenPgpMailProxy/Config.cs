using System;
using ThrowException.CSharpLibs.ConfigParserLib;

namespace OpenPgpMailProxy
{
    public class Config : IConfig
    {
        [Setting]
        public Pop3ServerConfig Pop3Server { get; private set; }

        [Setting]
        public Pop3ClientConfig Pop3Client { get; private set; }

        [Setting]
        public SmtpServerConfig SmtpServer { get; private set; }

        [Setting]
        public SmtpClientConfig SmtpClient { get; private set; }
    }

    public class Pop3ClientConfig : IConfig
    {
        [Setting]
        public string ServerAddress { get; private set; }

        [Setting]
        public int ServerPort { get; private set; }

        [Setting]
        public string Username { get; private set; }

        [Setting]
        public string Password { get; private set; }
    }

    public class Pop3ServerConfig : IConfig
    {
        [Setting]
        public string BindAddress { get; private set; }

        [Setting]
        public int BindPort { get; private set; }

        [Setting]
        public string Username { get; private set; }

        [Setting]
        public string Password { get; private set; }
    }

    public class SmtpClientConfig : IConfig
    {
        [Setting]
        public string ServerAddress { get; private set; }

        [Setting]
        public int ServerPort { get; private set; }

        [Setting]
        public string Username { get; private set; }

        [Setting]
        public string Password { get; private set; }
    }

    public class SmtpServerConfig : IConfig
    {
        [Setting]
        public string BindAddress { get; private set; }

        [Setting]
        public int BindPort { get; private set; }

        [Setting]
        public string Username { get; private set; }

        [Setting]
        public string Password { get; private set; }
    }
}
