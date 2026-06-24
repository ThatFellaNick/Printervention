/*
  Printervention
  Network discovery helpers for identifying a printer from an IP address.
*/

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Printervention
{
    internal sealed class PrinterDiscovery
    {
        public async Task<PrinterIdentity> DiscoverAsync(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException("Enter a printer IP address first.", "ipAddress");
            }

            IPAddress parsed;
            if (!IPAddress.TryParse(ipAddress.Trim(), out parsed))
            {
                throw new ArgumentException("The printer IP address is not valid.", "ipAddress");
            }

            var identity = await QuerySnmpAsync(parsed).ConfigureAwait(false);
            if (identity.HasUsefulName)
            {
                return identity;
            }

            var httpIdentity = await QueryHttpAsync(parsed).ConfigureAwait(false);
            return httpIdentity.HasUsefulName ? httpIdentity : identity;
        }

        private static async Task<PrinterIdentity> QuerySnmpAsync(IPAddress ipAddress)
        {
            // sysDescr.0 is widely supported and often includes vendor, model, and firmware.
            var sysDescrPacket = SnmpGetPacket("1.3.6.1.2.1.1.1.0");
            var printer = new PrinterIdentity { IpAddress = ipAddress.ToString(), Source = "SNMP" };

            try
            {
                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 2500;
                    await udp.SendAsync(sysDescrPacket, sysDescrPacket.Length, new IPEndPoint(ipAddress, 161)).ConfigureAwait(false);
                    var result = await ReceiveWithTimeoutAsync(udp, 3000).ConfigureAwait(false);
                    printer.RawDescription = DecodeSnmpString(result.Buffer);
                    printer.Model = CleanModel(printer.RawDescription);
                }
            }
            catch
            {
                printer.Source = "SNMP unavailable";
            }

            return printer;
        }

        private static async Task<PrinterIdentity> QueryHttpAsync(IPAddress ipAddress)
        {
            var identity = new PrinterIdentity { IpAddress = ipAddress.ToString(), Source = "HTTP" };

            try
            {
                var request = WebRequest.Create("http://" + ipAddress + "/");
                request.Timeout = 3500;

                using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        return identity;
                    }

                    var buffer = new byte[32768];
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    var html = Encoding.UTF8.GetString(buffer, 0, read);
                    identity.RawDescription = ExtractTitle(html);
                    identity.Model = CleanModel(identity.RawDescription);
                }
            }
            catch
            {
                identity.Source = "HTTP unavailable";
            }

            return identity;
        }

        private static Task<UdpReceiveResult> ReceiveWithTimeoutAsync(UdpClient udp, int timeoutMilliseconds)
        {
            var receiveTask = udp.ReceiveAsync();
            var timeoutTask = Task.Delay(timeoutMilliseconds);
            return Task.WhenAny(receiveTask, timeoutTask).ContinueWith(task =>
            {
                if (task.Result == receiveTask)
                {
                    return receiveTask.Result;
                }

                throw new TimeoutException("No SNMP response was received.");
            });
        }

        private static byte[] SnmpGetPacket(string oid)
        {
            var oidBytes = EncodeOid(oid);
            var varBind = Sequence(Concat(ObjectIdentifier(oidBytes), NullValue()));
            var varBindList = Sequence(varBind);
            var pdu = Tag(0xA0, Concat(Integer(1), Integer(0), Integer(0), varBindList));
            return Sequence(Concat(Integer(0), OctetString("public"), pdu));
        }

        private static byte[] EncodeOid(string oid)
        {
            var parts = Array.ConvertAll(oid.Split('.'), int.Parse);
            var bytes = new System.Collections.Generic.List<byte> { (byte)(parts[0] * 40 + parts[1]) };

            for (var i = 2; i < parts.Length; i++)
            {
                var value = parts[i];
                var encoded = new System.Collections.Generic.Stack<byte>();
                encoded.Push((byte)(value & 0x7F));
                value >>= 7;

                while (value > 0)
                {
                    encoded.Push((byte)((value & 0x7F) | 0x80));
                    value >>= 7;
                }

                bytes.AddRange(encoded);
            }

            return bytes.ToArray();
        }

        private static string DecodeSnmpString(byte[] packet)
        {
            // A minimal BER scan is enough here because the response contains a single requested value.
            for (var i = 0; i < packet.Length - 2; i++)
            {
                var tag = packet[i];
                if (tag != 0x04 && tag != 0x13)
                {
                    continue;
                }

                var length = packet[i + 1];
                if ((length & 0x80) != 0 || length <= 0 || i + 2 + length > packet.Length)
                {
                    continue;
                }

                var value = Encoding.ASCII.GetString(packet, i + 2, length).Trim('\0', ' ', '\r', '\n', '\t');
                if (value.Length > 3 && !value.Equals("public", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string ExtractTitle(string html)
        {
            var match = Regex.Match(html ?? string.Empty, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : string.Empty;
        }

        private static string CleanModel(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(text, @"\s+", " ").Trim();
            cleaned = Regex.Replace(cleaned, @"\b(Web|Printer|Print Server|Embedded Web Server)\b", string.Empty, RegexOptions.IgnoreCase).Trim();
            return cleaned;
        }

        private static byte[] Integer(int value)
        {
            return Tag(0x02, new[] { (byte)value });
        }

        private static byte[] ObjectIdentifier(byte[] value)
        {
            return Tag(0x06, value);
        }

        private static byte[] OctetString(string value)
        {
            return Tag(0x04, Encoding.ASCII.GetBytes(value));
        }

        private static byte[] NullValue()
        {
            return new byte[] { 0x05, 0x00 };
        }

        private static byte[] Sequence(byte[] value)
        {
            return Tag(0x30, value);
        }

        private static byte[] Tag(byte tag, byte[] value)
        {
            return Concat(new[] { tag }, Length(value.Length), value);
        }

        private static byte[] Length(int length)
        {
            if (length < 128)
            {
                return new[] { (byte)length };
            }

            return new[] { (byte)0x81, (byte)length };
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            var total = 0;
            foreach (var array in arrays)
            {
                total += array.Length;
            }

            var output = new byte[total];
            var offset = 0;
            foreach (var array in arrays)
            {
                Buffer.BlockCopy(array, 0, output, offset, array.Length);
                offset += array.Length;
            }

            return output;
        }
    }

    internal sealed class PrinterIdentity
    {
        public string IpAddress { get; set; }
        public string Model { get; set; }
        public string RawDescription { get; set; }
        public string Source { get; set; }

        public bool HasUsefulName
        {
            get { return !string.IsNullOrWhiteSpace(Model); }
        }
    }
}
