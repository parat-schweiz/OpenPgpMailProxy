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
        private const string SubjectTagEncrypt = "[encrypt]";
        private const string SubjectTagEncrypted = "[encrypted]";
        private string[] SubjectTags = new string[] {
            SubjectTagEncrypt,
            SubjectTagEncrypted,
        };

        private readonly Gpg _gpg;

        public GpgOutboundProcessor(Gpg gpg)
        {
            _gpg = gpg;
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
                input.Message.Subject.ToLowerInvariant().Contains(SubjectTagEncrypt) ||
                input.Message.Subject.ToLowerInvariant().Contains(SubjectTagEncrypted);
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

        private byte[] GetBytes(Multipart part)
        {
            using (var stream = new MemoryStream())
            {
                part.WriteTo(stream);
                return stream.ToArray();
            }
        }

        private byte[] GetBytes(MimePart part)
        {
            using (var stream = new MemoryStream())
            {
                part.WriteTo(stream);
                return stream.ToArray();
            }
        }

        private MimeEntity AttachPublicKey(MimeEntity body, GpgKey key)
        {
            var keyData = _gpg.ExportKeyBinary(key.Id, true);
            var publicKey = new MimePart("application", "pgp-keys");
            publicKey.Headers[HeaderId.ContentType] += "; name=\"OpenPGP_0x" + key.Id + ".asc\"";
            publicKey.Content = new MimeContent(new MemoryStream(keyData), ContentEncoding.SevenBit);

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
            var sender = input.Message.From.FirstOrDefault() as MailboxAddress;
            var signatureKey = GetSignatureKey(input);

            Multipart protectedPart = GetProctectedPart(input, signatureKey);
            var protectedPartBytes = GetBytes(protectedPart);
            var result = _gpg.Sign(protectedPartBytes, out byte[] signatureBytes, sender.Address, SignatureType.DetachSign, true);

            if (result.Status == GpgStatus.Success)
            {
                var signedPart = new Multipart("signed");
                signedPart.Headers[HeaderId.ContentType] += "; micalg=pgp-sha256; protocol=\"application/pgp-signature\"";
                signedPart.Add(protectedPart);
                var signaturePart = new MimePart("application", "pgp-signature");
                signaturePart.Headers[HeaderId.ContentType] += "; name=\"OpenPGP_signature.asc\"";
                signaturePart.Headers.Add(HeaderId.ContentDescription, "OpenPGP digital signature");
                signaturePart.Headers.Add(HeaderId.ContentDisposition, "attachment; filename=\"OpenPGP_signature\"");
                signaturePart.Content = new MimeContent(new MemoryStream(signatureBytes), ContentEncoding.SevenBit);
                signedPart.Add(signaturePart);
                input.Message.Body = signedPart;
                return input;
            }
            else
            {
                SendError(input, errorBox, "Signature failed: " + result.Status + "\n\n" + result.Information);
                return null;
            }
        }

        private string CleanSubject(string subject)
        { 
            foreach (var tag in SubjectTags)
            {
                subject = Regex.Replace(subject, " +" + tag + " +", " ", RegexOptions.CultureInvariant);
                subject = Regex.Replace(subject, " +" + tag, " ", RegexOptions.CultureInvariant);
                subject = Regex.Replace(subject, tag + " +", " ", RegexOptions.CultureInvariant);
                subject = Regex.Replace(subject, tag, "", RegexOptions.CultureInvariant);
            }
            return subject;
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
                        protectedPart.Headers.Add(header);
                        break;
                    case HeaderId.Subject:
                        protectedPart.Headers.Add(HeaderId.Subject, CleanSubject(input.Message.Subject));
                        break;
                }
            }
            protectedPart.Add(AttachPublicKey(input.Message.Body, signatureKey));
            return protectedPart;
        }

        private Envelope EncryptMessage(Envelope input, IMailbox errorBox)
        {
            var sender = input.Message.From.FirstOrDefault() as MailboxAddress;
            var signatureKey = GetSignatureKey(input);
            var recipientIds = GetRecipientIds(input);

            Multipart protectedPart = GetProctectedPart(input, signatureKey);
            var protectedPartBytes = GetBytes(protectedPart);
            var result = _gpg.EncryptAndSign(protectedPartBytes, out byte[] encryptedBytes, recipientIds, sender.Address, true);

            if (result.Status == GpgStatus.Success)
            {
                var version = new MimePart("application", "pgp-encrypted");
                version.Headers[HeaderId.ContentDescription] = "PGP/MIME version identification";
                version.Content = new MimeContent(new MemoryStream(Encoding.ASCII.GetBytes("Version: 1")), ContentEncoding.SevenBit);

                var innerEncrypted = new MimePart("application", "octet-stream");
                innerEncrypted.Headers[HeaderId.ContentType] += "; name=\"encrypted.asc\"";
                innerEncrypted.Headers[HeaderId.ContentDescription] = "OpenPGP encrypted message";
                innerEncrypted.Headers[HeaderId.ContentDisposition] = "inline; filename=\"encrypted.asc\"";
                innerEncrypted.Content = new MimeContent(new MemoryStream(encryptedBytes), ContentEncoding.SevenBit);

                var outerEncrypted = new Multipart("encrypted");
                outerEncrypted.Headers[HeaderId.ContentType] += "; protocol=\"application/pgp-encrypted\"";
                outerEncrypted.Add(version);
                outerEncrypted.Add(innerEncrypted);

                input.Message.Subject = "...";
                input.Message.Body = outerEncrypted;
                return input;
            }
            else
            {
                SendError(input, errorBox, "Encryption failed: " + result.Status + "\n\n" + result.Information);
                return null;
            }
        }

        public Envelope Process(Envelope input, IMailbox errorBox)
        {
            if (CanEncrypt(input))
            {
                return EncryptMessage(input, errorBox);
            }
            else if (MustEncrypt(input))
            {
                SendError(input, errorBox, "Required encryption not possible");
                return null;
            }
            else
            {
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
