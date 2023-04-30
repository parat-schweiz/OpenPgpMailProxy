using System;
using System.IO;
using System.Linq;
using System.Text;
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Text;

namespace OpenPgpMailProxy
{
    public class SmtpSendTask : IMailTask
    {
        private readonly Context _context;

        public SmtpSendTask(Context context)
        {
            _context = context;
        }

        private bool Send(MailboxConfig config, Envelope envelope, IMailbox errorBox)
        {
            using (var client = new SmtpClient())
            {
                try
                {
                    client.Connect(config.SmtpServerAddress, config.SmtpServerPort);
                    client.Authenticate(config.Username, config.RemotePassword);
                }
                catch (Exception exception)
                {
                    _context.Log.Warning("SMTP client: sending failed caused by ", exception.Message);
                    return false;
                }
                try
                {
                    client.Send(envelope.Message);
                    _context.Log.Info("SMTP client: mail sent");
                    return true;
                }
                catch (SmtpCommandException exception)
                {
                    switch (exception.StatusCode)
                    {
                        case SmtpStatusCode.InsufficientStorage:
                        case SmtpStatusCode.ExceededStorageAllocation:
                            SendError(envelope, errorBox, "Message delivery failed due to insufficient storage.");
                            _context.Log.Info("SMTP client: sending not possible caused by ", exception.StatusCode);
                            return true;
                        case SmtpStatusCode.MailboxUnavailable:
                            SendError(envelope, errorBox, "Message delivery failed due to mailbox unavailable.");
                            _context.Log.Info("SMTP client: sending not possible caused by ", exception.StatusCode);
                            return true;
                        default:
                            _context.Log.Warning("SMTP client: sending failed caused by ", exception.Message);
                            return false;
                    }
                }
                catch (Exception exception)
                {
                    _context.Log.Warning("SMTP client: sending failed caused by ", exception.Message);
                    return false;
                }
            }
        }

        private static void SendError(Envelope envelope, IMailbox errorBox, string errorText)
        {
            var text = new TextPart(TextFormat.Plain);
            text.Text = errorText;
            var mail = new MimePart("application", "mail");
            mail.FileName = "message.eml";
            mail.Content = new MimeContent(new MemoryStream(envelope.Bytes));
            var body = new Multipart();
            body.Add(text);
            body.Add(mail);
            var warning = new MimeMessage();
            warning.From.Add(new MailboxAddress("OpenPGP Mail Proxy", "proxy@localhost"));
            warning.To.Add(envelope.Message.From.First());
            warning.Subject = "Message delivery failed: " + envelope.Message.Subject;
            warning.InReplyTo = envelope.Message.MessageId;
            warning.Body = body;
            errorBox.Enqueue(new Envelope(Guid.NewGuid().ToString(), warning));
        }

        private void Run(MailboxConfig config, IMailbox mailbox, IMailbox errorBox)
        {
            foreach (var envelope in mailbox.List().ToList())
            {
                if (Send(config, envelope, errorBox))
                {
                    mailbox.Delete(envelope);
                }
            }
        }

        private void Run(MailboxConfig config)
        {
            _context.Log.Verbose("SMTP client: sending mails for {0}", config.Username);
            var errorBox = _context.Mailboxes.Get(config.Username, MailboxType.InboundOutput);
            errorBox.Lock();
            try
            {
                var mailbox = _context.Mailboxes.Get(config.Username, MailboxType.OutboundOutput);
                mailbox.Lock();
                try
                {
                    Run(config, mailbox, errorBox);
                }
                finally
                {
                    mailbox.Release();
                }
            }
            finally
            {
                errorBox.Release();
            }
        }

        public void Run()
        {
            _context.Log.Info("SMTP client: running task");
            foreach (var user in _context.Config.Mailboxes)
            {
                Run(user);
            }
        }
    }
}
