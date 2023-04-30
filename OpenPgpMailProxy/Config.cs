using System;
using System.Collections.Generic;
using ThrowException.CSharpLibs.ConfigParserLib;
using ThrowException.CSharpLibs.LogLib;

namespace OpenPgpMailProxy
{
    public class Config : IConfig
    {
        [Setting(Name = "Mailbox")]
        public IEnumerable<MailboxConfig> Mailboxes { get; private set; }

        [Setting]
        public string SmtpBindAddress { get; private set; }

        [Setting]
        public int SmtpBindPort { get; private set; }

        [Setting]
        public string Pop3BindAddress { get; private set; }

        [Setting]
        public int Pop3BindPort { get; private set; }

        [Setting]
        public string MailboxesPath { get; private set; }

        [Setting]
        public string GpgHome { get; private set; }

        [Setting]
        public LogSeverity LogConsoleLevel { get; private set; }

        [Setting]
        public string LogFilePrefix { get; private set; }

        [Setting]
        public LogSeverity LogFileLevel { get; private set; }
    }

    public class MailboxConfig : IConfig
    {
        [Setting]
        public string Pop3ServerAddress { get; private set; }

        [Setting]
        public int Pop3ServerPort { get; private set; }

        [Setting]
        public string SmtpServerAddress { get; private set; }

        [Setting]
        public int SmtpServerPort { get; private set; }

        [Setting]
        public string Username { get; private set; }

        [Setting]
        public string RemotePassword { get; private set; }

        [Setting]
        public string LocalPassword { get; private set; }
    }
}
