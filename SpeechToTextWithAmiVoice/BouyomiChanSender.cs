using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Diagnostics;


namespace SpeechToTextWithAmiVoice
{
    class BouyomiChanSender
    {
        public const byte Charset = 0;
        public Int16 Voice = 1;
        public Int16 Volume = -1;
        public Int16 Speed = -1;
        public Int16 Tone = -1;
        public const Int16 Command = 0x0001;
        public readonly string Hostname;
        public readonly Int32 Port;
        public bool isEnable { get; protected set; } = true;
        public string LastErrorString { get; protected set; } = "";

        public BouyomiChanSender(string hostname, Int32 port) {
            this.Hostname = hostname;
            this.Port = port;
        }

        public bool Send(string message)
        {
            if (!isEnable)
            {
                return false;
            }

            TcpClient client;

            try
            {
                client = new TcpClient(Hostname, Port);
            }
            catch (Exception ex)
            {
                LastErrorString = ex.Message;
                isEnable = false;
                return false;
            }

            byte[] byteMessage = Encoding.UTF8.GetBytes(message);
            Int32 length = byteMessage.Length;

            try
            {
                using (NetworkStream ns = client.GetStream())
                {
                    using (BinaryWriter bw = new BinaryWriter(ns))
                    {
                        bw.Write(Command);
                        bw.Write(Speed);
                        bw.Write(Tone);
                        bw.Write(Volume);
                        bw.Write(Voice);
                        bw.Write(Charset);
                        bw.Write(length);
                        bw.Write(byteMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                isEnable = false;
                return false;
            }

            return true;
        }
    }
}
