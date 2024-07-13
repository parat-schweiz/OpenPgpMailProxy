using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ThrowException.CSharpLibs.BytesUtilLib;
using ThrowException.CSharpLibs.LogLib;

namespace OpenPgpMailProxy
{
    public class SmtpSession : Session
    {
        private bool _authorized = false;
        private string _from;
        private List<string> _to;
        private StringBuilder _data;
        private IMailbox _mailbox;
        private MailboxConfig _mailboxConfig;

        public SmtpSession(Context context, TcpClient client)
            : base(context, client)
        {
            _context.Log.Notice("SMTP server: New session");
        }

        protected override void WriteLine(string text)
        {
            base.Write(text + "\r\n");
        }

        protected override string ReadLine()
        {
            var text = base.ReadLine();
            return text;
        }

        protected override void Process()
        {
            WriteLine("220 ESMTP server read");

            bool run = true;
            while (run)
            {
                var command = ReadLine();
                if (string.IsNullOrEmpty(command))
                    return;
                try
                {
                    run = ProcessCommand(command);
                }
                catch (Exception exception)
                {
                    _context.Log.Warning("SMTP server failure: {0}", exception.ToString());
                }
            }
        }

        private IEnumerable<string> SplitNullByte(byte[] data)
        {
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            { 
                if (data[i] == 0)
                {
                    yield return Encoding.UTF8.GetString(data.Part(start, i - start));
                    start = i + 1;
                }
            }
            yield return Encoding.UTF8.GetString(data.Part(start));
        }

        private bool Authenticate(string username, string password)
        {
            var mailboxConfig = _context.Config.Mailboxes.SingleOrDefault(m => m.Username == username);
            if ((mailboxConfig != null) && (mailboxConfig.LocalPassword == password))
            {
                _mailboxConfig = mailboxConfig;
                _mailbox = _context.Mailboxes.Get(_mailboxConfig.Username, MailboxType.OutboundInput);
                _context.Log.Notice("SMTP server: Authentication for {0} successful", username);
                return true;
            }
            else
            {
                _context.Log.Notice("SMTP server: Authentication for {0} failed", username);
                return false;
            }
        }

        private void ProcessAuth(Queue<string> arguments)
        {
            if (arguments.Count < 2)
            {
                WriteLine("501 argument missing");
                return;
            }

            if (arguments.Dequeue() != "PLAIN")
            {
                WriteLine("504 method not implemented");
                return;
            }

            var authBytes = Convert.FromBase64String(arguments.Dequeue());
            var authParts = SplitNullByte(authBytes).ToList();

            if (authParts.Count < 3)
            {
                WriteLine("535 invalid credentials");
                return;
            }

            var username = authParts[1];
            var password = authParts[2];
            if (Authenticate(username, password))
            {
                WriteLine("235 authentication successful");
                _authorized = true;
            }
            else
            {
                WriteLine("535 invalid credentials");
            }
        }

        private void ProcessMail(Queue<string> arguments)
        {
            if (!_authorized)
            {
                WriteLine("530 authentication required");
                _context.Log.Verbose("SMTP server: MAIL failed with authentication required");
                return;
            }

            if (!arguments.Any())
            {
                WriteLine("501 argument missing");
                _context.Log.Verbose("SMTP server: MAIL failed with argument missing");
                return;
            }

            var match = Regex.Match(arguments.Dequeue(), @"^FROM\:\<(.*)\>$");
            if (match.Success)
            {
                _from = match.Groups[1].Value;
                _to = new List<string>();
                _data = new StringBuilder();
                WriteLine("250 Ok");
                _context.Log.Verbose("SMTP server: MAIL succesful");
            }
            else
            {
                WriteLine("501 invalid parameter");
                _context.Log.Verbose("SMTP server: MAIL failed with invalid parameter");
            }
        }

        private void ProcessRcpt(Queue<string> arguments)
        {
            if (!_authorized)
            {
                WriteLine("530 authentication required");
                _context.Log.Verbose("SMTP server: RCPT failed with authentication required");
                return;
            }

            if (!arguments.Any())
            {
                WriteLine("501 argument missing");
                _context.Log.Verbose("SMTP server: RCPT failed with argument missing");
                return;
            }

            var match = Regex.Match(arguments.Dequeue(), @"^TO\:\<(.*)\>$");
            if (match.Success)
            {
                _to.Add(match.Groups[1].Value);
                WriteLine("250 Ok");
                _context.Log.Verbose("SMTP server: RCPT succesful");
            }
            else
            {
                WriteLine("501 invalid parameter");
                _context.Log.Verbose("SMTP server: RCPT failed with invalid parameter");
            }
        }

        private void ProcessData()
        {
            if (!_authorized)
            {
                WriteLine("530 authentication required");
                _context.Log.Verbose("SMTP server: DATA failed with authentication required");
                return;
            }

            WriteLine("354 ready for data");

            while (true)
            {
                var line = ReadLine();
                if (line == ".")
                {
                    var envelope = new Envelope(Guid.NewGuid().ToString(), _data.ToString());
                    _mailbox.Enqueue(envelope);
                    WriteLine("250 Ok");
                    _context.Log.Notice("SMTP server: New mail queued");
                    return;
                }
                else if (line.StartsWith(".", StringComparison.Ordinal))
                {
                    _data.AppendLine(line.Substring(1));
                }
                else
                {
                    _data.AppendLine(line);
                }
            }
        }

        private bool ProcessCommand(string command)
        {
            var parts = new Queue<string>(command.Trim().Split(new string[] { " " }, StringSplitOptions.None));
            var commandWord = parts.Dequeue();
            switch (commandWord)
            {
                case "HELO":
                    WriteLine("250 localhost Hello " + parts.Dequeue());
                    _context.Log.Verbose("SMTP server: HELO sucessful");
                    return true;
                case "EHLO":
                    WriteLine("250-localhost Hello " + parts.Dequeue());
                    WriteLine("250-SIZE 14680064");
                    WriteLine("250 AUTH PLAIN");
                    _context.Log.Verbose("SMTP server: EHLO sucessful");
                    return true;
                case "AUTH":
                    ProcessAuth(parts);
                    return true;
                case "MAIL":
                    ProcessMail(parts);
                    return true;
                case "RCPT":
                    ProcessRcpt(parts);
                    return true;
                case "DATA":
                    ProcessData();
                    return true;
                case "QUIT":
                    WriteLine("221 bye");
                    _context.Log.Verbose("SMTP server: QUIT sucessful");
                    return false;
                default:
                    WriteLine("500 unknown command");
                    _context.Log.Verbose("SMTP server: Unknown command {0}", command);
                    return true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            _context.Log.Info("SMTP server: Session closed");
            if (_mailbox != null)
            {
                _mailbox = null;
            }
            base.Dispose(disposing);
        }
    }
}
