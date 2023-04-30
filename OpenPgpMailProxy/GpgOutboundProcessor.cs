using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;
using MimeKit.Cryptography;
using MimeKit.Text;

namespace OpenPgpMailProxy
{
    public class GpgOutboundProcessor : IMailProcessor
    {
        private readonly Context _context;
        private readonly Gpg _gpg;

        public GpgOutboundProcessor(Gpg gpg, Context context)
        {
            _gpg = gpg;
            _context = context;
        }

        private static bool IsValidSignatureKey(GpgKey key)
        {
            return
                key.Usage.HasFlag(GpgKeyUsage.Sign) &&
                (key.Status == GpgKeyStatus.Active);
        }

        private static bool IsValidEncryptionKey(GpgKey key)
        {
            return
                key.Usage.HasFlag(GpgKeyUsage.Sign) &&
                (key.Status == GpgKeyStatus.Active);
        }

        private GpgKey GetSignatureKey(Envelope input)
        {
            var sender = input.Message.From.FirstOrDefault() as MailboxAddress;
            return _gpg.List(sender.Address, true).FirstOrDefault(IsValidSignatureKey);
        }

        private bool MustEncrypt(Envelope input)
        {
            return
                input.Message.Subject.ToLowerInvariant().Contains(GpgTags.SubjectTagEncrypt) ||
                input.Message.Subject.ToLowerInvariant().Contains(GpgTags.SubjectTagEncrypted);
        }

        private bool CanEncrypt(Envelope input)
        {
            return input.Message.To
                .Select(m => m as MailboxAddress)
                .All(m =>
            {
                return _gpg.List(m.Address).Any(IsValidEncryptionKey);
            });
        }

        private IEnumerable<string> GetRecipientIds(Envelope input)
        {
            return input.Message.To
                .Select(m => m as MailboxAddress)
                .Select(m => _gpg.List(m.Address).FirstOrDefault(IsValidEncryptionKey).Id)
                .ToList();
        }

        private byte[] GetBytes(MimeEntity entity)
        {
            using (var stream = new MemoryStream())
            {
                entity.WriteTo(stream);
                return stream.ToArray();
            }
        }

        private byte[] ConvertLineEndings(byte[] data)
        {
            var text = Encoding.UTF8.GetString(data);
            var lines = text.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var newText = string.Join("\r\n", lines);
            var newBytes = Encoding.UTF8.GetBytes(newText);
            return newBytes;
        }

        private MimeEntity AttachPublicKey(MimeEntity body, GpgKey key)
        {
            var keyData = _gpg.ExportKeyBinary(key.Id, true);
            var publicKey = new MimePart("application", "pgp-keys");
            publicKey.Headers[HeaderId.ContentType] += "; name=\"OpenPGP_0x" + key.Id + ".asc\"";
            publicKey.Content = new MimeContent(new MemoryStream(keyData));
            publicKey.ContentTransferEncoding = ContentEncoding.QuotedPrintable;

            if (body is Multipart bodyPart)
            {
                bodyPart.Add(publicKey);
                return bodyPart;
            }
            else
            {
                var newPart = new Multipart("mixed");
                newPart.Add(body);
                newPart.Add(publicKey);
                return newPart;
            }
        }

        private Envelope SignMessage(Envelope input, IMailbox errorBox)
        {
            _context.Log.Verbose("Outbound: Signing message");
            var sender = input.Message.From.FirstOrDefault() as MailboxAddress;
            var signatureKey = GetSignatureKey(input);
            if (signatureKey != null)
            {
                var protectedPart = GetProctectedPart(input, signatureKey);
                var protectedPartBytes = ConvertLineEndings(GetBytes(protectedPart));
                var result = _gpg.Sign(protectedPartBytes, out byte[] signatureBytes, sender.Address, SignatureType.DetachSign, true);

                if (result.Status == GpgStatus.Success)
                {
                    var signedPart = new Multipart("signed");
                    signedPart.Headers[HeaderId.ContentType] += "; micalg=pgp-sha512; protocol=\"application/pgp-signature\"";
                    signedPart.Add(protectedPart);
                    var signaturePart = new MimePart("application", "pgp-signature");
                    signaturePart.Headers[HeaderId.ContentType] += "; name=\"signature.asc\"";
                    signaturePart.Headers.Add(HeaderId.ContentDisposition, "attachment; filename=\"signature.asc\"");
                    signaturePart.Content = new MimeContent(new MemoryStream(signatureBytes));
                    signaturePart.ContentTransferEncoding = ContentEncoding.SevenBit;
                    signedPart.Add(signaturePart);
                    input.Message.Body = signedPart;
                    _context.Log.Notice("Outbound: Signed message returned");
                    return input;
                }
                else
                {
                    _context.Log.Warning("Outbound: Signature failed caused by {0}", result.Information);
                    SendError(input, errorBox, "Signature failed: " + result.Status + "\n\n" + result.Information);
                    return null;
                }
            }
            else
            {
                _context.Log.Notice("Outbound: Signature key not found for {0}", sender.Address);
                return input;
            }
        }

        private string CleanSubject(string subject)
        { 
            foreach (var tag in GpgTags.SubjectTags)
            {
                subject = Regex.Replace(subject, " +" + Regex.Escape(tag) + " +", " ", RegexOptions.CultureInvariant);
                subject = Regex.Replace(subject, " +" + Regex.Escape(tag), " ", RegexOptions.CultureInvariant);
                subject = Regex.Replace(subject, Regex.Escape(tag) + " +", " ", RegexOptions.CultureInvariant);
                subject = Regex.Replace(subject, Regex.Escape(tag), "", RegexOptions.CultureInvariant);
            }
            return subject.Trim();
        }

        private Multipart GetProctectedPart(Envelope input, GpgKey signatureKey)
        {
            var protectedPart = new Multipart("mixed");
            protectedPart.Headers[HeaderId.ContentType] += "; protected-headers=\"v1\"";
            foreach (var header in input.Message.Headers)
            {
                switch (header.Id)
                {
                    case HeaderId.From:
                    case HeaderId.To:
                    case HeaderId.Cc:
                    case HeaderId.MessageId:
                    case HeaderId.Subject:
                        protectedPart.Headers.Add(header);
                        break;
                }
            }
            protectedPart.Add(AttachPublicKey(input.Message.Body, signatureKey));
            return protectedPart;
        }

        private Envelope EncryptMessage(Envelope input, IMailbox errorBox)
        {
            _context.Log.Verbose("Outbound: Encrypting message");
            var sender = input.Message.From.FirstOrDefault() as MailboxAddress;
            var signatureKey = GetSignatureKey(input);
            var recipientIds = GetRecipientIds(input);
            if (signatureKey != null)
            {
                Multipart protectedPart = GetProctectedPart(input, signatureKey);
                var protectedPartBytes = GetBytes(protectedPart);
                var result = _gpg.EncryptAndSign(protectedPartBytes, out byte[] encryptedBytes, recipientIds, sender.Address, true);

                if (result.Status == GpgStatus.Success)
                {
                    var version = new MimePart("application", "pgp-encrypted");
                    version.Headers[HeaderId.ContentDescription] = "PGP/MIME version identification";
                    version.Content = new MimeContent(new MemoryStream(Encoding.ASCII.GetBytes("Version: 1")));
                    version.ContentTransferEncoding = ContentEncoding.SevenBit;

                    var innerEncrypted = new MimePart("application", "octet-stream");
                    innerEncrypted.Headers[HeaderId.ContentType] += "; name=\"encrypted.asc\"";
                    innerEncrypted.Headers[HeaderId.ContentDescription] = "OpenPGP encrypted message";
                    innerEncrypted.Headers[HeaderId.ContentDisposition] = "inline; filename=\"encrypted.asc\"";
                    innerEncrypted.Content = new MimeContent(new MemoryStream(encryptedBytes));
                    innerEncrypted.ContentTransferEncoding = ContentEncoding.QuotedPrintable;

                    var outerEncrypted = new Multipart("encrypted");
                    outerEncrypted.Headers[HeaderId.ContentType] += "; protocol=\"application/pgp-encrypted\"";
                    outerEncrypted.Add(version);
                    outerEncrypted.Add(innerEncrypted);

                    input.Message.Subject = "...";
                    input.Message.Body = outerEncrypted;
                    _context.Log.Notice("Outbound: Encrypted message returned.");
                    return input;
                }
                else
                {
                    _context.Log.Warning("Outbound: Encryption failed caused by {0}", result.Information);
                    SendError(input, errorBox, "Encryption failed: " + result.Status + "\n\n" + result.Information);
                    return null;
                }
            }
            else
            {
                _context.Log.Notice("Outbound: Encryption failed caused by no private key available for {0}", sender.Address);
                SendError(input, errorBox, "Encryption failed: No private key available for " + sender.Address);
                return null;
            }
        }

        public Envelope Process(Envelope input, IMailbox errorBox)
        {
            _context.Log.Info("Outbound: Processing mail");
            if (input.Message.Subject.Contains(GpgTags.SubjectTagUnsigned))
            {
                input.Message.Subject = CleanSubject(input.Message.Subject);
                _context.Log.Notice("Outbound: Forced unsigned mail returned");
                return input;
            }
            else if (CanEncrypt(input))
            {
                if (input.Message.Subject.Contains(GpgTags.SubjectTagUnencrypted))
                {
                    input.Message.Subject = CleanSubject(input.Message.Subject);
                    return SignMessage(input, errorBox);
                }
                else
                {
                    input.Message.Subject = CleanSubject(input.Message.Subject);
                    return EncryptMessage(input, errorBox);
                }
            }
            else if (MustEncrypt(input))
            {
                _context.Log.Notice("Outbound: Required encryption not possible");
                SendError(input, errorBox, "Required encryption not possible");
                return null;
            }
            else
            {
                input.Message.Subject = CleanSubject(input.Message.Subject);
                return SignMessage(input, errorBox);
            }
        }

        private static void SendError(Envelope envelope, IMailbox errorBox, string errorText)
        {
            var text = new TextPart(TextFormat.Plain);
            text.Text = errorText;
            var mail = new MimePart("application", "mail");
            mail.FileName = "message.eml";
            mail.Content = new MimeContent(new MemoryStream(envelope.Bytes));
            mail.ContentTransferEncoding = ContentEncoding.Base64;
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
    }
}
