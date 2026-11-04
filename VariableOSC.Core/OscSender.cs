using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VariableOSC
{
    public class OscSender : IDisposable
    {
        private readonly UdpClient udpClient;
        private readonly IPEndPoint destination;

        public OscSender(string ipAddress, int port)
        {
            if (!IPAddress.TryParse(ipAddress, out IPAddress? ip))
            {
                throw new ArgumentException(
                    "The IP address is not valid.");
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "The port must be between 1 and 65535.");
            }

            destination = new IPEndPoint(ip, port);
            udpClient = new UdpClient();
        }

        public void SendValue(string address, object value, DataType type)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException(
                    "The OSC address cannot be empty.");
            }

            if (!address.StartsWith("/"))
            {
                throw new ArgumentException(
                    "An OSC address must begin with '/'.");
            }

            byte[] packet = BuildPacket(
                address,
                value,
                type);

            udpClient.Send(
                packet,
                packet.Length,
                destination);
        }

        private static byte[] BuildPacket(
            string address,
            object value,
            DataType type)
        {
            List<byte> packet = new List<byte>();

            AddOscString(packet, address);

            switch (type)
            {
                case DataType.Byte:
                case DataType.Int16:
                case DataType.Int32:
                    AddOscString(packet, ",i");
                    AddInt32(packet, Convert.ToInt32(value));
                    break;

                case DataType.Int64:
                    AddOscString(packet, ",h");
                    AddInt64(packet, Convert.ToInt64(value));
                    break;

                case DataType.Float:
                    AddOscString(packet, ",f");
                    AddFloat(packet, Convert.ToSingle(value));
                    break;

                case DataType.Double:
                    AddOscString(packet, ",d");
                    AddDouble(packet, Convert.ToDouble(value));
                    break;

                case DataType.Bool:
                    bool boolValue = Convert.ToBoolean(value);

                    AddOscString(
                        packet,
                        boolValue ? ",T" : ",F");
                    break;

                case DataType.String:
                    AddOscString(packet, ",s");
                    AddOscString(packet, Convert.ToString(value) ?? "");
                    break;

                default:
                    throw new NotSupportedException(
                        $"OSC output does not currently support {type}.");
            }

            return packet.ToArray();
        }

        private static void AddOscString(
            List<byte> packet,
            string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);

            packet.AddRange(bytes);
            packet.Add(0);

            while (packet.Count % 4 != 0)
            {
                packet.Add(0);
            }
        }

        private static void AddInt32(
            List<byte> packet,
            int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            packet.AddRange(bytes);
        }

        private static void AddInt64(
            List<byte> packet,
            long value)
        {
            byte[] bytes = BitConverter.GetBytes(value);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            packet.AddRange(bytes);
        }

        private static void AddFloat(
            List<byte> packet,
            float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            packet.AddRange(bytes);
        }

        private static void AddDouble(
            List<byte> packet,
            double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            packet.AddRange(bytes);
        }

        public void Dispose()
        {
            udpClient.Dispose();
        }
    }
}
