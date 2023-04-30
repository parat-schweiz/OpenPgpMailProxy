using System;
using System.IO;
using System.Net;
using System.Threading;
using MimeKit;
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

            var pop3IpAddress = IPAddress.Parse(config.Pop3BindAddress);
            var pop3EndPoint = new IPEndPoint(pop3IpAddress, config.Pop3BindPort);
            var pop3Server = new Pop3Server(context, pop3EndPoint);

            var smtpIpAddress = IPAddress.Parse(config.SmtpBindAddress);
            var smtpEndpoint = new IPEndPoint(smtpIpAddress, config.SmtpBindPort);
            var smtpServer = new SmtpServer(context, smtpEndpoint);

            var gpg = new LocalGpg("/usr/bin/gpg", config.GpgHome, "--batch", "--trust-model tofu+pgp");
            var inboundProcessor = new GpgInboundProcessor(gpg);
            var outboundProcessor = new GpgOutboundProcessor(gpg);
            var processTask = new MailProcessTask(context, inboundProcessor, outboundProcessor);
            var sendTask = new SmtpSendTask(context);
            var recieveTask = new Pop3RecieveTask(context);
            var runner = new TaskRunner(processTask, sendTask, recieveTask);

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
