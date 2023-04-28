using System;
using System.IO;
using System.Net;
using System.Threading;
using ThrowException.CSharpLibs.ConfigParserLib;

namespace OpenPgpMailProxy
{
    public static class MainClass
    {
        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Config file argument missing");
                Environment.Exit(-1);
            }

            var configFilename = args[0];
            if (!File.Exists(configFilename))
            {
                Console.Error.WriteLine("Config file {0} not found", configFilename);
                Environment.Exit(-1);
            }

            var parser = new XmlConfig<Config>();
            var config = parser.ParseFile(configFilename);
            var context = new Context(config);

            var queue = new MemoryMailQueue();

            var pop3IpAddress = IPAddress.Parse(config.Pop3Server.BindAddress);
            var pop3EndPoint = new IPEndPoint(pop3IpAddress, config.Pop3Server.BindPort);
            var pop3Server = new Pop3Server(context, pop3EndPoint, queue);

            var smtpIpAddress = IPAddress.Parse(config.SmtpServer.BindAddress);
            var smtpEndpoint = new IPEndPoint(smtpIpAddress, config.SmtpServer.BindPort);
            var smtpServer = new SmtpServer(context, smtpEndpoint, queue);

            while (true)
            {
                bool progress = false;

                progress |= pop3Server.Process();
                progress |= smtpServer.Process();

                if (!progress)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
