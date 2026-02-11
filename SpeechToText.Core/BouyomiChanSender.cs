using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Diagnostics;


namespace SpeechToText.Core
{
    public class BouyomiChanSender
    {
        public const byte Charset = 0;
        public Int16 Voice = 1;  //  0:棒読みちゃん画面上の設定、1:女性1、2:女性2、3:男性1、4:男性2、5:中性、6:ロボット、7:機械1、8:機械2、10001～:SAPI5）
        public Int16 Volume = -1;
        public Int16 Speed = -1;
        public Int16 Tone = -1;
        public const Int16 Command = 0x0001;
        public readonly string Hostname;
        public readonly Int32 Port;
        public readonly Int16 voice;
        public bool isEnable { get; protected set; } = true;
        public string LastErrorString { get; protected set; } = "";


        public BouyomiChanSender(string hostname, Int32 port, Int16 voice = -1) {
            this.Hostname = hostname;
            this.Port = port;
            this.voice = voice;
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
                        bw.Write(voice);
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
